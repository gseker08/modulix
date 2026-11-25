using Modulix.Core.Contracts.Modules;

namespace Modulix.Core.Modules;

/// <summary>İçe aktarılabilir kayıt türleri (ADR-0033): tanımlar açılışta doğrulanır, hatalı tanımla uygulama açılmaz.</summary>
public sealed partial class ModuleCatalog
{
    private readonly Dictionary<string, (string ModuleKey, ImportDefinition Definition)> imports = new(StringComparer.Ordinal);

    public IEnumerable<(string ModuleKey, ImportDefinition Definition)> ImportDefinitions =>
        imports.Values.OrderBy(i => i.Definition.DisplayName, StringComparer.CurrentCulture);

    public (string ModuleKey, ImportDefinition Definition)? FindImport(string entityKey) =>
        imports.TryGetValue(entityKey, out var found) ? found : null;

    private void RegisterImports(IModule module)
    {
        foreach (var definition in module.Imports)
        {
            ValidateImport(module, definition);
            if (!imports.TryAdd(definition.EntityKey, (module.Key, definition)))
            {
                throw new InvalidOperationException($"İçe aktarma tanımı iki kez kayıtlı: '{definition.EntityKey}'.");
            }

            // Kayıt türü seçicide görünen ad: iki tanım aynı adı taşırsa kullanıcı hangisini seçtiğini bilemez ("Kişi" ve "Kişi" gibi).
            // Ayırt etmek için ad parantezle genişletilir ("Kişi (ortak kayıt)", "Kişi (CRM)").
            var sameName = imports.Values.FirstOrDefault(i =>
                i.Definition.EntityKey != definition.EntityKey
                && string.Equals(i.Definition.DisplayName.Trim(), definition.DisplayName.Trim(), StringComparison.CurrentCultureIgnoreCase));
            if (sameName.Definition is not null)
            {
                throw new InvalidOperationException(
                    $"İçe aktarma tanımlarının görünen adı benzersiz olmalıdır: '{definition.DisplayName}' hem '{sameName.Definition.EntityKey}' hem '{definition.EntityKey}' için kullanılıyor.");
            }
        }
    }

    private static void ValidateImport(IModule module, ImportDefinition definition)
    {
        var fields = definition.Fields.Select(f => f.Key).ToList();
        var valid = definition.EntityKey.StartsWith(module.Key + ".", StringComparison.Ordinal)
            && module.Permissions.Any(p => p.Key == definition.CreatePermission)
            && definition.TemplateVersion > 0
            && fields.Count > 0
            && fields.Distinct(StringComparer.Ordinal).Count() == fields.Count
            && definition.Fields.Select(f => f.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() == fields.Count
            && definition.MatchRules.All(r => fields.Contains(r.Field) && (r.Exact || r.Filter is not null))
            && definition.Fields.All(f => (f.Type == ImportFieldType.Reference) == (f.Reference is { Filters.Count: > 0 }));
        if (!valid)
        {
            throw new InvalidOperationException(
                $"Geçersiz içe aktarma tanımı: '{definition.EntityKey}' ({module.Key}). Kayıt türü ve izin modülün olmalı, sütun anahtarları ve adları tekrar etmemeli, eşleşme alanları sütunlardan seçilmeli, bağlantı sütunları hedefini vermeli.");
        }
    }
}
