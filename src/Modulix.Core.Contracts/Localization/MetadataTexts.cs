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
