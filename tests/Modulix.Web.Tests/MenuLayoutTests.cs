using System.Reflection;
using Modulix.Shared.Access;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>
/// E8 dilim 2b: rol bazlı menü düzeninin kullanıcının menüsüne uygulanması. Kural: düzeni tanımlı rollerden herhangi birinde
/// görünen öğe görünür; sıra en küçüğüdür; düzeni olan rolü yoksa katalog düzeni. Gizlemek yetki değildir: izin süzgeci ayrıca çalışır.
/// </summary>
public class MenuLayoutTests
{
    private static readonly Guid RoleA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid RoleB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static readonly string[] Permissions = [CrmAccess.CompaniesRead, CrmAccess.ContactsRead, CrmAccess.LeadsRead];

    private static AppNavigation Nav(IReadOnlyList<RoleMenuRowDto>? layout, string[]? permissions = null)
    {
        var session = new SessionState(new ApiClient(new HttpClient()));
        Set(session, nameof(SessionState.Access), new MyAccessResponse([CrmAccess.ModuleKey], permissions ?? Permissions) { MenuLayout = layout ?? [] });
        Set(session, nameof(SessionState.User), new MeResponse(Guid.NewGuid(), "a@example.com", "Test", Guid.NewGuid(), [], false));
        return new AppNavigation(session);
    }

    private static void Set(SessionState session, string property, object value) =>
        typeof(SessionState).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(session, value);

    private static string[] Ids(AppNavigation nav) =>
        [.. nav.Context("crm/firmalar")!.Sections.SelectMany(s => s.Entries).Select(e => e.App.Id)];

    private static string[] CustomerIds(AppNavigation nav) =>
        [.. nav.Context("crm/firmalar")!.Sections.Where(s => s.Group?.Id == AppCatalog.CrmCustomersGroup).SelectMany(s => s.Entries).Select(e => e.App.Id)];

    [Fact]
    public void Without_a_layout_the_catalog_menu_is_unchanged()
    {
        Assert.Equal(Ids(Nav(null)), Ids(Nav([])));
        Assert.Contains("crm.leads", Ids(Nav(null)));
    }

    [Fact]
    public void Hidden_item_is_removed_only_when_every_layout_role_hides_it()
    {
        // Tek rolde gizli → görünmez.
        var onlyA = Nav([new(RoleA, "crm.leads", null, true)]);
        Assert.DoesNotContain("crm.leads", Ids(onlyA));
        Assert.Contains("crm.companies", Ids(onlyA));

        // İkinci rolün düzeni var ama öğeyi gizlemiyor (satırı yok) → herhangi birinde görünür → görünür.
        var shownByB = Nav([new(RoleA, "crm.leads", null, true), new(RoleB, "crm.contacts", null, true)]);
        Assert.Contains("crm.leads", Ids(shownByB));
        Assert.Contains("crm.contacts", Ids(shownByB)); // yalnızca B gizliyor; A'nın düzeninde satırı yok, yani A gösteriyor

        // Her iki rol de gizlerse görünmez.
        var bothHide = Nav([new(RoleA, "crm.leads", null, true), new(RoleB, "crm.leads", null, true)]);
        Assert.DoesNotContain("crm.leads", Ids(bothHide));
    }

    [Fact]
    public void Order_is_the_smallest_explicit_order_among_roles_that_show_the_item()
    {
        // Katalogda Firmalar (10) Kişiler'den (20) önce; A rolü Kişiler'i öne alır.
        var first = Nav([new(RoleA, "crm.contacts", 1, false), new(RoleA, "crm.companies", 2, false)]);
        Assert.Equal(["crm.contacts", "crm.companies"], CustomerIds(first).Take(2));

        // B rolü Kişiler'i gizler: A hâlâ gösterdiği için görünür ve A'nın sırası geçerli; B'nin sırası (gizli satır) sayılmaz.
        var hiddenByB = Nav([
            new(RoleA, "crm.contacts", 5, false), new(RoleA, "crm.companies", 6, false),
            new(RoleB, "crm.contacts", 1, true)]);
        Assert.Equal(["crm.contacts", "crm.companies"], CustomerIds(hiddenByB).Take(2));

        // İki rol farklı sıra verirse en küçüğü kazanır.
        var smallest = Nav([new(RoleA, "crm.contacts", 50, false), new(RoleB, "crm.contacts", 1, false)]);
        Assert.Equal("crm.contacts", CustomerIds(smallest)[0]);
    }

    [Fact]
    public void Unknown_keys_are_ignored_and_permissions_still_filter()
    {
        // Katalogda olmayan satır sessizce yok sayılır.
        var unknown = Nav([new(RoleA, "crm.no-such-screen", 1, true)]);
        Assert.Equal(Ids(Nav(null)), Ids(unknown));

        // Düzen bir ekranı "görünür" bırakamaz: izni olmayan kullanıcıda satır yine yoktur (gizlemek yetki değildir, göstermek de değildir).
        var noLeads = Nav([new(RoleA, "crm.leads", 1, false)], [CrmAccess.CompaniesRead]);
        Assert.DoesNotContain("crm.leads", Ids(noLeads));
    }

    [Fact]
    public void Hiding_every_entry_keeps_the_app_in_the_launcher()
    {
        var layout = Nav(null).Context("crm/firmalar")!.Sections.SelectMany(s => s.Entries)
            .Select(e => new RoleMenuRowDto(RoleA, e.App.Id, null, true)).ToList();
        var nav = Nav(layout);

        Assert.Empty(nav.Context("crm/firmalar")!.Sections);
        Assert.Contains(AppCatalog.Crm, nav.Roots().Select(r => r.Id));
    }

    [Fact]
    public void Editable_menus_ignore_permissions_and_platform_only_screens()
    {
        var nav = Nav(null, []);

        var crm = Assert.Single(nav.EditableMenus(), m => m.Root.Id == AppCatalog.Crm);
        var ids = crm.Sections.SelectMany(s => s.Entries).Select(e => e.App.Id).ToList();
        Assert.Contains("crm.leads", ids);
        Assert.Contains("crm.companies", ids);
        Assert.DoesNotContain(nav.EditableMenus(), m => m.Root.PlatformOnly);
        Assert.DoesNotContain(nav.EditableMenus(), m => m.Root.ModuleKey is not null && m.Root.ModuleKey != CrmAccess.ModuleKey);
    }
}
