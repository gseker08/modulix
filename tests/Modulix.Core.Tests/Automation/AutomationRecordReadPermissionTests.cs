using Modulix.Core.Automation;
using Modulix.Core.Contracts.Automation;
using static Modulix.Core.Tests.Automation.AutomationKit;

namespace Modulix.Core.Tests.Automation;

/// <summary>
/// Kayıt deposunun ayrı okuma izni (ADR-0042 bölüm 29): kayıt türünün izni "yönetim" (taslağı görmek için), deponun okuma izni başka olabilir. Kural
/// kaydı boru hattından okuduğu için kimliği iki izni birlikte taşır; kullanıcı ikisine sahip değilse kayıt türü ona kural için görünmez; izin
/// modülün olmalıdır.
/// </summary>
public class AutomationRecordReadPermissionTests
{
    private static AutomationCapabilities With(string? recordRead) => new()
    {
        Entities = [new(Entity, "Fırsat", WritePermission) { RecordReadPermission = recordRead }],
        Fields = [Title],
    };

    private static IReadOnlyList<AutomationEntityView> Entities(AutomationCapabilities capabilities, params string[] permissions) =>
        CatalogService(Catalog(capabilities), new FakeAccess(permissions, ["sales", "billing"])).GetAsync(CancellationToken.None).GetAwaiter().GetResult().Entities;

    [Fact]
    public void The_rule_identity_carries_both_the_entity_and_the_record_read_permission_even_without_fields()
    {
        var bare = new AutomationDefinition(new RecordCreatedTrigger(), null, []);

        Assert.Equal(["sales.deals.read", WritePermission], AutomationRuleIdentity.Permissions(Entity, bare, Catalog(With("sales.deals.read"))).Order());
        Assert.Equal([WritePermission], AutomationRuleIdentity.Permissions(Entity, bare, Catalog(With(null))));
    }

    [Fact]
    public void A_user_without_the_record_read_permission_does_not_see_the_entity()
    {
        var capabilities = With("sales.deals.read");

        Assert.Single(Entities(capabilities, WritePermission, "sales.deals.read"));
        Assert.Empty(Entities(capabilities, WritePermission));
    }

    [Fact]
    public void The_record_read_permission_must_belong_to_the_module()
    {
        Assert.Throws<InvalidOperationException>(() => Catalog(With("billing.invoices.read")));
    }
}
