using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Marketing.Domain;

namespace Modulix.Modules.Marketing.Records;

/// <summary>
/// Müşteri yolculuğu kayıt deposu (ADR-0056). Kod ve hedef türü oluşturulduktan sonra değişmez; giriş segmenti etkin (yayımlanmış) ve aynı hedef
/// türünde olmalıdır, yolculuk taslak dışındayken değişmez. Durum yalnızca iş akışıyla yazılır (genel PUT yok sayar). Çıkış koşulu bu dilimde yalnızca
/// JSON nesnesi olarak denetlenir; alan ve operatör doğrulaması adımların bağlandığı dilimde gelir. Yayımlanmış ya da kaydı olan yolculuk silinmez.
/// </summary>
internal sealed partial class JourneyRecordStore(MarketingStoreContext context) : MarketingRecordStore<Journey>(context)
{
    private readonly Dictionary<Guid, (string Code, string Target, Guid SegmentId, string Status)> before = [];

    public override string EntityKey => MarketingModule.Entities.Journey;

    protected override string ReadPermission => MarketingModule.Permissions.JourneysRead;

    protected override string WritePermission => MarketingModule.Permissions.JourneysWrite;

    protected override IReadOnlyList<RecordField<Journey>> Fields { get; } =
    [
        Field.Text<Journey>("code", Journey.CodeMaxLength, j => j.Code, (j, v) => j.Code = v!.ToLowerInvariant(), required: true),
        Field.Text<Journey>("name", Journey.NameMaxLength, j => j.Name, (j, v) => j.Name = v!, required: true),
        Field.Text<Journey>("description", Journey.DescriptionMaxLength, j => j.Description, (j, v) => j.Description = v),
        Field.Choice<Journey>("target", SegmentTargets.All, j => j.Target, (j, v) => j.Target = v),
        Field.Reference<Journey, Segment>("segmentId", MarketingMessages.NotFound.Segment, j => j.SegmentId, (j, v) => j.SegmentId = v ?? Guid.Empty, required: true),
        Field.Choice<Journey>("reentryPolicy", JourneyReentryPolicies.All, j => j.ReentryPolicy, (j, v) => j.ReentryPolicy = v),
        Field.Text<Journey>("exitCondition", Journey.ExitConditionMaxLength, j => j.ExitCondition, (j, v) => j.ExitCondition = v),
        Field.Owner<Journey>("ownerId", j => j.OwnerId, (j, v) => j.OwnerId = v),
        Field.Choice<Journey>("status", JourneyStatuses.All, j => j.Status, (j, v) => j.Status = v).AsSystem(),
        Field.Id<Journey>("createdBy", j => j.CreatedBy, (j, v) => j.CreatedBy = v).AsSystem(),
        Field.Id<Journey>("updatedBy", j => j.UpdatedBy, (j, v) => j.UpdatedBy = v).AsSystem(),
    ];

    protected override IReadOnlyDictionary<string, RecordFilter<Journey>> Filters { get; } = new Dictionary<string, RecordFilter<Journey>>
    {
        ["status"] = (q, v, _) => JourneyStatuses.All.Contains(v) ? q.Where(x => x.Status == v) : null,
        ["target"] = (q, v, _) => SegmentTargets.All.Contains(v) ? q.Where(x => x.Target == v) : null,
        ["segmentId"] = (q, v, _) => FilterValue.Id(v) is { } id ? q.Where(x => x.SegmentId == id) : null,
        ["ownerId"] = (q, v, c) => FilterValue.User(v, c) is { } id ? q.Where(x => x.OwnerId == id) : null,
    };

    protected override IOrderedQueryable<Journey> Order(IQueryable<Journey> source) => source.OrderBy(j => j.Name);

    protected override IReadOnlyCollection<string>? SortKeys { get; } = ["name", "-name", "status", "-status", "target", "-target", "createdAt", "-createdAt"];

    protected override IOrderedQueryable<Journey> Order(IQueryable<Journey> source, string? sort) => sort switch
    {
        "-name" => source.OrderByDescending(j => j.Name),
        "status" => source.OrderBy(j => j.Status).ThenBy(j => j.Name),
        "-status" => source.OrderByDescending(j => j.Status).ThenBy(j => j.Name),
        "target" => source.OrderBy(j => j.Target).ThenBy(j => j.Name),
        "-target" => source.OrderByDescending(j => j.Target).ThenBy(j => j.Name),
        "createdAt" => source.OrderBy(j => j.CreatedAt).ThenBy(j => j.Name),
        "-createdAt" => source.OrderByDescending(j => j.CreatedAt).ThenBy(j => j.Name),
        _ => Order(source),
    };

