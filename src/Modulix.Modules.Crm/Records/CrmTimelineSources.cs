using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Persistence;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Crm.Contracts.Timeline;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Quotes;
using Modulix.Shared.Crm;

namespace Modulix.Modules.Crm.Records;

/// <summary>
/// Müşteri zaman çizelgesinin CRM kaynakları (CRM eksik süreçler E9, dilim 1): aktivite, fırsat ve teklif. Satırlar kayıt boru hattından okunur; izin,
/// veri kapsamı, lisans ve özel aktivite maskesi orada uygulanır. Boru hattı oluşturulma anını dışarı vermediği için o an, boru hattının zaten
/// görünür bulduğu kayıt kimlikleriyle modülün kendi tablosundan okunur (yeni kayıt alanı açılmaz).
/// </summary>
internal abstract class CrmTimelineSource(IRecordPipeline pipeline) : ICustomerTimelineSource
{
    public abstract string Key { get; }

    protected abstract string EntityKey { get; }

    /// <summary>Firmaya göre süzgeç, en yeniden eskiye sıralama ve (verildiyse) imleç.</summary>
    protected abstract Dictionary<string, string> Filters(CustomerTimelineQuery query, string? before);

    protected abstract Task<IReadOnlyList<CustomerTimelineEntry>> ToEntriesAsync(IReadOnlyList<RecordData> items, CancellationToken cancellationToken);

    public async Task<CustomerTimelineSlice?> ReadAsync(CustomerTimelineQuery query, CancellationToken cancellationToken)
    {
        var before = query.Before?.ToString("O", CultureInfo.InvariantCulture);
        var take = Math.Clamp(query.Take, 1, RecordQuery.MaxPageSize);
        var page = await pipeline.QueryAsync(new RecordQuery(EntityKey, 1, take, Filters: Filters(query, before)), cancellationToken);
        if (!page.IsSuccess)
        {
            // Çağıran bu türü okuyamıyorsa kaynak yoktur; başka hata sessizce boş çizelgeye çevrilmez.
            return page.Error!.Code is RecordErrorCode.PermissionDenied or RecordErrorCode.FeatureNotLicensed
                ? null
                : throw new InvalidOperationException($"Zaman çizelgesi okunamadı ({Key}): {page.Error.Message}");
        }

        var entries = (await ToEntriesAsync(page.Value!.Items, cancellationToken)).ToList();
        var more = page.Value.TotalCount > page.Value.Items.Count;

        // Durum değişikliği satırları (dilim 4): kayıt satırının yanında, aynı kaynağın kendi satırları olarak gelir.
        var (statusRows, statusMore) = await StatusRowsAsync(query, take, cancellationToken);
        entries.AddRange(statusRows);
        more |= statusMore;
        return new CustomerTimelineSlice([.. entries.OrderByDescending(e => e.OccurredAt).ThenBy(e => e.RecordId)], more);
    }

    /// <summary>Kaynağın durum değişikliği satırları (en çok <paramref name="take"/> adet, imleçten önce); ikinci değer daha eski satır kalıp kalmadığıdır.</summary>
    protected virtual Task<(IReadOnlyList<CustomerTimelineEntry> Entries, bool HasMore)> StatusRowsAsync(
        CustomerTimelineQuery query, int take, CancellationToken cancellationToken) =>
        Task.FromResult<(IReadOnlyList<CustomerTimelineEntry>, bool)>(([], false));

    /// <summary>
    /// Firmanın bu türden, çağıranın görebildiği bütün kayıtları (kayıt boru hattıyla: izin ve veri kapsamı uygulanır). Durum satırları yalnızca bu
    /// kayıtlar için üretilir; görünmeyen kaydın geçmişi çizelgeye girmez. En çok <see cref="MaxVisible"/> kayıt okunur.
    /// </summary>
    protected async Task<Dictionary<Guid, RecordData>> VisibleRecordsAsync(CustomerTimelineQuery query, CancellationToken cancellationToken)
    {
        var visible = new Dictionary<Guid, RecordData>();
        var filters = new Dictionary<string, string> { ["companyId"] = query.CompanyId.ToString() };
        for (var pageNo = 1; visible.Count < MaxVisible; pageNo++)
        {
            var page = await pipeline.QueryAsync(new RecordQuery(EntityKey, pageNo, RecordQuery.MaxPageSize, Filters: filters), cancellationToken);
            if (!page.IsSuccess)
            {
                return page.Error!.Code is RecordErrorCode.PermissionDenied or RecordErrorCode.FeatureNotLicensed
                    ? []
                    : throw new InvalidOperationException($"Zaman çizelgesi okunamadı ({Key}): {page.Error.Message}");
            }

            foreach (var item in page.Value!.Items)
            {
                visible[item.Id] = item;
            }

            if (page.Value.Items.Count == 0 || visible.Count >= page.Value.TotalCount)
            {
                break;
            }
        }

        return visible;
    }

