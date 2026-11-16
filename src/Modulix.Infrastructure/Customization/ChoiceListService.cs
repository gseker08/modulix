using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Customization;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Customization;
using Modulix.Core.Modules;
using Modulix.Core.Records;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Customization;

/// <summary>
/// Seçim listeleri (Y9b). Kayıt depolarına firmanın listesini verir (<see cref="IChoiceLists"/>): liste başına istekte
/// bir kez okunur. Firma listeyi düzenleyene kadar tabloda satırı yoktur; koddaki varsayılan geçerlidir.
/// </summary>
internal sealed class ChoiceListService(
    ModulixDbContext db,
    ITenantContextAccessor tenantAccessor,
    ModuleCatalog catalog,
    IModuleEntitlements entitlements,
    IAuditWriter audit,
    CustomizationLimits limits,
    IRequestLanguage language,
    ITextCatalog texts) : IChoiceListService, IChoiceLists
{
    private readonly Dictionary<(Guid TenantId, string ListKey), ChoiceList> cache = [];

    public async Task<ChoiceList> GetAsync(string listKey, CancellationToken cancellationToken)
    {
        var definition = catalog.FindChoiceList(listKey)?.List
            ?? throw new InvalidOperationException($"Tanımsız seçim listesi: '{listKey}'.");
        if (tenantAccessor.Current is not { } tenant)
        {
            return MetadataTexts.Localize(ChoiceListRules.Resolve(definition, null), definition, await TextsAsync(cancellationToken));
        }

        if (cache.TryGetValue((tenant.TenantId, listKey), out var list))
        {
            return list;
        }

        var stored = await db.ChoiceLists.AsNoTracking().SingleOrDefaultAsync(l => l.ListKey == listKey, cancellationToken);
        list = MetadataTexts.Localize(ChoiceListRules.Resolve(definition, stored?.Values), definition, await TextsAsync(cancellationToken));
        cache[(tenant.TenantId, listKey)] = list;
        return list;
    }

    public async Task<IReadOnlyList<ChoiceListInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var definitions = await EnabledAsync(cancellationToken);
        var keys = definitions.Select(d => d.Key).ToList();
        var stored = await db.ChoiceLists.AsNoTracking().Where(l => keys.Contains(l.ListKey)).ToListAsync(cancellationToken);
        var bundle = await TextsAsync(cancellationToken);
        return definitions
            .Select(d => stored.FirstOrDefault(s => s.ListKey == d.Key) is { } row
                ? Localized(new ChoiceListInfo(d, ChoiceListRules.Resolve(d, row.Values), Customized: true), bundle)
                : Localized(new ChoiceListInfo(d, ChoiceListRules.Resolve(d, null), Customized: false), bundle))
            .ToList();
    }

    public async Task<ChoiceListInfo?> SaveAsync(string listKey, IReadOnlyList<ChoiceValueInput> values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        var tenant = tenantAccessor.Current ?? throw new InvalidOperationException("Aktif tenant olmadan listeler yönetilemez.");
        if ((await EnabledAsync(cancellationToken)).FirstOrDefault(d => d.Key == listKey) is not { } definition)
        {
            return null;
        }

        var row = await db.ChoiceLists.SingleOrDefaultAsync(l => l.ListKey == listKey, cancellationToken);
        // Okurun dilinde gösterilen hazır ad Türkçe kaynağa döner; firmanın verdiği ad olduğu gibi saklanır (ADR-0034, dilim 18c-4).
        var bundle = await TextsAsync(cancellationToken);
        values = [.. values.Select(v => v.Key is { } key ? v with { Label = MetadataTexts.ChoiceValueSource(definition, key, v.Label, bundle) } : v)];
        var current = ChoiceListRules.Resolve(definition, row?.Values);
        var updated = ChoiceListRules.Apply(definition, current, values, limits.MaxValuesPerList);
        var after = ChoiceListRules.Resolve(definition, updated);
        // İlk düzenleme firmanın listesini oluşturur: denetim kaydında tam liste görünür (ve listenin adı kayda geçer).
        var created = row is null;
        if (row is null)
        {
            row = new TenantChoiceList(listKey);
            db.ChoiceLists.Add(row);
        }

        row.Values = updated;
        var (before, now) = (created ? null : Snapshot(definition, current), Snapshot(definition, after));
        if (!JsonNode.DeepEquals(before, now))
        {
            audit.Add(new AuditEntry(
                tenant.TenantId,
                tenant.UserId,
                CoreModule.AuditedEntityKeys.ChoiceList,
                row.Id,
                created ? AuditAction.Created : AuditAction.Updated,
                RecordDiff.Compute(before, now),
                ExecutionSource.Api,
                Activity.Current?.TraceId.ToString()));
        }

        await db.SaveChangesAsync(cancellationToken);
        cache.Clear();
        return Localized(new ChoiceListInfo(definition, after, Customized: true), bundle);
    }

    private IReadOnlyDictionary<string, string>? bundle;

    // Liste adı, kategoriler ve firmanın değiştirmediği hazır değer adları okurun dilinde (ADR-0034, dilim 18c-4); anahtarlar değişmez.
    private async ValueTask<IReadOnlyDictionary<string, string>> TextsAsync(CancellationToken cancellationToken) =>
        bundle ??= texts.Bundle(await language.GetAsync(cancellationToken));

    private static ChoiceListInfo Localized(ChoiceListInfo info, IReadOnlyDictionary<string, string> bundle) =>
        info with { Definition = MetadataTexts.Localize(info.Definition, bundle), List = MetadataTexts.Localize(info.List, info.Definition, bundle) };

    // Kapalı modülün listesi görünmez ve düzenlenemez; firmanın düzenlemesi silinmez, modül açılınca geri gelir.
    private async Task<IReadOnlyList<ChoiceListDefinition>> EnabledAsync(CancellationToken cancellationToken)
    {
        var tenant = tenantAccessor.Current ?? throw new InvalidOperationException("Aktif tenant olmadan listeler okunamaz.");
        var enabled = await entitlements.GetEnabledAsync(tenant.TenantId, cancellationToken);
        return catalog.ChoiceLists.Where(l => catalog.IsEnabled(l.ModuleKey, enabled)).Select(l => l.List).ToList();
    }

    // Denetim kaydında liste tek satırdır: değerler sırasıyla, pasifler işaretli.
    private static JsonObject Snapshot(ChoiceListDefinition definition, ChoiceList list) => new()
    {
        ["list"] = definition.DisplayName,
        ["values"] = new JsonArray([.. list.Values.Select(v => JsonValue.Create(v.Active ? v.Label : $"{v.Label} (pasif)"))]),
    };
}
