using Modulix.Core.Contracts.Modules;
using Modulix.Core.Modules;

namespace Modulix.Core.Tests.Modules;

/// <summary>
/// İçe aktarma kayıt türü seçicisinde iki tanım aynı adı taşırsa kullanıcı hangisini seçtiğini bilemez (gerçek örnek: çekirdeğin ortak kişisi ile
/// CRM kişisi ikisi de "Kişi" idi). Görünen ad açılışta benzersiz olmaya zorlanır; hatalı tanımla uygulama açılmaz (ADR-0033).
/// Katalog kuralıdır: tenant izolasyonu ve yetki konusu yoktur, kayıt yazılmaz.
/// </summary>
public class ImportDefinitionNameTests
{
    [Fact]
    public void Distinct_display_names_are_accepted_and_listed_by_name()
    {
        var catalog = new ModuleCatalog(
        [
            new CoreModule(),
            new ImportingModule("alpha", Define("alpha.person", "Kişi (alfa)")),
            new ImportingModule("beta", Define("beta.contact", "Kişi (CRM)")),
        ]);

        var names = catalog.ImportDefinitions.Select(i => i.Definition.DisplayName).ToList();
        Assert.Contains("Kişi (alfa)", names);
        Assert.Contains("Kişi (CRM)", names);
        Assert.True(names.IndexOf("Kişi (alfa)") < names.IndexOf("Kişi (CRM)"), "Liste ada göre sıralıdır.");
    }

    [Fact]
    public void The_same_display_name_in_two_modules_is_rejected_and_names_both_entities()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
        [
            new CoreModule(),
            new ImportingModule("alpha", Define("alpha.person", "Kişi")),
            new ImportingModule("beta", Define("beta.contact", "Kişi")),
        ]));

        Assert.Contains("alpha.person", exception.Message);
        Assert.Contains("beta.contact", exception.Message);
    }

    [Theory]
    [InlineData("kişi")]
    [InlineData("  Kişi ")]
    public void A_name_that_differs_only_by_case_or_surrounding_spaces_still_collides(string second)
    {
        Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
        [
            new CoreModule(),
            new ImportingModule("alpha", Define("alpha.person", "Kişi")),
            new ImportingModule("beta", Define("beta.contact", second)),
        ]));
    }

    [Fact]
    public void Two_definitions_in_one_module_with_the_same_name_are_rejected_too()
    {
        Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
            [new CoreModule(), new ImportingModule("alpha", Define("alpha.person", "Kişi"), Define("alpha.contact", "Kişi"))]));
    }

    [Fact]
    public void A_module_cannot_reuse_a_name_the_core_module_already_shows()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
            [new CoreModule(), new ImportingModule("alpha", Define("alpha.holiday", "Resmî tatil"))]));

        Assert.Contains("alpha.holiday", exception.Message);
    }

    [Fact]
    public void The_core_module_keeps_registering_with_its_own_unique_names()
    {
        var catalog = new ModuleCatalog([new CoreModule()]);

        var names = catalog.ImportDefinitions.Select(i => i.Definition.DisplayName).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.CurrentCultureIgnoreCase).Count());
        Assert.Contains("Kişi (ortak kayıt)", names);
    }

    private static ImportDefinition Define(string entityKey, string displayName) =>
        new(entityKey, displayName, $"{entityKey.Split('.')[0]}.records.import", [new("name", "Ad")]);

    private sealed class ImportingModule(string key, params ImportDefinition[] imports) : IModule
    {
        public string Key => key;

        public string DisplayName => key;

        public IReadOnlyList<PermissionDefinition> Permissions { get; } = [new($"{key}.records.import", "İçe aktarma")];

        public IReadOnlyList<ImportDefinition> Imports { get; } = imports;
    }
}