    private const int MaxVisible = 1000;

    /// <summary>Açık iş süzgeci (<c>open=true</c>); türün açık işi yoksa <c>null</c>.</summary>
    protected virtual bool CountsOpen => false;

    public async Task<int?> CountOpenAsync(CustomerTimelineQuery query, CancellationToken cancellationToken)
    {
        if (!CountsOpen)
        {
            return null;
        }

        var filters = Filters(query, before: null);
        filters.Remove("sort");
        filters["open"] = "true";
        var page = await pipeline.QueryAsync(new RecordQuery(EntityKey, 1, 1, Filters: filters), cancellationToken);
        return page.IsSuccess
            ? page.Value!.TotalCount
            : page.Error!.Code is RecordErrorCode.PermissionDenied or RecordErrorCode.FeatureNotLicensed
                ? null
                : throw new InvalidOperationException($"Açık iş sayılamadı ({Key}): {page.Error.Message}");
    }

    protected static string? Text(JsonObject values, string name) => values[name]?.GetValue<string>();

    protected static Guid? Id(JsonObject values, string name) => Guid.TryParse(Text(values, name), out var id) ? id : null;

    protected static DateTimeOffset? Time(JsonObject values, string name) =>
        DateTimeOffset.TryParse(Text(values, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : null;
}

internal sealed class ActivityTimelineSource(IRecordPipeline pipeline, IModuleData data) : CrmTimelineSource(pipeline)
{
    public override string Key => "crm.activities";

    protected override bool CountsOpen => true;

    protected override string EntityKey => CrmModule.Entities.Activity;

    protected override Dictionary<string, string> Filters(CustomerTimelineQuery query, string? before)
    {
        // timeline: firmanın kişilerine ve fırsatlarına bağlı aktiviteler de gelir (firma detayındaki aktivite listesiyle aynı kapsam).
        var filters = new Dictionary<string, string>
        {
            ["timeline"] = $"{CrmModule.Entities.Company}:{query.CompanyId}",
            ["sort"] = ActivityRecordStore.OccurredDescending,
        };
        if (before is not null)
        {
            filters["occurredBefore"] = before;
        }

        return filters;
    }

    protected override async Task<IReadOnlyList<CustomerTimelineEntry>> ToEntriesAsync(IReadOnlyList<RecordData> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.Id).ToList();
        var created = await data.Set<Activity>().AsNoTracking().Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.CreatedAt, cancellationToken);
        return
        [
            .. items.Select(i => new CustomerTimelineEntry(
                EntityKey,
                i.Id,
                Time(i.Values, "completedAt") ?? Time(i.Values, "startAt") ?? Time(i.Values, "dueAt") ?? created[i.Id],
                Text(i.Values, "subject") ?? string.Empty,
                Text(i.Values, "status"),
                Text(i.Values, "type"),
                Id(i.Values, OwnedRecordType.OwnerField))),
        ];
    }
}

internal sealed class OpportunityTimelineSource(IRecordPipeline pipeline, IModuleData data) : CrmTimelineSource(pipeline)
{
    public override string Key => "crm.opportunities";

    protected override bool CountsOpen => true;

    protected override string EntityKey => CrmModule.Entities.Opportunity;

