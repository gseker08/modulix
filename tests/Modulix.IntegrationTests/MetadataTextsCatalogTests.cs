using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Localization;
using Modulix.Core.Modules;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0034 dilim 18c: bütün modüllerin denetim kayıt türleri (tür, alan ve kodda tanımlı seçenek adları) üç sözlükte karşılığını taşır,
/// Türkçe sözlük kaynak metinle aynıdır ve sözlükte karşılığı olmayan <c>meta.*</c> anahtarı kalmaz. Veritabanı gerekmez; katalog
/// uygulamanın kaydettiği modüllerden kurulur.
/// </summary>
public class MetadataTextsCatalogTests
{
    private static readonly ModuleCatalog Catalog = ModuleCatalog.Create(
        (IModule[])typeof(Program).Assembly.GetType("ModulixModules", throwOnError: true)!.GetMethod("Create")!.Invoke(null, null)!);

    private static IEnumerable<(string Key, string Source)> Names() =>
        Catalog.AuditedEntities.SelectMany(entity =>
            new[] { (MetadataTexts.EntityKey(entity.EntityKey), entity.DisplayName) }
                .Concat(entity.Fields.Select(f => (MetadataTexts.FieldKey(entity.EntityKey, f.Key), f.Value)))
                .Concat(entity.Options.SelectMany(o => o.Value.Select(v => (MetadataTexts.OptionKey(entity.EntityKey, o.Key, v.Key), v.Value)))));

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
}
