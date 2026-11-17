using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Warehouse.Contracts.Customs;

namespace Modulix.Modules.Warehouse.Customs;

/// <summary>
/// Rejim profili yönetimi (ADR-0032 K16). Tablolar modülün başvuru tablolarıdır (ADR-0046 bölüm 6): <c>platform</c> şemasındadır, RLS dışıdır ve tenant
/// verisi içermez; okuma <see cref="IModuleReferenceData"/>, yazma <see cref="IPlatformReferenceStore"/> iledir. Profil başına en çok bir
/// taslak ve bir yayın vardır (filtreli benzersiz dizin); yayın, önceki yayını "yerini bıraktı" yapar ve geçmiş olarak kalır. Her yazma
/// değiştiren yönetici ile birlikte <c>PlatformAuditLog</c>'a aynı işlemde yazılır.
/// </summary>
internal sealed class RegimeProfileService(IModuleReferenceData reads, IPlatformReferenceStore store) : IRegimeProfileAdmin, IRegimeProfileCatalog
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // ---- Okuma (modüller): yalnızca etkin profilin yayındaki sürümü ----

    public async Task<IReadOnlyList<PublishedRegimeProfile>> ListPublishedAsync(CancellationToken cancellationToken)
    {
        // Birkaç profil vardır; sıralama bellekte yapılır (EF kayıt projeksiyonundan sonraki sıralamayı çeviremez).
        var rows = await PublishedQuery(null).ToListAsync(cancellationToken);
        return [.. rows.OrderBy(r => r.Profile.Name, StringComparer.CurrentCultureIgnoreCase).Select(ToPublished)];
    }

    public async Task<PublishedRegimeProfile?> GetPublishedAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var row = await PublishedQuery(profileId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToPublished(row);
    }

    // Süzgeç projeksiyondan önce uygulanır (EF kayıt tipine projeksiyondan sonraki koşulu çeviremez).
    private IQueryable<PublishedRow> PublishedQuery(Guid? profileId) =>
        from profile in reads.Query<RegimeProfile>()
        join version in reads.Query<RegimeProfileVersion>() on profile.Id equals version.ProfileId
        where profile.IsActive && version.State == RegimeProfileVersionState.Published && (profileId == null || profile.Id == profileId)
        select new PublishedRow(profile, version);

    private static PublishedRegimeProfile ToPublished(PublishedRow row)
    {
        var view = ToView(row.Version);
        return new PublishedRegimeProfile(row.Profile.Id, row.Profile.Code, row.Profile.Name, row.Version.Id, row.Version.Number, row.Version.IsVerified, row.Version.VerificationNote, view.Content);
    }

    private sealed record PublishedRow(RegimeProfile Profile, RegimeProfileVersion Version);

    // ---- Yönetim (platform) ----

    public async Task<IReadOnlyList<RegimeProfileSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var profiles = await reads.Query<RegimeProfile>().OrderBy(p => p.Name).ToListAsync(cancellationToken);
        var versions = await reads.Query<RegimeProfileVersion>()
            .Where(v => v.State != RegimeProfileVersionState.Superseded)
            .Select(v => new { v.ProfileId, v.Number, v.State, v.IsVerified }).ToListAsync(cancellationToken);
        return [.. profiles.Select(p => Summarize(p, versions.Where(v => v.ProfileId == p.Id)
            .Select(v => (v.Number, v.State, v.IsVerified))))];
    }

    public async Task<RegimeProfileDetail> GetAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await FindAsync(profileId, cancellationToken);
        var versions = await reads.Query<RegimeProfileVersion>().Where(v => v.ProfileId == profileId)
            .OrderByDescending(v => v.Number).ToListAsync(cancellationToken);
        return new RegimeProfileDetail(
            Summarize(profile, versions.Where(v => v.State != RegimeProfileVersionState.Superseded).Select(v => (v.Number, v.State, v.IsVerified))),
            [.. versions.Select(ToView)]);
    }

    public async Task<RegimeProfileSummary> CreateAsync(Guid actingUserId, string code, string name, CancellationToken cancellationToken)
    {
        var profile = new RegimeProfile(code, name);
        if (await store.Set<RegimeProfile>().AnyAsync(p => p.Code == profile.Code, cancellationToken))
        {
            throw new RegimeProfileConflictException($"Bu kodla bir rejim profili zaten var: '{profile.Code}'.");
        }

        store.Set<RegimeProfile>().Add(profile);
        Audit(actingUserId, "regime-profile.created", new JsonObject { ["code"] = profile.Code, ["name"] = profile.Name });
        await store.SaveChangesAsync(cancellationToken);
        return Summarize(profile, []);
    }

    public async Task<RegimeProfileVersionView> SaveDraftAsync(Guid actingUserId, Guid profileId, RegimeProfileContent content, CancellationToken cancellationToken)
    {
        var profile = await FindAsync(profileId, cancellationToken);
        if (RegimeProfileRules.Validate(content) is { Count: > 0 } errors)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(content));
        }

        var json = JsonSerializer.Serialize(content, Json);
        var draft = await store.Set<RegimeProfileVersion>().SingleOrDefaultAsync(v => v.ProfileId == profileId && v.State == RegimeProfileVersionState.Draft, cancellationToken);
        if (draft is null)
        {
            var next = (await store.Set<RegimeProfileVersion>().Where(v => v.ProfileId == profileId).MaxAsync(v => (int?)v.Number, cancellationToken) ?? 0) + 1;
            draft = new RegimeProfileVersion(profileId, next, json, actingUserId);
            store.Set<RegimeProfileVersion>().Add(draft);
        }
        else
        {
            draft.ReplaceContent(json);
        }

        Audit(actingUserId, "regime-profile.draft-saved", new JsonObject { ["code"] = profile.Code, ["version"] = draft.Number });
        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Aynı anda ikinci bir taslak açıldı (profil başına tek taslak).
            throw new RegimeProfileConflictException("Taslak başka bir yönetici tarafından açıldı; sayfayı yenileyip tekrar deneyin.");
        }

        return ToView(draft);
    }

    public async Task DiscardDraftAsync(Guid actingUserId, Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await FindAsync(profileId, cancellationToken);
        var draft = await store.Set<RegimeProfileVersion>().SingleOrDefaultAsync(v => v.ProfileId == profileId && v.State == RegimeProfileVersionState.Draft, cancellationToken)
            ?? throw new RegimeProfileNotFoundException("Silinecek taslak yok.");
        store.Set<RegimeProfileVersion>().Remove(draft);
        Audit(actingUserId, "regime-profile.draft-discarded", new JsonObject { ["code"] = profile.Code, ["version"] = draft.Number });
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<RegimeProfileVersionView> PublishAsync(Guid actingUserId, Guid profileId, bool verified, string? note, CancellationToken cancellationToken)
    {
        var profile = await FindAsync(profileId, cancellationToken);
        if (note is { Length: > RegimeProfileVersion.NoteMaxLength })
        {
            throw new ArgumentException($"Doğrulama notu en çok {RegimeProfileVersion.NoteMaxLength} karakter olabilir.", nameof(note));
        }

        if (verified && string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("Doğrulanmış olarak yayınlarken kimin/neye göre doğruladığını not olarak yazın.", nameof(note));
        }

        var draft = await store.Set<RegimeProfileVersion>().SingleOrDefaultAsync(v => v.ProfileId == profileId && v.State == RegimeProfileVersionState.Draft, cancellationToken)
            ?? throw new RegimeProfileConflictException("Yayınlanacak taslak yok; önce içeriği taslak olarak kaydedin.");

        // Taslağın içeriği yayın anında yeniden doğrulanır (kural paketi bir kez yazılıp sonra bozulmuş olamaz).
        var content = JsonSerializer.Deserialize<RegimeProfileContent>(draft.ContentJson, Json);
        if (RegimeProfileRules.Validate(content) is { Count: > 0 } errors)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(profileId));
        }

        // Önce eski yayın yerini bırakır, sonra taslak yayına çıkar: "profil başına tek yayın" dizini iki adımda da korunur.
        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        var previous = await store.Set<RegimeProfileVersion>().SingleOrDefaultAsync(v => v.ProfileId == profileId && v.State == RegimeProfileVersionState.Published, cancellationToken);
        if (previous is not null)
        {
            previous.Supersede();
            await store.SaveChangesAsync(cancellationToken);
        }

        draft.Publish(verified, note, actingUserId);
        Audit(actingUserId, "regime-profile.published", new JsonObject
        {
            ["code"] = profile.Code, ["version"] = draft.Number, ["verified"] = verified, ["note"] = draft.VerificationNote,
            ["previousVersion"] = previous?.Number,
        });
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToView(draft);
    }

    public async Task<RegimeProfileSummary> SetActiveAsync(Guid actingUserId, Guid profileId, bool active, CancellationToken cancellationToken)
    {
        var profile = await FindAsync(profileId, cancellationToken, track: true);
        if (profile.IsActive != active)
        {
            profile.SetActive(active);
            Audit(actingUserId, "regime-profile.active", new JsonObject { ["code"] = profile.Code, ["active"] = active });
            await store.SaveChangesAsync(cancellationToken);
        }

        return (await GetAsync(profileId, cancellationToken)).Summary;
    }

    private async Task<RegimeProfile> FindAsync(Guid profileId, CancellationToken cancellationToken, bool track = false)
    {
        var query = track ? store.Set<RegimeProfile>() : reads.Query<RegimeProfile>();
        return await query.SingleOrDefaultAsync(p => p.Id == profileId, cancellationToken)
            ?? throw new RegimeProfileNotFoundException("Rejim profili bulunamadı.");
    }

    private static RegimeProfileSummary Summarize(RegimeProfile profile, IEnumerable<(int Number, RegimeProfileVersionState State, bool IsVerified)> openVersions)
    {
        var list = openVersions.ToList();
        var published = list.FirstOrDefault(v => v.State == RegimeProfileVersionState.Published);
        return new RegimeProfileSummary(
            profile.Id, profile.Code, profile.Name, profile.IsActive,
            list.Any(v => v.State == RegimeProfileVersionState.Published) ? published.Number : null,
            published.IsVerified, list.Any(v => v.State == RegimeProfileVersionState.Draft));
    }

    internal static RegimeProfileVersionView ToView(RegimeProfileVersion v) =>
        new(v.Id, v.Number, v.State, v.IsVerified, v.VerificationNote, v.CreatedAt, v.PublishedAt,
            JsonSerializer.Deserialize<RegimeProfileContent>(v.ContentJson, Json) ?? new RegimeProfileContent(RegimeProfileContent.CurrentSchemaVersion, null, null));

    private void Audit(Guid actingUserId, string action, JsonObject details) => store.Audit(actingUserId, action, details.ToJsonString());
}
