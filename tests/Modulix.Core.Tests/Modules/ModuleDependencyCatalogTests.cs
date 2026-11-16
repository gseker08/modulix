using Modulix.Core.Contracts.Modules;
using Modulix.Core.Modules;

namespace Modulix.Core.Tests.Modules;

/// <summary>ADR-0051: modül bağımlılık beyanı açılışta doğrulanır.</summary>
public class ModuleDependencyCatalogTests
{
    [Fact]
    public void The_catalog_returns_declared_dependencies()
    {
        var catalog = new ModuleCatalog(
        [
            new CoreModule(),
            new DemoModule("sales"),
            new DemoModule("billing", Required("sales"), Optional("fleet")),
            new DemoModule("fleet"),
        ]);

        Assert.Equal(["sales", "fleet"], catalog.DependenciesOf("billing").Select(d => d.ModuleKey));
        Assert.Equal(ModuleDependencyKind.Required, catalog.DependenciesOf("billing")[0].Kind);
        Assert.Empty(catalog.DependenciesOf("sales"));
        Assert.Empty(catalog.DependenciesOf("unknown"));
    }

    [Fact]
    public void A_dependency_on_a_module_that_is_not_installed_is_allowed()
    {
        var catalog = new ModuleCatalog([new CoreModule(), new DemoModule("billing", Required("sales"), Optional("fleet"))]);

        Assert.Equal(2, catalog.DependenciesOf("billing").Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sales")]
    [InlineData("sa.les")]
    public void A_dependency_with_a_malformed_module_key_is_rejected(string moduleKey) =>
        Rejected(new DemoModule("billing", Required(moduleKey)));

    [Fact]
    public void A_dependency_on_the_module_itself_is_rejected() =>
        Rejected(new DemoModule("billing", Optional("billing")));

    [Fact]
    public void A_dependency_on_core_is_rejected() =>
        Rejected(new DemoModule("billing", Required(CoreModule.ModuleKey)));

    [Fact]
    public void A_dependency_declared_twice_is_rejected() =>
        Rejected(new DemoModule("sales"), new DemoModule("billing", Required("sales"), Optional("sales")));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_dependency_without_a_reason_is_rejected(string reason) =>
        Rejected(new DemoModule("sales"), new DemoModule("billing", new ModuleDependency("sales", ModuleDependencyKind.Optional, reason)));

    [Fact]
    public void A_dependency_with_an_undefined_kind_is_rejected() =>
        Rejected(new DemoModule("sales"), new DemoModule("billing", new ModuleDependency("sales", (ModuleDependencyKind)9, "Fatura")));

    [Fact]
    public void A_cycle_of_required_dependencies_is_rejected()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
        [
            new CoreModule(),
            new DemoModule("sales", Required("billing")),
            new DemoModule("billing", Required("fleet")),
            new DemoModule("fleet", Required("sales")),
        ]));

        Assert.Contains("döngü", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cycle_that_includes_an_optional_dependency_is_allowed()
    {
        var catalog = new ModuleCatalog(
        [
            new CoreModule(),
            new DemoModule("sales", Required("billing")),
            new DemoModule("billing", Optional("sales")),
        ]);

        Assert.Single(catalog.DependenciesOf("sales"));
    }

    private static ModuleDependency Required(string moduleKey) => new(moduleKey, ModuleDependencyKind.Required, "Zorunlu özellik");

    private static ModuleDependency Optional(string moduleKey) => new(moduleKey, ModuleDependencyKind.Optional, "Ek özellik");

    private static void Rejected(params IModule[] modules) =>
        Assert.Throws<InvalidOperationException>(() => new ModuleCatalog([new CoreModule(), .. modules]));

    private sealed class DemoModule(string key, params ModuleDependency[] dependencies) : IModule
    {
        public string Key => key;

        public string DisplayName => key;

        public IReadOnlyList<PermissionDefinition> Permissions => [];

        public IReadOnlyList<ModuleDependency> Dependencies => dependencies;
    }
}
