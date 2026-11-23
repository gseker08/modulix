using Modulix.Core.Events;

namespace Modulix.Core.Tests.Events;

/// <summary>Verilmiş izinle çalıştırma kuralı (ADR-0056 dilim 1b-2): iş kimliğinin dışındaki izinler yalnızca okuma izni olabilir.</summary>
public class GrantedPermissionRulesTests
{
    private static readonly string[] Declared = ["marketing.segments.read", "marketing.journeys.write"];

    [Fact]
    public void Foreign_read_permissions_are_added_to_the_declared_set()
    {
        var set = GrantedPermissionRules.Validate(["crm.companies.read", "crm.contacts.read"], Declared);

        Assert.Equal(4, set.Count);
        Assert.Contains("crm.companies.read", set);
        Assert.Contains("marketing.journeys.write", set);
    }

    [Fact]
    public void A_declared_permission_may_be_a_write_permission_but_a_foreign_write_permission_is_refused()
    {
        Assert.Contains("marketing.journeys.write", GrantedPermissionRules.Validate(["marketing.journeys.write"], Declared));
        Assert.Throws<ArgumentException>(() => GrantedPermissionRules.Validate(["crm.companies.write"], Declared));
        Assert.Throws<ArgumentException>(() => GrantedPermissionRules.Validate(["crm.companies.read", "core.users.manage"], Declared));
    }

    [Fact]
    public void An_empty_grant_leaves_only_the_declared_permissions()
    {
        Assert.Equal(Declared.Length, GrantedPermissionRules.Validate([], Declared).Count);
    }
}
