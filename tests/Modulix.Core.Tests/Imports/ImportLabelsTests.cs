using Modulix.Core.Contracts.Modules;
using Modulix.Core.Imports;

namespace Modulix.Core.Tests.Imports;

/// <summary>
/// ADR-0034 dilim 18c-5: içe aktarma tanımının adları okurun dilinde; yüklenen dosya Türkçe kaynak adı ve öteki dillerdeki çevirileri de
/// kabul eder (başlık ve sabit seçenek), okurun dilindeki ad başka alanın adına kaptırılmaz.
/// </summary>
public class ImportLabelsTests
{
    private const string Entity = "hr.employee";

    private static readonly ImportField Status = new("status", "Durum", ImportFieldType.Choice)
    {
        Options = [new("active", "Çalışıyor"), new("terminated", "Ayrıldı")],
    };

    private static readonly ImportField Title = new("jobTitle", "Unvan");

    private static readonly Dictionary<string, string> Tr = [];

    private static readonly Dictionary<string, string> En = new()
    {
        ["meta.import.hr.employee"] = "Employee card",
        ["meta.import.hr.employee.field.status"] = "Status",
        ["meta.import.hr.employee.field.status.option.active"] = "Employed",
        ["meta.import.hr.employee.field.status.option.terminated"] = "Left",
        ["meta.import.hr.employee.field.jobTitle"] = "Job title",
    };

    private static readonly Dictionary<string, string> Az = new()
    {
        ["meta.import.hr.employee.field.status"] = "Status",
        ["meta.import.hr.employee.field.status.option.active"] = "İşləyir",
        ["meta.import.hr.employee.field.jobTitle"] = "Vəzifə",
    };

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> All = [Tr, En, Az];

    [Fact]
    public void Labels_and_options_are_shown_in_the_reader_language_with_the_other_names_as_variants()
    {
        var english = ImportLabels.Localize(Entity, Status, En, All);

        Assert.Equal("Status", english.Label);
        Assert.Equal(["Durum"], english.LabelVariants);
        Assert.Equal(["Employed", "Left"], english.Options.Select(o => o.Label));
        Assert.Contains(new SettingOption("active", "Çalışıyor"), english.OptionVariants);
        Assert.Contains(new SettingOption("active", "İşləyir"), english.OptionVariants);
        Assert.Equal("Employee card", ImportLabels.Name(new ImportDefinition(Entity, "Çalışan kartı", "hr.x", [Status]), En));

        var turkish = ImportLabels.Localize(Entity, Status, Tr, All);
        Assert.Equal("Durum", turkish.Label);
        Assert.Equal(["Status"], turkish.LabelVariants);
        Assert.Equal(["Çalışıyor", "Ayrıldı"], turkish.Options.Select(o => o.Label));
        Assert.Equal("Çalışan kartı", ImportLabels.Name(new ImportDefinition(Entity, "Çalışan kartı", "hr.x", [Status]), Tr));
    }

    [Fact]
    public void A_file_with_turkish_or_translated_headers_binds_to_the_same_columns()
    {
        IReadOnlyList<ImportField> english = [ImportLabels.Localize(Entity, Status, En, All), ImportLabels.Localize(Entity, Title, En, All)];

        foreach (var headers in new[] { new[] { "Durum", "Unvan" }, ["Status", "Job title"], ["status", "Vəzifə"] })
        {
            var map = ImportColumns.Bind(headers, english);
            Assert.Empty(map.Unknown);
            Assert.Empty(map.Errors);
            Assert.Equal(["status", "jobTitle"], map.Columns.Select(c => c.Field.Key));
        }
    }

    [Fact]
    public void Option_cells_accept_the_turkish_name_and_its_translations()
    {
        var english = ImportLabels.Localize(Entity, Status, En, All);
        IReadOnlyList<ImportColumn> columns = [new(0, english)];

        foreach (var text in new[] { "Çalışıyor", "Employed", "işləyir", "active" })
        {
            var (values, problems) = ImportRows.Build(columns, new ImportFileRow(2, [text]));
            Assert.Empty(problems);
            Assert.Equal("active", values["status"]!.GetValue<string>());
        }

        var (_, unknown) = ImportRows.Build(columns, new ImportFileRow(2, ["Retired"]));
        Assert.Single(unknown);
    }

    [Fact]
    public void A_translated_name_cannot_be_saved_as_an_alias_of_another_column()
    {
        IReadOnlyList<ImportField> fields = [ImportLabels.Localize(Entity, Status, Tr, All), ImportLabels.Localize(Entity, Title, Tr, All)];

        var errors = ImportDesignRules.Validate(
            fields, ImportDesignInput.Default with { Aliases = new Dictionary<string, IReadOnlyList<string>> { ["jobTitle"] = ["Status"] } });

        Assert.Contains(errors, e => e.Field == "jobTitle");
    }
}
