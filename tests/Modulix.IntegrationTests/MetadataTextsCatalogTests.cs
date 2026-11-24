using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Localization;
using Modulix.Core.Modules;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0034 dilim 18c: bütün modüllerin metadata adları üç sözlükte karşılığını taşır, Türkçe sözlük kaynak metinle aynıdır ve sözlükte
/// hiçbir modülün tanımlamadığı <c>meta.*</c> anahtarı kalmaz. Kapsam: denetim kayıt türleri (tür, alan ve kodda tanımlı seçenek adları;
/// 18c-1), modül adları, izinlerin açıklaması ve satır adı, hazır roller ("Salt okur" dahil; 18c-2), form düzeni ve firma ayarı adları (18c-3), seçim listeleri, özel alan türleri, lookup ve kayıt devri adları (18c-4), içe aktarma tanımları (tanım, sütun ve sabit seçenek adları; 18c-5).
/// Veritabanı gerekmez; katalog uygulamanın kaydettiği modüllerden kurulur.
/// </summary>
public class MetadataTextsCatalogTests
{
    private static readonly ModuleCatalog Catalog = ModuleCatalog.Create(
        (IModule[])typeof(Program).Assembly.GetType("ModulixModules", throwOnError: true)!.GetMethod("Create")!.Invoke(null, null)!);

    private static IEnumerable<(string Key, string Source)> Names() =>
        AuditedNames().Concat(AccessNames()).Concat(FormNames()).Concat(SettingNames()).Concat(CustomizationNames()).Concat(ImportNames());

    private static IEnumerable<(string Key, string Source)> ImportNames() =>
        Catalog.ImportDefinitions.SelectMany(i =>
            new[] { (MetadataTexts.ImportKey(i.Definition.EntityKey), i.Definition.DisplayName) }
                .Concat(i.Definition.Fields.SelectMany(f =>
                    new[] { (MetadataTexts.ImportFieldKey(i.Definition.EntityKey, f.Key), f.Label) }
                        .Concat(f.Options.Select(o => (MetadataTexts.ImportOptionKey(i.Definition.EntityKey, f.Key, o.Value), o.Label))))));

    private static IEnumerable<(string Key, string Source)> CustomizationNames() =>
        Catalog.ChoiceLists.SelectMany(l =>
                new[] { (MetadataTexts.ChoiceListKey(l.List.Key), l.List.DisplayName) }
                    .Concat(l.List.Categories.Select(c => (MetadataTexts.ChoiceCategoryKey(l.List.Key, c.Key), c.DisplayName)))
                    .Concat(l.List.Values.Select(v => (MetadataTexts.ChoiceValueKey(l.List.Key, v.Key), v.Label))))
            .Concat(Catalog.CustomizableEntities.Select(e => (MetadataTexts.CustomizableKey(e.Entity.EntityKey), e.Entity.DisplayName)))
            .Concat(Catalog.Lookups.SelectMany(k =>
                new[] { (MetadataTexts.LookupKey(k.Lookup.Key), k.Lookup.Title) }
                    .Concat(k.Lookup.SearchDescription.Length > 0 ? [(MetadataTexts.LookupDescriptionKey(k.Lookup.Key), k.Lookup.SearchDescription)] : [])
                    .Concat(k.Lookup.DependsOnHint is { } hint ? [(MetadataTexts.LookupHintKey(k.Lookup.Key), hint)] : [])))
            .Concat(Catalog.Modules.SelectMany(m => m.OwnedRecords).Select(o => (MetadataTexts.OwnedRecordKey(o.EntityKey), o.Label)));

    private static IEnumerable<(string Key, string Source)> FormNames() =>
        Catalog.Forms.SelectMany(f =>
            new[] { (MetadataTexts.FormKey(f.Form.EntityKey), f.Form.DisplayName) }
                .Concat(f.Form.Tabs.Select(t => (MetadataTexts.FormTabKey(f.Form.EntityKey, t.Key), t.Label)))
                .Concat(f.Form.Sections.Select(s => (MetadataTexts.FormSectionKey(f.Form.EntityKey, s.Key), s.Label)))
                .Concat(f.Form.Fields.Select(x => (MetadataTexts.FormFieldKey(f.Form.EntityKey, x.Key), x.Label))));

    private static IEnumerable<(string Key, string Source)> SettingNames() =>
        Catalog.Settings.SelectMany(s =>
            new[] { (MetadataTexts.SettingKey(s.Definition.Key), s.Definition.DisplayName), (MetadataTexts.SettingGroupKey(s.Definition.Key), s.Definition.Group) }
                .Concat(s.Definition.Description is { } description ? [(MetadataTexts.SettingDescriptionKey(s.Definition.Key), description)] : [])
                .Concat(s.Definition.PatternMessage is { } pattern ? [(MetadataTexts.SettingPatternMessageKey(s.Definition.Key), pattern)] : [])
                .Concat(s.Definition.Options.Select(o => (MetadataTexts.SettingOptionKey(s.Definition.Key, o.Value), o.Label))));

