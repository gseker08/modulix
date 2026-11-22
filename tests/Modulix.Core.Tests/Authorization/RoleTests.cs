using Modulix.Core.Authorization;
using Modulix.Core.Modules;

namespace Modulix.Core.Tests.Authorization;

public class RoleTests
{
    [Fact]
    public void Permissions_are_trimmed_deduplicated_and_sorted()
    {
        var role = new Role("  Satış ", ["crm.contacts.read", " crm.companies.read", "crm.contacts.read"]);

        Assert.Equal("Satış", role.Name);
        Assert.Equal(["crm.companies.read", "crm.contacts.read"], role.Permissions);
    }

    [Theory]
    [InlineData("crm")]
    [InlineData("crm.companies")]
    [InlineData("CRM.companies.read")]
    [InlineData("")]
    public void Malformed_permission_is_rejected(string permission)
    {
        Assert.Throws<ArgumentException>(() => new Role("Satış", [permission]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_name_is_rejected(string name)
    {
        // İleti alanın altında gösterilir; çerçevenin İngilizce varsayılan iletisi olmamalıdır.
        var ex = Assert.Throws<ArgumentException>(() => new Role(name, []));

        Assert.Equal("name", ex.ParamName);
        Assert.StartsWith("Rol adı zorunludur.", ex.Message);
    }

    [Fact]
    public void Renaming_to_an_empty_name_is_rejected_and_keeps_the_name()
    {
        var role = new Role("Satış", []);

        var ex = Assert.Throws<ArgumentException>(() => role.Rename(" "));

        Assert.StartsWith("Rol adı zorunludur.", ex.Message);
        Assert.Equal("Satış", role.Name);
    }

    [Fact]
    public void Name_longer_than_limit_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Role(new string('a', Role.NameMaxLength + 1), []));
    }

    [Fact]
    public void Membership_role_requires_ids()
    {
        Assert.Throws<ArgumentException>(() => new MembershipRole(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new MembershipRole(Guid.NewGuid(), Guid.Empty));
    }
}

public class ModuleEntitlementTests
{
    [Fact]
    public void New_entitlement_is_enabled_and_can_be_disabled()
    {
        var entitlement = new ModuleEntitlement(Guid.NewGuid(), "crm");
        Assert.True(entitlement.IsEnabled);

        entitlement.Disable();

        Assert.False(entitlement.IsEnabled);
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ModuleEntitlement(Guid.Empty, "crm"));
        Assert.Throws<ArgumentException>(() => new ModuleEntitlement(Guid.NewGuid(), "CRM"));
    }
}
