using Modulix.Core.Authorization;

namespace Modulix.Core.Tests.Authorization;

/// <summary>E8 dilim 2a: menü öğesi anahtarı ve sıra kuralları.</summary>
public class RoleMenuItemTests
{
    [Theory]
    [InlineData("crm.companies", true)]
    [InlineData("crm.quote-approvals", true)]
    [InlineData("admin", true)]
    [InlineData("CRM.companies", false)]
    [InlineData("crm..companies", false)]
    [InlineData(".crm", false)]
    [InlineData("crm.", false)]
    [InlineData("crm-", false)]
    [InlineData("crm companies", false)]
    [InlineData("crm.firmalar; drop", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Item_key_is_validated_by_format(string? key, bool valid)
    {
        Assert.Equal(valid, RoleMenuItem.IsValidKey(key));
    }

    [Fact]
    public void Item_key_length_is_limited()
    {
        Assert.True(RoleMenuItem.IsValidKey(new string('a', RoleMenuItem.KeyMaxLength)));
        Assert.False(RoleMenuItem.IsValidKey(new string('a', RoleMenuItem.KeyMaxLength + 1)));
    }

    [Fact]
    public void Constructor_rejects_bad_role_key_and_order()
    {
        var role = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new RoleMenuItem(Guid.Empty, "crm.leads", 1, false));
        Assert.Throws<ArgumentException>(() => new RoleMenuItem(role, "Bad Key", 1, false));
        Assert.Throws<ArgumentException>(() => new RoleMenuItem(role, "crm.leads", -1, false));
        Assert.Throws<ArgumentException>(() => new RoleMenuItem(role, "crm.leads", RoleMenuItem.MaxOrder + 1, false));

        var item = new RoleMenuItem(role, "crm.leads", null, true);
        Assert.Null(item.SortOrder);
        Assert.True(item.Hidden);
    }
}
