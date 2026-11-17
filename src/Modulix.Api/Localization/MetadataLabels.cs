using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Modules;
using Modulix.Shared.Access;

namespace Modulix.Api.Localization;

/// <summary>
/// Rol ve modül ekranlarına giden metadata adları isteğin dilinde (ADR-0034, dilim 18c-2): modül adı, iznin açıklaması ve satır adı,
/// hazır rolün adı ve açıklaması. Anahtarlar <see cref="MetadataTexts"/> ile tanımlayıcılardan türer; sözlükte yoksa Türkçe kaynak metin.
/// </summary>
public static class MetadataLabels
{
    public static async ValueTask<IReadOnlyDictionary<string, string>> BundleAsync(
        IRequestLanguage language, ITextCatalog texts, CancellationToken cancellationToken) =>
        texts.Bundle(await language.GetAsync(cancellationToken));

    public static string Module(IModule module, IReadOnlyDictionary<string, string> bundle) =>
        MetadataTexts.Text(bundle, MetadataTexts.ModuleKey(module.Key), module.DisplayName);

    public static PermissionDto Permission(PermissionDefinition permission, IModule module, IReadOnlyDictionary<string, string> bundle) =>
        new(
            permission.Key,
            MetadataTexts.Text(bundle, MetadataTexts.PermissionKey(permission.Key), permission.DisplayName),
            module.Key,
            Module(module, bundle),
            permission.ResourceName is { } resource
                ? MetadataTexts.Text(bundle, MetadataTexts.PermissionResourceKey(permission.Key), resource)
                : AccessKeys.ResourceOf(permission.Key));

    /// <summary>Hazır rol; <paramref name="withPermissions"/> yanlışsa izin listesi boş gider (platform ekranındaki "Salt okur").</summary>
    public static RoleTemplateDto Template(RoleTemplate template, IReadOnlyDictionary<string, string> bundle, bool withPermissions = true)
    {
        var localized = MetadataTexts.Localize(template, bundle);
        return new RoleTemplateDto(localized.Key, localized.Name, localized.Description, withPermissions ? localized.Permissions : []);
    }

    public static string Entity(string entityKey, string source, IReadOnlyDictionary<string, string> bundle) =>
        MetadataTexts.Text(bundle, MetadataTexts.EntityKey(entityKey), source);
}
