using Modulix.Core.Contracts.Modules;
using Modulix.Core.Modules;

namespace Modulix.Core.Tests.Modules;

/// <summary>ADR-0051 dilim 2: açık modül kümesinin bağımlılık beyanlarına uyup uymadığı tek kuralla sorulur.</summary>
public class ModuleDependencyRulesTests
{
    private static readonly ModuleCatalog Catalog = new(
    [
        new CoreModule(),
        new DemoModule("sales", "Satış"),
        new DemoModule("billing", "Fatura", new ModuleDependency("sales", ModuleDependencyKind.Required, "Satıştan fatura")),
        new DemoModule("orders", "Sipariş", new ModuleDependency("sales", ModuleDependencyKind.Optional, "Tekliften sipariş")),
        new DemoModule("portal", "Portal", new ModuleDependency("absent", ModuleDependencyKind.Required, "Kurulu olmayan modül")),
    ]);

    [Fact]
    public void A_set_that_includes_every_required_provider_has_nothing_missing()
    {
        Assert.Empty(ModuleDependencyRules.Missing(Catalog, ["sales", "billing", "orders"]));
        ModuleDependencyRules.EnsureRequiredSatisfied(Catalog, ["sales", "billing"], "modules");
    }

    [Fact]
    public void Missing_lists_required_and_optional_providers_that_are_closed()
    {
        var missing = ModuleDependencyRules.Missing(Catalog, ["billing", "orders"]);

        Assert.Equal(
            [("billing", "sales", ModuleDependencyKind.Required), ("orders", "sales", ModuleDependencyKind.Optional)],
            missing.Select(m => (m.ModuleKey, m.DependencyKey, m.Kind)));
        Assert.Single(ModuleDependencyRules.Missing(Catalog, ["billing", "orders"], ModuleDependencyKind.Required));
    }

    [Fact]
    public void A_missing_required_provider_is_rejected_with_the_display_names()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => ModuleDependencyRules.EnsureRequiredSatisfied(Catalog, ["billing"], "modules"));

        Assert.Equal("modules", error.ParamName);
        Assert.Contains("'Fatura' modülü 'Satış' açık olmadan kullanılamaz.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_optional_provider_is_not_rejected() =>
        ModuleDependencyRules.EnsureRequiredSatisfied(Catalog, ["orders"], "modules");

    [Fact]
    public void A_provider_that_is_not_installed_is_not_counted()
    {
        Assert.Empty(ModuleDependencyRules.Missing(Catalog, ["portal"]));
        ModuleDependencyRules.EnsureRequiredSatisfied(Catalog, ["portal"], "modules");
    }

    [Fact]
    public void A_violation_that_existed_before_the_change_does_not_block_it()
    {
        // Fatura zaten Satış'sız açık; Sipariş'i açmak bu yüzden reddedilmez.
        ModuleDependencyRules.EnsureRequiredSatisfied(Catalog, ["billing", "orders"], "moduleKey", ignore: ["billing"]);

        // Satış'ı kapatmak ise yeni bir ihlal yaratır.
        Assert.Throws<ArgumentException>(
            () => ModuleDependencyRules.EnsureRequiredSatisfied(Catalog, ["billing"], "moduleKey", ignore: ["billing", "sales"]));
    }

    private sealed class DemoModule(string key, string name, params ModuleDependency[] dependencies) : IModule
    {
        public string Key => key;

        public string DisplayName => name;

        public IReadOnlyList<PermissionDefinition> Permissions => [];

        public IReadOnlyList<ModuleDependency> Dependencies => dependencies;
    }
}