    /// <summary>Kazanıldı/kaybedildi geçişleri fırsat tarihçesinden okunur (yeni alan ya da migration yok); tutar ve neden satıra girmez.</summary>
    protected override async Task<(IReadOnlyList<CustomerTimelineEntry> Entries, bool HasMore)> StatusRowsAsync(
        CustomerTimelineQuery query, int take, CancellationToken cancellationToken)
    {
        var visible = await VisibleRecordsAsync(query, cancellationToken);
        if (visible.Count == 0)
        {
            return ([], false);
        }

        var ids = visible.Keys.ToList();
        var before = query.Before;
        var closed = await data.Set<OpportunityHistory>().AsNoTracking()
            .Where(h => ids.Contains(h.OpportunityId) && (h.ToStatus == Domain.OpportunityStatus.Won || h.ToStatus == Domain.OpportunityStatus.Lost) && (before == null || h.At < before))
            .OrderByDescending(h => h.At).ThenBy(h => h.OpportunityId).Take(take + 1).ToListAsync(cancellationToken);
        return (
            [
                .. closed.Take(take).Select(h => new CustomerTimelineEntry(
                    CrmTimelineKeys.OpportunityClosed,
                    h.OpportunityId,
                    h.At,
                    Text(visible[h.OpportunityId].Values, "title") ?? string.Empty,
                    h.ToStatus,
                    h.ToStatus,
                    Id(visible[h.OpportunityId].Values, OwnedRecordType.OwnerField),
                    Text(visible[h.OpportunityId].Values, "opportunityNumber"))),
            ],
            closed.Count > take);
    }

    protected override Dictionary<string, string> Filters(CustomerTimelineQuery query, string? before)
    {
        var filters = new Dictionary<string, string> { ["companyId"] = query.CompanyId.ToString(), ["sort"] = "-created" };
        if (before is not null)
        {
            filters["createdBefore"] = before;
        }

        return filters;
    }

    protected override async Task<IReadOnlyList<CustomerTimelineEntry>> ToEntriesAsync(IReadOnlyList<RecordData> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.Id).ToList();
        var created = await data.Set<Opportunity>().AsNoTracking().Where(o => ids.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.CreatedAt, cancellationToken);
        return
        [
            .. items.Select(i => new CustomerTimelineEntry(
                EntityKey,
                i.Id,
                created[i.Id],
                Text(i.Values, "title") ?? string.Empty,
                Text(i.Values, "status"),
                OwnerId: Id(i.Values, OwnedRecordType.OwnerField),
                Reference: Text(i.Values, "opportunityNumber"))),
        ];
    }
}

internal sealed class QuoteTimelineSource(IRecordPipeline pipeline, IModuleData data) : CrmTimelineSource(pipeline)
{
    public override string Key => "crm.quotes";

    protected override string EntityKey => CrmModule.Entities.Quote;

    private static readonly string[] Outcomes = [QuoteStatus.Accepted, QuoteStatus.Rejected, QuoteStatus.Expired, QuoteStatus.Cancelled];

    /// <summary>
    /// Teklifin sonuçlandığı an (kabul, red, süre dolumu, iptal) teklif tarihçesinden okunur; her revizyonun sonucu ayrı satırdır.
    /// Taslak, sunuldu ve yerine geçildi geçişleri satır vermez (ADR-0053).
    /// </summary>
    protected override async Task<(IReadOnlyList<CustomerTimelineEntry> Entries, bool HasMore)> StatusRowsAsync(
        CustomerTimelineQuery query, int take, CancellationToken cancellationToken)
    {
        var visible = await VisibleRecordsAsync(query, cancellationToken);
        if (visible.Count == 0)
        {
            return ([], false);
        }

        var ids = visible.Keys.ToList();
        var before = query.Before;
        var accepted = await data.Set<QuoteEvent>().AsNoTracking()
            .Where(e => ids.Contains(e.QuoteId) && e.ToStatus != null && Outcomes.Contains(e.ToStatus) && (before == null || e.CreatedAt < before))
            .OrderByDescending(e => e.CreatedAt).ThenBy(e => e.QuoteId).Take(take + 1).ToListAsync(cancellationToken);
        return (
            [
                .. accepted.Take(take).Select(e => new CustomerTimelineEntry(
                    e.ToStatus == QuoteStatus.Accepted ? CrmTimelineKeys.QuoteAccepted : CrmTimelineKeys.QuoteOutcome,
                    e.QuoteId,
                    e.CreatedAt,
                    Text(visible[e.QuoteId].Values, "title") ?? string.Empty,
                    e.ToStatus,
                    e.ToStatus,
                    OwnerId: Id(visible[e.QuoteId].Values, OwnedRecordType.OwnerField),
                    Reference: Text(visible[e.QuoteId].Values, "quoteNumber"))),
            ],
            accepted.Count > take);
    }

