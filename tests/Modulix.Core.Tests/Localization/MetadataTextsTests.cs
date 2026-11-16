using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Localization;
using Modulix.Core.Modules;

namespace Modulix.Core.Tests.Localization;

/// <summary>
/// ADR-0034 dilim 18c: metadata adlarının çevirisi. Anahtar metadata'nın tanımlayıcılarından türer; sözlükte yoksa Türkçe kaynak metin
/// kalır. Core modülünün denetim kayıt türleri üç sözlükte de karşılığını taşır ve Türkçe sözlük kaynak metinle aynıdır.
/// </summary>
public class MetadataTextsTests
{
    private static readonly AuditedEntity Entity = new(
        "crm.company", "Firma (CRM)", null, new Dictionary<string, string> { ["name"] = "Ad", ["status"] = "Durum", ["taxNo"] = "Vergi no" })
    {
        Options = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["status"] = new Dictionary<string, string> { ["active"] = "Etkin", ["closed"] = "Kapalı" },
        },
    };

    [Fact]
    public void Names_are_translated_by_key_and_missing_keys_keep_the_turkish_source()
    {
        var bundle = new Dictionary<string, string>
        {
            ["meta.entity.crm.company"] = "Company (CRM)",
            ["meta.field.crm.company.name"] = "Name",
            ["meta.field.crm.company.status"] = "Status",
            ["meta.option.crm.company.status.active"] = "Active",
        };

        var localized = MetadataTexts.Localize(Entity, bundle);

        Assert.Equal("Company (CRM)", localized.DisplayName);
        Assert.Equal(["name", "status", "taxNo"], localized.Fields.Keys);
        Assert.Equal(["Name", "Status", "Vergi no"], localized.Fields.Values);
        Assert.Equal("Active", localized.Options["status"]["active"]);
        Assert.Equal("Kapalı", localized.Options["status"]["closed"]);
        Assert.Equal(Entity.EntityKey, localized.EntityKey);
    }

    [Fact]
    public void An_empty_dictionary_leaves_the_entity_as_written() =>
        Assert.Equal("Firma (CRM)", MetadataTexts.Localize(Entity, new Dictionary<string, string>()).DisplayName);

    [Fact]
    public void Core_audited_types_have_all_three_languages_and_the_turkish_text_equals_the_source()
    {
        var tr = TextCatalog.Load("tr");
        var en = TextCatalog.Load("en");
        var az = TextCatalog.Load("az");
        var missing = new List<string>();
        foreach (var entity in ((IModule)new CoreModule()).AuditedEntities)
        {
            foreach (var (key, source) in Names(entity))
            {
                if (!tr.TryGetValue(key, out var text) || text != source || !en.ContainsKey(key) || !az.ContainsKey(key))
                {
                    missing.Add(key);
                }
            }
        }

        Assert.Empty(missing);
    }

    internal static IEnumerable<(string Key, string Source)> Names(AuditedEntity entity)
    {
        yield return (MetadataTexts.EntityKey(entity.EntityKey), entity.DisplayName);
        foreach (var (field, label) in entity.Fields)
        {
            yield return (MetadataTexts.FieldKey(entity.EntityKey, field), label);
        }

        foreach (var (field, options) in entity.Options)
        {
            foreach (var (value, label) in options)
            {
                yield return (MetadataTexts.OptionKey(entity.EntityKey, field, value), label);
            }
        }
    }
}
