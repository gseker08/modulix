namespace Modulix.Web.Services;

/// <summary>Panelde bir başlık altındaki ana uygulamalar; <paramref name="Group"/> boşsa başlıksız kümedir.</summary>
public sealed record AppRootGroup(AppGroup? Group, IReadOnlyList<AppDefinition> Apps);

/// <summary>Sol menüdeki bir bağlantı: ekran tanımı, (ev sahibinin menüsünde değişmişse) görünen adı ve bağlantı adresi.</summary>
public sealed record MenuEntry(AppDefinition App, string Name, string? GroupId, int Order, string? Href = null)
{
    /// <summary>Tıklanınca gidilen adres: ev sahibinin verdiği özel adres, yoksa ekranın route'u.</summary>
    public string? Link => Href ?? App.Route;
}

/// <summary>Sol menüdeki bölüm: başlık (boşsa başlıksız küme) ve bağlantıları.</summary>
public sealed record MenuSection(AppGroup? Group, IReadOnlyList<MenuEntry> Entries);

/// <summary>Rol menü düzeni ekranında bir uygulamanın menüsü: katalogdaki bölümler ve satırlar (izinden bağımsız).</summary>
public sealed record EditableMenu(AppDefinition Root, IReadOnlyList<MenuSection> Sections);

/// <summary>Bir sayfanın menü bağlamı: hangi uygulamanın menüsü gösterilir, hangi ekran aktiftir.</summary>
public sealed record MenuContext(AppDefinition Root, AppDefinition Active, IReadOnlyList<MenuSection> Sections);

/// <summary>
/// <see cref="AppCatalog"/> tanımlarını oturuma göre süzer: tenant'ın açık modülü, platform yöneticiliği ve kullanıcının
/// izinleri birlikte değerlendirilir. Sonuç önbelleğe alınmaz; tenant değişince oturum yeniden okunur ve liste
/// yeniden hesaplanır, önceki tenant'ın menüsü kalmaz.
/// </summary>
public sealed class AppNavigation(SessionState session)
{
    public bool IsVisible(AppDefinition app)
    {
        if (app.PlatformOnly && !session.IsPlatformAdministrator)
        {
            return false;
        }

        if (app.ModuleKey is not null && !session.IsModuleEnabled(app.ModuleKey))
        {
            return false;
        }

        if (app.Permission is not null && !session.Can(app.Permission))
        {
            return false;
        }

        return app.AnyPermission is null || session.CanAny(app.AnyPermission);
    }

    /// <summary>Kullanıcının kullanabildiği panel kısayolları, sıralı: modülü açık ve izni olanlar.</summary>
    public IReadOnlyList<AppShortcut> Shortcuts() =>
        AppCatalog.Shortcuts
            .Where(s => (s.ModuleKey is null || session.IsModuleEnabled(s.ModuleKey))
                && (s.Permission is null || session.Can(s.Permission))
                && (s.AnyPermission is null || session.CanAny(s.AnyPermission)))
            .OrderBy(s => s.Order)
            .ToList();

    /// <summary>Kullanıcının görebildiği alt uygulamalar, sıralı.</summary>
    public IReadOnlyList<AppDefinition> Children(string parentId) => Children(parentId, IsVisible);

    private static IReadOnlyList<AppDefinition> Children(string parentId, Func<AppDefinition, bool> visible) =>
        AppCatalog.All
            .Where(a => a.ParentId == parentId && visible(a))
            .OrderBy(a => a.Order)
            .ToList();

    /// <summary>
    /// Ana uygulamanın sol menü bağlantıları: kendi görünür alt uygulamaları ile ona ev sahipliği yapılan
    /// uygulamaların (<see cref="AppDefinition.HostId"/>) görünür ve yerleşimi tanımlı (<see cref="AppDefinition.Hosted"/>) ekranları.
    /// Ev sahipliği yalnızca menü yerleşimidir; her ekran kendi modül ve izin denetimiyle süzülür.
    /// </summary>
    public IReadOnlyList<MenuEntry> MenuEntries(string rootId) =>
        MenuLayoutRules.Apply(CatalogEntries(rootId, IsVisible), session.Access?.MenuLayout);

    /// <summary>Ana uygulamanın sol menü bölümleri: rol düzenindeki satır ve bölüm sırası uygulanmış (<see cref="MenuLayoutRules"/>).</summary>
    public IReadOnlyList<MenuSection> MenuSections(string rootId) =>
        MenuLayoutRules.OrderSections(rootId, Sections(MenuEntries(rootId)), session.Access?.MenuLayout);