    protected override Dictionary<string, string> Filters(CustomerTimelineQuery query, string? before)
    {
        // Her revizyon ayrı satırdır: revizyonun çıkarıldığı an müşteri geçmişinin parçasıdır.
        var filters = new Dictionary<string, string> { ["companyId"] = query.CompanyId.ToString(), ["sort"] = "-createdAt" };
        if (before is not null)
        {
            filters["createdBefore"] = before;
        }

        return filters;
    }

    protected override async Task<IReadOnlyList<CustomerTimelineEntry>> ToEntriesAsync(IReadOnlyList<RecordData> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.Id).ToList();
        var created = await data.Set<Quote>().AsNoTracking().Where(q => ids.Contains(q.Id))
            .ToDictionaryAsync(q => q.Id, q => q.CreatedAt, cancellationToken);
        return
        [
            .. items.Select(i => new CustomerTimelineEntry(
                EntityKey,
                i.Id,
                created[i.Id],
                Text(i.Values, "title") ?? string.Empty,
                Text(i.Values, "status"),
                OwnerId: Id(i.Values, OwnedRecordType.OwnerField),
                Reference: Text(i.Values, "quoteNumber"))),
        ];
    }
}

/// <summary>
/// Müşteri zaman çizelgesinin müşteri adayı kaynağı (ADR-0053, dilim 4): bu firmaya dönüşen müşteri adayları, dönüşüm anıyla. Aday kayıt boru hattından
/// okunur (izin ve veri kapsamı uygulanır); dönüşmemiş aday firmaya bağlı sayılmaz. Bir firma çok az adayın dönüşüm hedefi olduğundan hepsi okunup bellekte sıralanır.
/// </summary>
internal sealed class LeadTimelineSource(IRecordPipeline pipeline) : ICustomerTimelineSource
{
    public string Key => "crm.leads";

    public async Task<CustomerTimelineSlice?> ReadAsync(CustomerTimelineQuery query, CancellationToken cancellationToken)
    {
        var filters = new Dictionary<string, string> { ["convertedCompanyId"] = query.CompanyId.ToString() };
        if (query.Before is { } before)
        {
            filters["convertedBefore"] = before.ToString("O", CultureInfo.InvariantCulture);
        }

        var page = await pipeline.QueryAsync(new RecordQuery(CrmModule.Entities.Lead, 1, RecordQuery.MaxPageSize, Filters: filters), cancellationToken);
        if (!page.IsSuccess)
        {
            return page.Error!.Code is RecordErrorCode.PermissionDenied or RecordErrorCode.FeatureNotLicensed
                ? null
                : throw new InvalidOperationException($"Zaman çizelgesi okunamadı ({Key}): {page.Error.Message}");
        }

        var take = Math.Clamp(query.Take, 1, RecordQuery.MaxPageSize);
        var rows = page.Value!.Items
            .Select(i => (Item: i, At: DateTimeOffset.TryParse(Text(i.Values, "convertedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : (DateTimeOffset?)null))
            .Where(r => r.At is not null)
            .OrderByDescending(r => r.At)
            .ThenBy(r => r.Item.Id)
            .ToList();
        return new CustomerTimelineSlice(
            [
                .. rows.Take(take).Select(r => new CustomerTimelineEntry(
                    CrmTimelineKeys.LeadConverted,
                    r.Item.Id,
                    r.At!.Value,
                    Name(r.Item.Values),
                    OwnerId: Guid.TryParse(Text(r.Item.Values, OwnedRecordType.OwnerField), out var owner) ? owner : null)),
            ],
            rows.Count > take || page.Value.TotalCount > page.Value.Items.Count);
    }

    private static string Name(JsonObject values) =>
        string.Join(' ', new[] { Text(values, "firstName"), Text(values, "lastName") }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } person
            ? person
            : Text(values, "companyName") ?? string.Empty;

    private static string? Text(JsonObject values, string name) => values[name]?.GetValue<string>();
}