    protected override IQueryable<Journey> Search(IQueryable<Journey> source, string term) =>
        source.Where(j => j.Name.Contains(term) || j.Code.Contains(term));

    protected override void OnUpdating(Journey entity) => before[entity.Id] = (entity.Code, entity.Target, entity.SegmentId, entity.Status);

    protected override async Task AppliedAsync(Journey entity, JsonObject values, bool isNew, CancellationToken cancellationToken)
    {
        var errors = new List<RecordFieldError>();
        if (!CodePattern().IsMatch(entity.Code))
        {
            errors.Add(new RecordFieldError("code", MarketingMessages.Journey.CodeFormat));
        }

        var segmentChanged = isNew;
        if (!isNew && before.TryGetValue(entity.Id, out var old))
        {
            if (old.Code != entity.Code)
            {
                errors.Add(new RecordFieldError("code", MarketingMessages.Journey.CodeLocked));
            }

            if (old.Target != entity.Target)
            {
                errors.Add(new RecordFieldError("target", MarketingMessages.Journey.TargetLocked));
            }

            segmentChanged = old.SegmentId != entity.SegmentId;
            if (segmentChanged && entity.Status != JourneyStatuses.Draft)
            {
                errors.Add(new RecordFieldError("segmentId", MarketingMessages.Journey.SegmentLocked));
            }
        }

        // Yolculuğu etkinleştiren geçiş (yayın, sürdürme) yalnızca giriş segmenti hâlâ etkinken yapılır.
        var activating = !isNew && Context.Gate.IsOpen && entity.Status == JourneyStatuses.Active
            && before.TryGetValue(entity.Id, out var previous) && previous.Status != JourneyStatuses.Active;
        if (activating && !await Context.Data.Set<Segment>().AsNoTracking().AnyAsync(s => s.Id == entity.SegmentId && s.Status == SegmentStatuses.Active, cancellationToken))
        {
            throw new RecordConflictException(MarketingMessages.Journey.SegmentNotActive);
        }

        if (activating)
        {
            // Giriş işi segmenti, yolculuğu etkinleştirenin okuma izinleriyle değerlendirir (yetki yükseltme yok).
            entity.EntryPermissions = EntryPermissionSet.Serialize(await Context.Permissions.GetPermissionsAsync(cancellationToken));
        }

        if (!string.IsNullOrWhiteSpace(entity.ExitCondition) && !IsJsonObject(entity.ExitCondition))
        {
            errors.Add(new RecordFieldError("exitCondition", MarketingMessages.Journey.ExitConditionInvalid));
        }

        if (errors.Count == 0 && segmentChanged)
        {
            var segment = await Context.Data.Set<Segment>().AsNoTracking()
                .Where(s => s.Id == entity.SegmentId).Select(s => new { s.Status, s.Target }).SingleOrDefaultAsync(cancellationToken);
            if (segment is null || segment.Status != SegmentStatuses.Active)
            {
                errors.Add(new RecordFieldError("segmentId", MarketingMessages.Journey.SegmentNotUsable));
            }
            else if (segment.Target != entity.Target)
            {
                errors.Add(new RecordFieldError("segmentId", MarketingMessages.Journey.SegmentTargetMismatch));
            }
        }

        if (errors.Count > 0)
        {
            throw new RecordValidationException(errors);
        }

        if (await Context.Data.Set<Journey>().AsNoTracking().AnyAsync(j => j.Code == entity.Code && j.Id != entity.Id, cancellationToken))
        {
            throw new RecordConflictException(MarketingMessages.Journey.CodeTaken);
        }

        entity.UpdatedBy = Context.CurrentUserId;
        if (isNew)
        {
            entity.CreatedBy = Context.CurrentUserId;
        }
    }

    protected override async Task BeforeDeleteAsync(Journey entity, CancellationToken cancellationToken)
    {
        if (entity.Status != JourneyStatuses.Draft
            || await Context.Data.Set<JourneyEnrollment>().AnyAsync(e => e.JourneyId == entity.Id, cancellationToken))
        {
            throw new RecordConflictException(MarketingMessages.Journey.NoDelete);
        }
    }

    private static bool IsJsonObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) is JsonObject;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")]
    private static partial Regex CodePattern();
}

/// <summary>
/// Yolculuk kayıt deposu (ADR-0056): bir hedefin yolculuktaki kaydı. Kayıtlar yalnızca yolculuk girişiyle (iş akışı kapısı açıkken) oluşur ve değişir;
/// elle oluşturulamaz, değiştirilemez ya da silinemez. Görünürlük yolculuğun sorumlusuna bağlıdır.
/// </summary>
internal sealed class JourneyEnrollmentRecordStore(MarketingStoreContext context) : MarketingRecordStore<JourneyEnrollment>(context)
{
    public override string EntityKey => MarketingModule.Entities.JourneyEnrollment;