    private static IEnumerable<(string Key, string Source)> AuditedNames() =>
        Catalog.AuditedEntities.SelectMany(entity =>
            new[] { (MetadataTexts.EntityKey(entity.EntityKey), entity.DisplayName) }
                .Concat(entity.Fields.Select(f => (MetadataTexts.FieldKey(entity.EntityKey, f.Key), f.Value)))
                .Concat(entity.Options.SelectMany(o => o.Value.Select(v => (MetadataTexts.OptionKey(entity.EntityKey, o.Key, v.Key), v.Value)))));

    private static IEnumerable<(string Key, string Source)> AccessNames()
    {
        var templates = Catalog.TemplatesFor(Catalog.Modules.Select(m => m.Key).ToHashSet(StringComparer.Ordinal));
        return Catalog.Modules.SelectMany(module =>
                new[] { (MetadataTexts.ModuleKey(module.Key), module.DisplayName) }
                    .Concat(module.Permissions.Select(p => (MetadataTexts.PermissionKey(p.Key), p.DisplayName)))
                    .Concat(module.Permissions
                        .Where(p => p.ResourceName is not null)
                        .Select(p => (MetadataTexts.PermissionResourceKey(p.Key), p.ResourceName!))))
            .Concat(templates.SelectMany(t => new[]
            {
                (MetadataTexts.RoleTemplateKey(t.Key), t.Name),
                (MetadataTexts.RoleTemplateDescriptionKey(t.Key), t.Description),
            }));
    }

    [Fact]
    public void Every_audited_name_is_in_all_three_dictionaries_with_the_turkish_text_unchanged()
    {
        var tr = TextCatalog.Load("tr");
        var en = TextCatalog.Load("en");
        var az = TextCatalog.Load("az");

        var wrong = Names()
            .Where(n => !tr.TryGetValue(n.Key, out var text) || text != n.Source || !en.ContainsKey(n.Key) || !az.ContainsKey(n.Key))
            .Select(n => $"{n.Key}\t{n.Source}")
            .ToList();

        Assert.True(wrong.Count == 0, "Sözlükte eksik ya da farklı metadata adı:\n" + string.Join('\n', wrong));
    }

    [Fact]
    public void No_dictionary_keeps_a_metadata_key_that_no_module_defines()
    {
        var known = Names().Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var language in new[] { "tr", "en", "az" })
        {
            var orphans = TextCatalog.Load(language).Keys.Where(k => k.StartsWith("meta.", StringComparison.Ordinal) && !known.Contains(k)).ToList();
            Assert.True(orphans.Count == 0, $"{language}: kullanılmayan metadata anahtarı:\n" + string.Join('\n', orphans));
        }
    }

    /// <summary>
    /// Dosya başlığı her dildeki adla eşleştiği için (18c-5) bir tanımda iki sütunun herhangi bir dildeki adı ya da anahtarı çakışmamalı;
    /// çakışırsa başlık yanlış sütuna bağlanabilir. Seçeneklerin adları da sütun içinde tekildir.
    /// </summary>
    [Fact]
    public void Import_column_and_option_names_do_not_collide_across_languages()
    {
        var bundles = new[] { "tr", "en", "az" }.Select(TextCatalog.Load).ToList();
        var problems = new List<string>();
        foreach (var (_, definition) in Catalog.ImportDefinitions)
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in definition.Fields)
            {
                var names = bundles.Select(b => MetadataTexts.Text(b, MetadataTexts.ImportFieldKey(definition.EntityKey, field.Key), field.Label))
                    .Append(field.Label).Append(field.Key).Select(n => n.Trim().ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))).Distinct();
                foreach (var name in names)
                {
                    if (!owners.TryAdd(name, field.Key) && owners[name] != field.Key)
                    {
                        problems.Add($"{definition.EntityKey}: '{name}' hem {owners[name]} hem {field.Key}");
                    }
                }

                var options = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var option in field.Options)
                {
                    foreach (var name in bundles.Select(b => MetadataTexts.Text(b, MetadataTexts.ImportOptionKey(definition.EntityKey, field.Key, option.Value), option.Label))
                        .Append(option.Label).Append(option.Value).Select(n => n.Trim().ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))).Distinct())
                    {
                        if (!options.TryAdd(name, option.Value) && options[name] != option.Value)
                        {
                            problems.Add($"{definition.EntityKey}.{field.Key}: '{name}' hem {options[name]} hem {option.Value}");
                        }
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }
}
