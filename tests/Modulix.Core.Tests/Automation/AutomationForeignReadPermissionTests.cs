using System.Text.Json.Nodes;
using Modulix.Core.Automation;
using Modulix.Core.Contracts.Automation;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Modules;

namespace Modulix.Core.Tests.Automation;

/// <summary>
/// Aksiyonun başka modülün okuma izni istemesi (ADR-0056 dilim 2b): yalnızca ".read" ve yalnızca bağımlılığı beyan edilmiş modülün izni istenebilir;
/// böylece kuralın kimliği (yayınlayanın altkümesi) o modülün verisini okuyabilir ama yazamaz.
/// </summary>
public class AutomationForeignReadPermissionTests
{
    private static AutomationActionDefinition ActionNeeding(params string[] permissions) => new(
        "sales.outreach.send",
        "Ulaşım gönder",
        "Başka modülün verisini okuyarak ileti gönderir.",
        [new AutomationParameter("note", "Not", AutomationFieldType.Text, Required: false)],
        permissions,
        [],
        AutomationSideEffect.Write,
        SupportsDryRun: false);

    private static ModuleCatalog CatalogWith(AutomationActionDefinition action) =>
        AutomationKit.Catalog(
            new AutomationCapabilities
            {
                Entities = [new(AutomationKit.Entity, "Fırsat", "sales.deals.read")],
                Actions = [AutomationActionRegistration.For<AutomationKit.NoopAction>(action)],
            });

    [Fact]
    public void A_foreign_read_permission_is_refused_without_a_declared_dependency()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CatalogWith(ActionNeeding("billing.invoices.read")));
        Assert.Contains("billing.invoices.read", exception.Message);
    }

    [Fact]
    public void A_foreign_read_permission_of_a_depended_module_is_accepted_and_joins_the_rule_identity()
    {
        var capabilities = new AutomationCapabilities
        {
            Entities = [new(AutomationKit.Entity, "Fırsat", "sales.deals.read")],
            Actions = [AutomationActionRegistration.For<AutomationKit.NoopAction>(ActionNeeding("billing.invoices.read"))],
        };
        var catalog = new ModuleCatalog([
            new CoreModule(),
            new AutomationKit.TestModule("sales", capabilities, "sales.deals.read", AutomationKit.WritePermission, AutomationKit.SecretPermission)
            {
                Dependencies = [new ModuleDependency("billing", ModuleDependencyKind.Required, "Fatura okuma")],
            },
            new AutomationKit.TestModule("billing", AutomationCapabilities.None, "billing.invoices.read", "billing.invoices.write"),
        ]);

        var definition = new AutomationDefinition(new ManualTrigger(), null, [new AutomationActionStep("gonder", "sales.outreach.send", new JsonObject())]);
        var identity = AutomationRuleIdentity.Permissions(AutomationKit.Entity, definition, catalog);

        Assert.Contains("billing.invoices.read", identity);
        Assert.Contains("sales.deals.read", identity);
        Assert.DoesNotContain("billing.invoices.write", identity);
    }

    [Fact]
    public void A_foreign_write_permission_is_refused_even_with_a_dependency()
    {
        var capabilities = new AutomationCapabilities
        {
            Entities = [new(AutomationKit.Entity, "Fırsat", "sales.deals.read")],
            Actions = [AutomationActionRegistration.For<AutomationKit.NoopAction>(ActionNeeding("billing.invoices.write"))],
        };
        var exception = Assert.Throws<InvalidOperationException>(() => new ModuleCatalog([
            new CoreModule(),
            new AutomationKit.TestModule("sales", capabilities, "sales.deals.read", AutomationKit.WritePermission, AutomationKit.SecretPermission)
            {
                Dependencies = [new ModuleDependency("billing", ModuleDependencyKind.Required, "Fatura")],
            },
            new AutomationKit.TestModule("billing", AutomationCapabilities.None, "billing.invoices.read", "billing.invoices.write"),
        ]));
        Assert.Contains("billing.invoices.write", exception.Message);
    }
}
