using System.Reflection;
using Modulix.Shared.Access;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>
/// Rol bazlı menü düzeninin (E8 dilim 2b-1) sol menüye uygulanması. Gizlemek yetki değildir: düzen yalnızca
/// modül/izin süzmesinden geçmiş bağlantıların üstüne biner.
/// </summary>
public class MenuLayoutTests
{
    private static readonly Guid RoleA = Guid.NewGuid();
    private static readonly Guid RoleB = Guid.NewGuid();

    private static AppNavigation Nav(string[] permissions, Guid[] roleIds, RoleMenuRowDto[] layout, bool admin = false)
    {
        var session = new SessionState(new ApiClient(new HttpClient()));
        var access = new MyAccessResponse([CrmAccess.ModuleKey], permissions, admin, RoleIds: roleIds, MenuLayout: layout);
        typeof(SessionState).GetProperty(nameof(SessionState.Access), BindingFlags.Public | BindingFlags.Instance)!.SetValue(session, access);
        typeof(SessionState).GetProperty(nameof(SessionState.User), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(session, new MeResponse(Guid.NewGuid(), "a@example.com", "Test", Guid.NewGuid(), [], false));
        return new AppNavigation(session);
    }

    private static string[] Crm => [CrmAccess.CompaniesRead, CrmAccess.ContactsRead, CrmAccess.LeadsRead];

    private static string[] Ids(MenuContext? menu) =>
        menu!.Sections.SelectMany(s => s.Entries).Select(e => e.App.Id).ToArray();

    private static string[] CustomerIds(MenuContext? menu) =>
        menu!.Sections.Where(s => s.Group?.Id == AppCatalog.CrmCustomersGroup).SelectMany(s => s.Entries).Select(e => e.App.Id).ToArray();

    private static RoleMenuRowDto Row(Guid role, string key, int? order = null, bool hidden = false) => new(role, key, order, hidden);

    [Fact]
    public void No_layout_keeps_catalog_order()
    {
        var menu = Nav(Crm, [RoleA], []).Context("crm/firmalar");

        Assert.Equal(["crm.companies", "crm.contacts"], CustomerIds(menu));
    }

    [Fact]
    public void Empty_layout_for_administrator_shows_full_menu()
    {
        var menu = Nav(Crm, [RoleA], [], admin: true).Context("crm/firmalar");

        Assert.Contains("crm.contacts", Ids(menu));
    }

    [Fact]
    public void Role_layout_reorders_items()
    {
        var layout = new[] { Row(RoleA, "crm.contacts", 1), Row(RoleA, "crm.companies", 2) };

        var menu = Nav(Crm, [RoleA], layout).Context("crm/firmalar");

        Assert.Equal(["crm.contacts", "crm.companies"], CustomerIds(menu));
    }

    [Fact]
    public void Hidden_item_leaves_menu_but_active_context_stays_valid()
    {
        var layout = new[] { Row(RoleA, "crm.companies", hidden: true) };

        var menu = Nav(Crm, [RoleA], layout).Context("crm/firmalar");

        Assert.DoesNotContain("crm.companies", Ids(menu));
        Assert.Equal("crm.companies", menu!.Active.Id);
        Assert.Equal(AppCatalog.Crm, menu.Root.Id);
    }

    [Fact]
    public void Multiple_roles_union_visibility_and_take_smallest_order()
    {
        var layout = new[]
        {
            Row(RoleA, "crm.companies", hidden: true),
            Row(RoleA, "crm.contacts", 5),
            Row(RoleB, "crm.companies", 9),
            Row(RoleB, "crm.contacts", 2),
        };

        var menu = Nav(Crm, [RoleA, RoleB], layout).Context("crm/firmalar");

        // Firmalar RoleB'de görünür (birleşim); kişiler iki rolün en küçük sırası olan 2'dir.
        Assert.Equal(["crm.contacts", "crm.companies"], CustomerIds(menu));
    }

    [Fact]
    public void Item_hidden_by_every_layout_role_is_hidden()
    {
        var layout = new[] { Row(RoleA, "crm.companies", hidden: true), Row(RoleB, "crm.companies", hidden: true), Row(RoleB, "crm.contacts", 1) };

        var menu = Nav(Crm, [RoleA, RoleB], layout).Context("crm/kisiler");

        Assert.DoesNotContain("crm.companies", Ids(menu));
    }

    [Fact]
    public void Rows_of_roles_the_user_does_not_have_are_ignored()
    {
        var layout = new[] { Row(Guid.NewGuid(), "crm.companies", hidden: true) };

        var menu = Nav(Crm, [RoleA], layout).Context("crm/firmalar");

        Assert.Contains("crm.companies", Ids(menu));
    }

    [Fact]
    public void Unknown_item_key_is_ignored()
    {
        var layout = new[] { Row(RoleA, "yok.olan", 1, hidden: true) };

        var menu = Nav(Crm, [RoleA], layout).Context("crm/firmalar");

        Assert.Equal(["crm.companies", "crm.contacts"], CustomerIds(menu));
    }

    [Fact]
    public void Layout_never_makes_unauthorized_item_visible()
    {
        // Kullanıcının firma izni yok; düzen "firmalar 1. sırada görünür" dese de menüde çıkmaz.
        var layout = new[] { Row(RoleA, "crm.companies", 1) };

        var menu = Nav([CrmAccess.ContactsRead], [RoleA], layout).Context("crm/kisiler");

        Assert.DoesNotContain("crm.companies", Ids(menu));
        Assert.Contains("crm.contacts", Ids(menu));
    }

    [Fact]
    public void Footer_group_is_not_hidden_or_reordered()
    {
        string[] perms = [.. Crm, CoreAccess.ManageSettings];
        var layout = new[] { Row(RoleA, "crm.settings", hidden: true) };

        var menu = Nav(perms, [RoleA], layout).Context("crm/firmalar");

        Assert.Contains("crm.settings", Ids(menu));
    }

    [Fact]
    public void Rule_returns_same_list_without_rows()
    {
        MenuEntry[] entries = [new(AppCatalog.All.First(a => a.Id == "crm.companies"), "Firmalar", AppCatalog.CrmCustomersGroup, 10)];

        Assert.Same(entries, MenuLayoutRules.Apply(entries, [RoleA], [], []));
    }
}
