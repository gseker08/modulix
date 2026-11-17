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

    /// <summary>Modülün adı (<see cref="IModule.DisplayName"/>).</summary>
    public static string ModuleKey(string moduleKey) => $"meta.module.{moduleKey}";

    /// <summary>İznin rol ekranındaki açıklaması (<see cref="PermissionDefinition.DisplayName"/>).</summary>
    public static string PermissionKey(string permissionKey) => $"meta.permission.{permissionKey}";

    /// <summary>İznin rol ekranındaki satır (kaynak) adı (<see cref="PermissionDefinition.ResourceName"/>).</summary>
    public static string PermissionResourceKey(string permissionKey) => $"meta.permission.{permissionKey}.resource";

    /// <summary>Hazır rolün adı (<see cref="RoleTemplate.Name"/>).</summary>
    public static string RoleTemplateKey(string templateKey) => $"meta.role-template.{templateKey}";

    /// <summary>Hazır rolün açıklaması (<see cref="RoleTemplate.Description"/>).</summary>
    public static string RoleTemplateDescriptionKey(string templateKey) => $"meta.role-template.{templateKey}.description";

    /// <summary>Formun adı (<see cref="FormDefinition.DisplayName"/>).</summary>
    public static string FormKey(string entityKey) => $"meta.form.{entityKey}";

    /// <summary>Formun varsayılan sekmesinin adı.</summary>
    public static string FormTabKey(string entityKey, string tab) => $"meta.form.{entityKey}.tab.{tab}";

    /// <summary>Formun varsayılan bölümünün adı.</summary>
    public static string FormSectionKey(string entityKey, string section) => $"meta.form.{entityKey}.section.{section}";

    /// <summary>Formdaki (kodda tanımlı) alanın etiketi.</summary>
    public static string FormFieldKey(string entityKey, string field) => $"meta.form.{entityKey}.field.{field}";

    /// <summary>Firma ayarının adı (<see cref="SettingDefinition.DisplayName"/>).</summary>
    public static string SettingKey(string settingKey) => $"meta.setting.{settingKey}";

    /// <summary>Firma ayarının ekrandaki bölüm adı (<see cref="SettingDefinition.Group"/>); ayar başınadır.</summary>
    public static string SettingGroupKey(string settingKey) => $"meta.setting.{settingKey}.group";

    /// <summary>Firma ayarının açıklaması.</summary>
    public static string SettingDescriptionKey(string settingKey) => $"meta.setting.{settingKey}.description";

    /// <summary>Seçenekli firma ayarında seçeneğin adı.</summary>
    public static string SettingOptionKey(string settingKey, string value) => $"meta.setting.{settingKey}.option.{KeySegment(value)}";

    /// <summary>Seçim listesinin adı (<see cref="ChoiceListDefinition.DisplayName"/>).</summary>
    public static string ChoiceListKey(string listKey) => $"meta.choice-list.{listKey}";

    /// <summary>Seçim listesinde kategorinin adı.</summary>
    public static string ChoiceCategoryKey(string listKey, string category) => $"meta.choice-list.{listKey}.category.{KeySegment(category)}";

    /// <summary>Seçim listesinde kodda tanımlı hazır değerin adı.</summary>
    public static string ChoiceValueKey(string listKey, string value) => $"meta.choice-list.{listKey}.value.{KeySegment(value)}";

    /// <summary>Özel alan eklenebilen kayıt türünün adı (<see cref="CustomizableEntity.DisplayName"/>).</summary>
    public static string CustomizableKey(string entityKey) => $"meta.customizable.{entityKey}";

    /// <summary>Lookup alanının başlığı (<see cref="LookupDefinition.Title"/>).</summary>
    public static string LookupKey(string lookupKey) => $"meta.lookup.{lookupKey}";

    /// <summary>Lookup alanında aranan alanların açıklaması (<see cref="LookupDefinition.SearchDescription"/>).</summary>
    public static string LookupDescriptionKey(string lookupKey) => $"meta.lookup.{lookupKey}.description";

    /// <summary>Bağımlı lookup alanının ipucu (<see cref="LookupDefinition.DependsOnHint"/>).</summary>
    public static string LookupHintKey(string lookupKey) => $"meta.lookup.{lookupKey}.hint";

    /// <summary>Kayıt devrinde sayıyla birlikte gösterilen tür adı (<see cref="OwnedRecordType.Label"/>, ör. "12 firma").</summary>
    public static string OwnedRecordKey(string entityKey) => $"meta.owned.{entityKey}";

    /// <summary>
    /// Firmanın listesi okurun dilinde: kodda tanımlı hazır değerin adı, firma değiştirmediyse (tanımdaki adla aynıysa) çevrilir; firmanın
    /// verdiği ad ve eklediği değerler firmanın verisidir, olduğu gibi kalır. Anahtar, kategori ve etkinlik değişmez.
    /// </summary>
    public static Customization.ChoiceList Localize(Customization.ChoiceList list, ChoiceListDefinition definition, IReadOnlyDictionary<string, string> bundle)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(definition);
        var defaults = definition.Values.ToDictionary(v => v.Key, v => v.Label, StringComparer.OrdinalIgnoreCase);
        return list with
        {
            Values = [.. list.Values.Select(v => v.BuiltIn && defaults.TryGetValue(v.Key, out var source) && source == v.Label
                ? v with { Label = Text(bundle, ChoiceValueKey(definition.Key, v.Key), source) }
                : v)],
        };
    }

    /// <summary>Liste tanımının adı ve kategori adları okurun dilinde (değerler <see cref="Localize(Customization.ChoiceList, ChoiceListDefinition, IReadOnlyDictionary{string, string})"/> ile).</summary>
    public static ChoiceListDefinition Localize(ChoiceListDefinition definition, IReadOnlyDictionary<string, string> bundle)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition with
        {
            DisplayName = Text(bundle, ChoiceListKey(definition.Key), definition.DisplayName),
            Categories = [.. definition.Categories.Select(c => c with { DisplayName = Text(bundle, ChoiceCategoryKey(definition.Key, c.Key), c.DisplayName) })],
        };
    }

    /// <summary>
    /// Kaydedilecek değer adı: okurun dilinde gösterilmiş hazır ad Türkçe kaynak metne döner (yönetici adı değiştirmeden kaydederse öteki
    /// dillerde çevrilmeye devam eder); değiştirilmiş ad olduğu gibi saklanır.
    /// </summary>
    public static string ChoiceValueSource(ChoiceListDefinition definition, string valueKey, string label, IReadOnlyDictionary<string, string> bundle)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Values.FirstOrDefault(v => string.Equals(v.Key, valueKey, StringComparison.OrdinalIgnoreCase)) is { } builtIn
            && label == Text(bundle, ChoiceValueKey(definition.Key, builtIn.Key), builtIn.Label)
            ? builtIn.Label
            : label;
    }

    /// <summary>
    /// Değerin anahtar parçası: harf, rakam ve tire dışındaki karakterler tire olur (ör. saat dilimi "Europe/Istanbul" → "Europe-Istanbul",
    /// iş saati "08:00-17:00" → "08-00-17-00"); sözlük anahtarı biçimi bu karakterleri kabul etmez.
    /// </summary>
    public static string KeySegment(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-';
            }
        });
    }

    /// <summary>Hazır rolün adı ve açıklaması <paramref name="bundle"/> dilinde.</summary>
    public static RoleTemplate Localize(RoleTemplate template, IReadOnlyDictionary<string, string> bundle)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template with
        {
            Name = Text(bundle, RoleTemplateKey(template.Key), template.Name),
            Description = Text(bundle, RoleTemplateDescriptionKey(template.Key), template.Description),
        };
    }

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