    /// <summary>
    /// Rol düzeni uygulanmamış (katalog) menü bağlantıları; <paramref name="visible"/> hangi ekranların sayıldığını belirler.
    /// Uygulama panelinde bir uygulamanın listelenip listelenmeyeceği bunlara bakar: düzen bir uygulamanın bütün bağlantılarını
    /// gizlese de uygulama panelden kaybolmaz.
    /// </summary>
    private IReadOnlyList<MenuEntry> CatalogEntries(string rootId, Func<AppDefinition, bool> visible)
    {
        // MenuParentId dolu ekranlar (ör. teklif onayları) ayrı satır olmaz; üst satırın altında sayılır.
        List<MenuEntry> entries = Children(rootId, visible)
            .Where(a => a.MenuParentId is null)
            .Select(a => new MenuEntry(a, AppNames.Of(a), a.Group, a.Order))
            .ToList();

        foreach (AppDefinition hosted in AppCatalog.All.Where(a => a.ParentId is null && a.HostId == rootId && visible(a)))
        {
            foreach (AppDefinition child in Children(hosted.Id, visible).Where(c => c.Hosted is { ParentEntryId: null }))
            {
                HostPlacement place = child.Hosted!;
                entries.Add(new MenuEntry(child, AppNames.MenuNameOf(child), place.Group, place.Order, place.Route));
            }
        }

        return entries;
    }

    /// <summary>
    /// Görünür ana uygulamalar. Erişilebilir ekranı ya da kendi giriş ekranı olmayan boş modül listelenmez; menüsü
    /// görünür bir ev sahibinin (ör. CRM) içinde gösterilen uygulama ayrı listelenmez. Rol düzenindeki uygulama sırası (<c>app.&lt;uygulama&gt;</c> satırı)
    /// grubu içinde uygulanır (<see cref="MenuLayoutRules.OrderApps"/>).
    /// </summary>
    public IReadOnlyList<AppDefinition> Roots() =>
        MenuLayoutRules.OrderApps(
            [.. AppCatalog.All
                .Where(a => a.ParentId is null && IsVisible(a) && (a.Route is not null || CatalogEntries(a.Id, IsVisible).Count > 0) && HostOf(a) is null)
                .OrderBy(a => a.Order)],
            session.Access?.MenuLayout);

    /// <summary>Uygulamanın ekranlarını menüsünde gösteren ve şu an görünür olan ev sahibi; yoksa <c>null</c>.</summary>
    public AppDefinition? HostOf(AppDefinition root)
    {
        if (root.HostId is null || Find(root.HostId) is not { } host)
        {
            return null;
        }

        return IsVisible(host) && CatalogEntries(host.Id, IsVisible).Count > 0 ? host : null;
    }

    /// <summary>Panele tıklanınca açılan ilk ekran: kendi route'u, yoksa menüsündeki ilk bağlantı.</summary>
    public string? EntryRoute(AppDefinition root) =>
        root.Route ?? MenuSections(root.Id).SelectMany(s => s.Entries).FirstOrDefault()?.Link;

    /// <summary>
    /// Verilen (görünür, sıralı) uygulamaları grup sırasına göre kümeler; kümeler içinde sıra korunur. Hiç uygulaması
    /// olmayan grup yer almaz; grubu tanımsız uygulamalar sonda başlıksız (<c>Group == null</c>) kümede toplanır.
    /// </summary>
    public IReadOnlyList<AppRootGroup> Grouped(IReadOnlyList<AppDefinition> listed)
    {
        List<AppRootGroup> result = [];
        foreach (AppGroup group in AppCatalog.Groups.OrderBy(g => g.Order))
        {
            List<AppDefinition> apps = listed.Where(a => a.Group == group.Id).ToList();
            if (apps.Count > 0)
            {
                result.Add(new AppRootGroup(group, apps));
            }
        }

        List<AppDefinition> ungrouped = listed.Where(a => AppCatalog.Groups.All(g => g.Id != a.Group)).ToList();
        if (ungrouped.Count > 0)
        {
            result.Add(new AppRootGroup(null, ungrouped));
        }

        return result;
    }

    /// <summary>Menü bağlantılarını bölümlere ayırır: bölümler grup sırasında, bağlantılar bölüm içinde sıralı; boş bölüm yer almaz.</summary>
    public static IReadOnlyList<MenuSection> Sections(IReadOnlyList<MenuEntry> entries)
    {
        List<MenuSection> result = [];
        foreach (AppGroup group in AppCatalog.Groups.OrderBy(g => g.Order))
        {
            List<MenuEntry> inGroup = entries.Where(e => e.GroupId == group.Id).OrderBy(e => e.Order).ToList();
            if (inGroup.Count > 0)
            {
                result.Add(new MenuSection(group, inGroup));
            }
        }

        List<MenuEntry> ungrouped = entries.Where(e => AppCatalog.Groups.All(g => g.Id != e.GroupId)).OrderBy(e => e.Order).ToList();
        if (ungrouped.Count > 0)
        {
            result.Add(new MenuSection(null, ungrouped));
        }

        return result;
    }

