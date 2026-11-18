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
    public void Role_template_name_and_description_are_translated_while_key_and_permissions_stay()
    {
        var template = new RoleTemplate("crm.sales-rep", "Satış temsilcisi", "Kendi firmalarını yönetir.", ["crm.companies.read"]);
        var bundle = new Dictionary<string, string> { ["meta.role-template.crm.sales-rep"] = "Sales representative" };

        var localized = MetadataTexts.Localize(template, bundle);

        Assert.Equal("Sales representative", localized.Name);
        Assert.Equal("Kendi firmalarını yönetir.", localized.Description);
        Assert.Equal(template.Key, localized.Key);
        Assert.Equal(template.Permissions, localized.Permissions);
    }

    [Fact]
    public void Core_module_permissions_and_name_have_all_three_languages_and_the_turkish_text_equals_the_source()
    {
        var tr = TextCatalog.Load("tr");
        var en = TextCatalog.Load("en");
        var az = TextCatalog.Load("az");
        IModule core = new CoreModule();
        var names = new[] { (MetadataTexts.ModuleKey(core.Key), core.DisplayName) }
            .Concat(core.Permissions.Select(p => (MetadataTexts.PermissionKey(p.Key), p.DisplayName)));

        Assert.DoesNotContain(names, n => !tr.TryGetValue(n.Item1, out var text) || text != n.Item2 || !en.ContainsKey(n.Item1) || !az.ContainsKey(n.Item1));
    }

    [Fact]
    public void Built_in_choice_values_are_translated_unless_the_firm_renamed_them_and_save_returns_the_turkish_source()
    {
        var definition = new ChoiceListDefinition(
            "crm.stage", "Fırsat aşaması", "crm.opportunity", "stage", true, [new ChoiceCategory("won", "Kazanıldı")],
            [new ChoiceValueDefinition("New", "Yeni"), new ChoiceValueDefinition("Won", "Kazanıldı", "won")]);
        var bundle = new Dictionary<string, string>
        {
            ["meta.choice-list.crm.stage"] = "Opportunity stage",
            ["meta.choice-list.crm.stage.category.won"] = "Won",
            ["meta.choice-list.crm.stage.value.New"] = "New",
            ["meta.choice-list.crm.stage.value.Won"] = "Won",
        };
        // Firma "Won" değerini yeniden adlandırmış ve kendi "demo_yapildi" değerini eklemiş.
        var list = new Core.Contracts.Customization.ChoiceList(
            "crm.stage",
            [new("New", "Yeni", null, true, true), new("Won", "Satış oldu", "won", true, true), new("demo_yapildi", "Demo yapıldı", null, true, false)]);

        var localized = MetadataTexts.Localize(list, definition, bundle);
        var localizedDefinition = MetadataTexts.Localize(definition, bundle);

        Assert.Equal(["New", "Satış oldu", "Demo yapıldı"], localized.Values.Select(v => v.Label));
        Assert.Equal(list.Values.Select(v => (v.Key, v.Category, v.Active, v.BuiltIn)), localized.Values.Select(v => (v.Key, v.Category, v.Active, v.BuiltIn)));
        Assert.Equal(("Opportunity stage", "Won"), (localizedDefinition.DisplayName, localizedDefinition.Categories[0].DisplayName));
        Assert.Equal("Yeni", MetadataTexts.ChoiceValueSource(definition, "New", "New", bundle));
        Assert.Equal("Satış oldu", MetadataTexts.ChoiceValueSource(definition, "Won", "Satış oldu", bundle));
        Assert.Equal("Demo", MetadataTexts.ChoiceValueSource(definition, "demo_yapildi", "Demo", bundle));
    }

    [Theory]
    [InlineData("Europe/Istanbul", "Europe-Istanbul")]
    [InlineData("08:00-17:00", "08-00-17-00")]
    [InlineData("demo_yapildi", "demo-yapildi")]
    public void Key_segment_replaces_characters_the_dictionary_key_does_not_accept(string value, string expected) =>
        Assert.Equal(expected, MetadataTexts.KeySegment(value));

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
