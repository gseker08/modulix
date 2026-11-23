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
    public void Items_without_an_explicit_order_follow_the_ordered_ones_in_catalog_order()
    {
        // Satış bölümü katalogda: Müşteri adayları 10, Fırsatlar 20, Teklifler 30. A rolünün düzeninde Fırsatlar'ın satırı yok
        // (ör. sonradan eklendi); B rolünün düzeni var ama o da Fırsatlar'a sıra vermiyor. Eski kuralda Fırsatlar katalog sırası (20)
        // ile Teklifler'in (25) önüne düşerdi; şimdi sıra verilmeyen öğe açık sıralıların arkasına gider.
        var nav = Nav([new(RoleA, "crm.quotes", 25, false), new(RoleA, "crm.leads", 35, false), new(RoleB, "crm.contacts", null, false)],
            [CrmAccess.CompaniesRead, CrmAccess.LeadsRead, CrmAccess.OpportunitiesRead, CrmAccess.QuotesRead]);

        string[] sales = [.. nav.Context("crm/firmalar")!.Sections.Where(s => s.Group?.Id == AppCatalog.CrmSalesGroup).SelectMany(s => s.Entries).Select(e => e.App.Id)];
        Assert.Equal(["crm.quotes", "crm.leads", "crm.opportunities"], sales);
    }

    [Fact]
    public void A_section_where_no_role_gives_an_order_keeps_the_catalog_order()
    {
        // Yalnızca gizleme satırı olan (sırası null) bölümde sıra değişmez.
        var hideOnly = Nav([new(RoleA, "crm.leads", null, true)]);
        Assert.Equal(CustomerIds(Nav(null)).Where(id => id != "crm.leads"), CustomerIds(hideOnly));
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

    private static string[] GroupIds(AppNavigation nav) =>
        [.. nav.Context("crm/firmalar")!.Sections.Select(s => s.Group?.Id ?? "-")];

    private static string Key(string group) => MenuLayoutRules.SectionKey("crm", group);

    [Fact]
    public void Section_order_row_moves_the_section_and_leaves_footer_in_place()
    {
        string[] catalog = GroupIds(Nav(null));
        int sales = Array.IndexOf(catalog, AppCatalog.CrmSalesGroup);
        int customers = Array.IndexOf(catalog, AppCatalog.CrmCustomersGroup);
        Assert.True(customers < sales);

        // Satış bölümü Müşterilerden önce gelsin.
        string[] moved = GroupIds(Nav([new(RoleA, Key(AppCatalog.CrmSalesGroup), 1, false), new(RoleA, Key(AppCatalog.CrmCustomersGroup), 2, false)]));
        Assert.True(Array.IndexOf(moved, AppCatalog.CrmSalesGroup) < Array.IndexOf(moved, AppCatalog.CrmCustomersGroup));
        Assert.Equal(catalog.Length, moved.Length);

        // Alt sabitli bölüm (CRM Ayarları) taşınmaz, yeri değişmez.
        Assert.Equal(Array.IndexOf(catalog, AppCatalog.CrmSettingsGroup), Array.IndexOf(moved, AppCatalog.CrmSettingsGroup));
        Assert.Equal(catalog.Order().ToArray(), moved.Order().ToArray());
    }

    [Fact]
    public void Section_order_takes_the_smallest_order_across_roles_and_ignores_other_apps_and_unknown_sections()
    {
        string[] catalog = GroupIds(Nav(null));

        // İki rol farklı sıra verirse en küçüğü geçer: B satışı en öne alır.
        string[] merged = GroupIds(Nav([new(RoleA, Key(AppCatalog.CrmSalesGroup), 900, false), new(RoleB, Key(AppCatalog.CrmSalesGroup), 1, false)]));
        Assert.Equal(AppCatalog.CrmSalesGroup, merged.First(g => g != "-"));

        // Başka uygulamanın ve katalogda olmayan bölümün satırı etkisizdir; bölüm satırı öğe görünürlüğünü bozmaz.
        var other = Nav([new(RoleA, MenuLayoutRules.SectionKey("hr", AppCatalog.CrmSalesGroup), 1, false), new(RoleA, Key("yok"), 1, false)]);
        Assert.Equal(catalog, GroupIds(other));
        Assert.Contains("crm.leads", Ids(other));
    }
}
