using Modulix.Core.Contracts.Modules;

namespace Modulix.Core.Contracts.Localization;

/// <summary>
/// Modül metadata'sındaki görünen adların çevirisi (ADR-0034 §6, dilim 18c). Metadata Türkçe kaynak metni taşımaya devam eder; çeviri
/// anahtarı metadata'nın kendi tanımlayıcılarından türetilir (ör. <c>meta.entity.crm.company</c>). Sözlükte anahtar yoksa Türkçe kaynak
/// metin gösterilir; böylece yeni eklenen kayıt türü ya da alan çevirisi gelene kadar bozulmaz. Firmanın kendi verisi (özel alan adları,
/// düzenlenebilir seçim listesi değerleri) çevrilmez.
/// </summary>
public static class MetadataTexts
{
    /// <summary>Kayıt türünün adı (denetim kaydı, bildirimler).</summary>
    public static string EntityKey(string entityKey) => $"meta.entity.{entityKey}";

    /// <summary>Kayıt türünün denetim kaydındaki alan adı.</summary>
    public static string FieldKey(string entityKey, string field) => $"meta.field.{entityKey}.{field}";

    /// <summary>Kayıt türünün kodda tanımlı seçenekli alanındaki değerin adı.</summary>
    public static string OptionKey(string entityKey, string field, string value) => $"meta.option.{entityKey}.{field}.{value}";

    /// <summary>Anahtarın sözlükteki metni; yoksa <paramref name="source"/> (Türkçe kaynak metin).</summary>
    public static string Text(IReadOnlyDictionary<string, string> bundle, string key, string source)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return bundle.TryGetValue(key, out var text) ? text : source;
    }

    /// <summary>Kayıt türünün adı, alan adları ve kodda tanımlı seçenek adları <paramref name="bundle"/> dilinde.</summary>
    public static AuditedEntity Localize(AuditedEntity entity, IReadOnlyDictionary<string, string> bundle)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(bundle);
        return entity with
        {
            DisplayName = Text(bundle, EntityKey(entity.EntityKey), entity.DisplayName),
            Fields = entity.Fields.ToDictionary(
                f => f.Key, f => Text(bundle, FieldKey(entity.EntityKey, f.Key), f.Value), StringComparer.Ordinal),
            Options = entity.Options.ToDictionary(
                o => o.Key,
                o => (IReadOnlyDictionary<string, string>)o.Value.ToDictionary(
                    v => v.Key, v => Text(bundle, OptionKey(entity.EntityKey, o.Key, v.Key), v.Value), StringComparer.Ordinal),
                StringComparer.Ordinal),
        };
    }
}