    protected override string ReadPermission => MarketingModule.Permissions.JourneysRead;

    protected override string WritePermission => MarketingModule.Permissions.JourneysWrite;

    protected override bool DefaultsOwnerToCurrentUser => false;

    protected override bool UsesScope => true;

    protected override string ScopeEntityKey => MarketingModule.Entities.Journey;

    protected override IReadOnlyList<RecordField<JourneyEnrollment>> Fields { get; } =
    [
        Field.Reference<JourneyEnrollment, Journey>("journeyId", MarketingMessages.NotFound.Journey, e => e.JourneyId, (e, v) => e.JourneyId = v ?? Guid.Empty, required: true, createOnly: true),
        Field.Id<JourneyEnrollment>("targetId", e => e.TargetId, (e, v) => e.TargetId = v ?? Guid.Empty),
        Field.Choice<JourneyEnrollment>("status", EnrollmentStatuses.All, e => e.Status, (e, v) => e.Status = v).AsSystem(),
        Field.Time<JourneyEnrollment>("enteredAt", e => e.EnteredAt, (e, v) => e.EnteredAt = v ?? DateTimeOffset.UtcNow).AsSystem(),
        Field.Time<JourneyEnrollment>("exitedAt", e => e.ExitedAt, (e, v) => e.ExitedAt = v).AsSystem(),
        Field.Text<JourneyEnrollment>("exitReason", JourneyEnrollment.ExitReasonMaxLength, e => e.ExitReason, (e, v) => e.ExitReason = v).AsSystem(),
        Field.Time<JourneyEnrollment>("segmentLeftAt", e => e.SegmentLeftAt, (e, v) => e.SegmentLeftAt = v).AsSystem(),
    ];

    protected override IReadOnlyDictionary<string, RecordFilter<JourneyEnrollment>> Filters { get; } = new Dictionary<string, RecordFilter<JourneyEnrollment>>
    {
        ["journeyId"] = (q, v, _) => FilterValue.Id(v) is { } id ? q.Where(x => x.JourneyId == id) : null,
        ["targetId"] = (q, v, _) => FilterValue.Id(v) is { } id ? q.Where(x => x.TargetId == id) : null,
        ["status"] = (q, v, _) => EnrollmentStatuses.All.Contains(v) ? q.Where(x => x.Status == v) : null,
    };

    protected override IOrderedQueryable<JourneyEnrollment> Order(IQueryable<JourneyEnrollment> source) => source.OrderByDescending(e => e.EnteredAt);

    protected override IQueryable<JourneyEnrollment> Search(IQueryable<JourneyEnrollment> source, string term) => source.Where(e => e.ExitReason != null && e.ExitReason.Contains(term));

    protected override IQueryable<JourneyEnrollment> ScopeToOwners(IQueryable<JourneyEnrollment> source, IReadOnlySet<Guid> owners)
    {
        var ids = owners.Select(o => (Guid?)o).ToList();
        var journeys = Context.Data.Set<Journey>().AsNoTracking();
        return source.Where(e => journeys.Any(j => j.Id == e.JourneyId && ids.Contains(j.OwnerId)));
    }

    protected override async Task<bool> IsInScopeAsync(RecordData record, IReadOnlySet<Guid> owners, CancellationToken cancellationToken)
    {
        if (!record.Values.TryGetPropertyValue("journeyId", out var node) || node is not JsonValue value
            || !value.TryGetValue<string>(out var text) || !Guid.TryParse(text, out var journeyId))
        {
            return false;
        }

        var ids = owners.Select(o => (Guid?)o).ToList();
        return await Context.Data.Set<Journey>().AsNoTracking().AnyAsync(j => j.Id == journeyId && ids.Contains(j.OwnerId), cancellationToken);
    }

    protected override Task<JsonObject> PrepareNewAsync(JourneyEnrollment entity, JsonObject values, CancellationToken cancellationToken) =>
        Context.Gate.IsOpen
            ? Task.FromResult(values)
            : throw new RecordValidationException([new RecordFieldError("journeyId", MarketingMessages.Journey.EnrollmentViaEntry)]);

    protected override Task AppliedAsync(JourneyEnrollment entity, JsonObject values, bool isNew, CancellationToken cancellationToken) =>
        isNew || Context.Gate.IsOpen ? Task.CompletedTask : throw new RecordConflictException(MarketingMessages.Journey.EnrollmentLocked);

    protected override Task BeforeDeleteAsync(JourneyEnrollment entity, CancellationToken cancellationToken) =>
        throw new RecordConflictException(MarketingMessages.Journey.EnrollmentNoDelete);
}