    /// <summary>
    /// Rol menü düzeni ekranının satırları (E8): firmada modülü açık, platforma özel olmayan uygulamaların katalog menüleri. İzin
    /// bakılmaz: düzeni düzenleyen yönetici, rolün göreceği ekranları kendi izninden bağımsız görür; gizleme yetki değildir.
    /// Katalog sırasıyla; rol düzeni uygulanmaz.
    /// </summary>
    public IReadOnlyList<EditableMenu> EditableMenus()
    {
        bool Editable(AppDefinition app) =>
            !app.PlatformOnly && (app.ModuleKey is null || session.IsModuleEnabled(app.ModuleKey));

        List<EditableMenu> menus = [];
        foreach (AppDefinition root in AppCatalog.All.Where(a => a.ParentId is null && Editable(a)).OrderBy(a => a.Order))
        {
            // Ev sahibinin menüsünde gösterilen uygulama ayrı satırlanmaz; ev sahibinin altında düzenlenir.
            bool hosted = root.HostId is not null && Find(root.HostId) is { } host && Editable(host) && CatalogEntries(host.Id, Editable).Count > 0;
            IReadOnlyList<MenuEntry> entries = hosted ? [] : CatalogEntries(root.Id, Editable);
            if (entries.Count > 0)
            {
                menus.Add(new EditableMenu(root, Sections(entries)));
            }
        }

        return menus;
    }

    public AppDefinition? Find(string id) => AppCatalog.All.FirstOrDefault(a => a.Id == id);

    /// <summary>Kullanıcının açabildiği modül ayar ekranları (Yönetim girişindeki bağlantılar); modülü kapalı ya da izni olmayan yer almaz.</summary>
    public IReadOnlyList<AppDefinition> ModuleSettings() =>
        AppCatalog.All
            .Where(a => a.ModuleKey is not null && a.Route == AppCatalog.ModuleSettingsRoute(a.ModuleKey) && IsVisible(a))
            .ToList();

    /// <summary>
    /// Sayfanın menü bağlamı: aktif ekran, menüsü gösterilecek uygulama (ev sahibi görünürse o) ve bölümleri. Aktif ekran
    /// ya da uygulaması görünmüyorsa <c>null</c>. Aktif ekran, tanımın tamamından bulunur; yetkisiz sayfaya doğrudan gidilse de
    /// bağlam doğrudur (sayfa erişimini yine AuthGate/sunucu denetler), ama o uygulamanın menüsü kullanıcıya görünmüyorsa gösterilmez.
    /// </summary>
    public MenuContext? Context(string relativePath)
    {
        AppDefinition? active = Resolve(relativePath);
        AppDefinition? root = active is null ? null : RootOf(active);
        if (active is null || root is null)
        {
            return null;
        }

        AppDefinition menuRoot = HostOf(root) ?? root;
        if (Roots().All(r => r.Id != menuRoot.Id))
        {
            return null;
        }

        // Ayrı satırı olmayan ekran (ör. teklif onayları) aktifken üst satırı işaretlenir.
        string? parentId = active.MenuParentId ?? (menuRoot.Id != root.Id ? active.Hosted?.ParentEntryId : null);
        if (parentId is not null && Find(parentId) is { } parent)
        {
            active = parent;
        }

        return new MenuContext(menuRoot, active, MenuSections(menuRoot.Id));
    }

    /// <summary>
    /// Route'a göre aktif alt uygulama (en uzun eşleşen) ve ana uygulaması; hiçbiri değilse <c>null</c>. Tanımın tamamına
    /// bakar, böylece yetkisiz sayfaya doğrudan gidilse de bağlam doğru bulunur (sayfa erişimini yine AuthGate/sunucu denetler).
    /// </summary>
    public static AppDefinition? Resolve(string relativePath)
    {
        string path = Normalize(relativePath);
        AppDefinition? best = null;
        foreach (AppDefinition app in AppCatalog.All)
        {
            if (app.Route is null)
            {
                continue;
            }

            bool matches = app.ExactMatch
                ? path == app.Route
                : path == app.Route || path.StartsWith(app.Route + "/", StringComparison.Ordinal);
            if (matches && (best is null || app.Route.Length > best.Route!.Length))
            {
                best = app;
            }
        }

        return best;
    }

    /// <summary>Alt uygulamanın (ya da ana uygulamanın kendisinin) ana uygulaması.</summary>
    public AppDefinition? RootOf(AppDefinition app) => app.ParentId is null ? app : Find(app.ParentId);

    private static string Normalize(string relativePath)
    {
        int cut = relativePath.IndexOfAny(['?', '#']);
        string path = cut >= 0 ? relativePath[..cut] : relativePath;
        return path.Trim('/').ToLowerInvariant();
    }
}
