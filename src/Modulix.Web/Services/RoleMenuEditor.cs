using Modulix.Shared.Access;

namespace Modulix.Web.Services;

/// <summary>Rol menü düzeni ekranında düzenlenebilen bir öğe: katalog kimliği, görünen ad ve bulunduğu bölüm.</summary>
/// <param name="Key">Katalog öğe kimliği (<see cref="AppDefinition.Id"/>); sunucudaki <c>ItemKey</c>.</param>
/// <param name="Name">Menüde görünen ad.</param>
/// <param name="Icon">İkon adı.</param>
/// <param name="SectionKey">Öğenin bölümü (<c>{uygulama}/{grup}</c>); sıra yalnızca bölüm içinde anlamlıdır.</param>
/// <param name="DefaultOrder">Katalogdaki bölüm içi sıra.</param>
public sealed record RoleMenuCatalogItem(string Key, string Name, string Icon, string SectionKey, int DefaultOrder);

/// <summary>Ekrandaki bölüm: uygulama başlığı, bölüm başlığı (yoksa <c>null</c>) ve sıralı öğeleri.</summary>
public sealed record RoleMenuSection(string Key, string RootName, string? GroupName, IReadOnlyList<RoleMenuCatalogItem> Items);

/// <summary>
/// Rol menü düzeni düzenleyicisinin arayüzden bağımsız mantığı (E8 dilim 2b-2): katalogdan düzenlenebilir öğeleri seçer,
/// kayıtlı düzeni uygular, taşıma ve gizlemeyi tutar, kayıt isteğini üretir. Kapsam öğe düzeyindedir: öğe sırası bölüm içindedir,
/// modül ya da bölüm sırası düzenlenmez. Gizlemek yetki değildir.
/// <para>
/// Kayıt kuralı: sırası katalog sırasından farklı olan bölümde bütün öğelere açık sıra (10, 20, ...) yazılır; sırası değişmeyen
/// bölümde sıra boş kalır (katalog sırası). Gizli öğe her durumda yazılır. Katalogda olmayan (ör. modülü şu an kapalı) kayıtlı
/// satırlar olduğu gibi korunur, böylece modül yeniden açılınca düzen kaybolmaz.
/// </para>
/// </summary>
public sealed class RoleMenuEditor
{
    /// <summary>Bölümde açık sıra verilirken kullanılan adım.</summary>
    public const int OrderStep = 10;

    private readonly Dictionary<string, SectionState> sections = new(StringComparer.Ordinal);
    private readonly List<string> sectionOrder = [];
    private readonly Dictionary<string, RoleMenuCatalogItem> catalog = new(StringComparer.Ordinal);
    private readonly Func<string, (string Root, string? Group)> titleOf;
    private List<RoleMenuItemDto> preserved = [];
    private string initial = string.Empty;

    /// <param name="items">Düzenlenebilir katalog öğeleri (<see cref="CatalogItems"/>).</param>
    /// <param name="saved">Rolün kayıtlı düzeni.</param>
    /// <param name="titleOf">Bölüm anahtarından (uygulama adı, bölüm adı) başlıkları verir; verilmezse <see cref="TitlesOf"/>.</param>
    public RoleMenuEditor(
        IEnumerable<RoleMenuCatalogItem> items,
        IEnumerable<RoleMenuItemDto> saved,
        Func<string, (string Root, string? Group)>? titleOf = null)
    {
        this.titleOf = titleOf ?? TitlesOf;
        foreach (RoleMenuCatalogItem item in items)
        {
            catalog[item.Key] = item;
            if (!sections.TryGetValue(item.SectionKey, out SectionState? state))
            {
                state = new SectionState();
                sections[item.SectionKey] = state;
                sectionOrder.Add(item.SectionKey);
            }

            state.Default.Add(item);
        }

        foreach (SectionState state in sections.Values)
        {
            state.Default = state.Default.OrderBy(i => i.DefaultOrder).ToList();
        }

        Load(saved);
    }

    /// <summary>Katalogdan düzenlenebilir öğeler: ayrı menü satırı olan, platform dışı, modülü tenant'ta açık ve sabit alt bölümde olmayan ekranlar.</summary>
    public static IReadOnlyList<RoleMenuCatalogItem> CatalogItems(Func<string, bool> moduleEnabled)
    {
        List<RoleMenuCatalogItem> result = [];
        foreach (AppDefinition app in AppCatalog.All.Where(a => a.ParentId is not null && !a.PlatformOnly && a.MenuParentId is null))
        {
            if (app.ModuleKey is not null && !moduleEnabled(app.ModuleKey))
            {
                continue;
            }

            AppDefinition? root = AppCatalog.All.FirstOrDefault(a => a.Id == app.ParentId);
            if (root is null || root.PlatformOnly)
            {
                continue;
            }

            string rootId = root.Id;
            string? group = app.Group;
            int order = app.Order;
            string name = AppNames.Of(app);

            // Ev sahibinin (ör. CRM) menüsünde gösterilen ekran, ev sahibinin bölümünde ve sırasında düzenlenir.
            AppDefinition? host = root.HostId is null ? null : AppCatalog.All.FirstOrDefault(a => a.Id == root.HostId);
            if (host is not null && (host.ModuleKey is null || moduleEnabled(host.ModuleKey)))
            {
                if (app.Hosted is not { } place || place.ParentEntryId is not null)
                {
                    // Ev sahibinin menüsünde yeri olmayan ya da üst satırın altında sayılan ekran ayrı satır değildir.
                    continue;
                }

                rootId = host.Id;
                group = place.Group;
                order = place.Order;
                name = AppNames.MenuNameOf(app);
            }

            if (group is not null && AppCatalog.Groups.FirstOrDefault(g => g.Id == group)?.Footer == true)
            {
                continue;
            }

            result.Add(new RoleMenuCatalogItem(app.Id, name, app.Icon, $"{rootId}/{group}", order));
        }

        return result;
    }

    /// <summary>Bölüm anahtarının başlıkları: uygulama adı ve (varsa) bölüm başlığı.</summary>
    public static (string Root, string? Group) TitlesOf(string sectionKey)
    {
        int slash = sectionKey.IndexOf('/');
        string rootId = sectionKey[..slash];
        string groupId = sectionKey[(slash + 1)..];
        AppDefinition? root = AppCatalog.All.FirstOrDefault(a => a.Id == rootId);
        AppGroup? group = AppCatalog.Groups.FirstOrDefault(g => g.Id == groupId);
        return (root is null ? rootId : AppNames.Of(root), group is null ? null : AppNames.Of(group));
    }

    /// <summary>Ekrandaki bölümler: uygulama sırasıyla, uygulama içinde grup sırasıyla; öğeler bölümün geçerli sırasındadır.</summary>
    public IReadOnlyList<RoleMenuSection> Sections =>
        sectionOrder
            .OrderBy(RootOrder)
            .ThenBy(GroupOrder)
            .Select(key =>
            {
                (string root, string? group) = titleOf(key);
                return new RoleMenuSection(key, root, group, sections[key].Current.Select(k => catalog[k]).ToList());
            })
            .ToList();

    public bool IsHidden(string key) => StateOf(key)?.Hidden.Contains(key) == true;

    public bool CanMove(string key, int delta)
    {
        SectionState? state = StateOf(key);
        if (state is null)
        {
            return false;
        }

        int target = state.Current.IndexOf(key) + delta;
        return target >= 0 && target < state.Current.Count;
    }

    /// <summary>Öğeyi bölümü içinde bir adım yukarı (<c>-1</c>) ya da aşağı (<c>+1</c>) taşır; taşınamıyorsa <c>false</c>.</summary>
    public bool Move(string key, int delta)
    {
        if (!CanMove(key, delta))
        {
            return false;
        }

        List<string> current = StateOf(key)!.Current;
        int index = current.IndexOf(key);
        (current[index], current[index + delta]) = (current[index + delta], current[index]);
        return true;
    }

    public void SetHidden(string key, bool hidden)
    {
        if (StateOf(key) is not { } state)
        {
            return;
        }

        if (hidden)
        {
            state.Hidden.Add(key);
        }
        else
        {
            state.Hidden.Remove(key);
        }
    }

    /// <summary>Düzeni katalog düzenine döndürür: sıra katalogdaki gibi, hiçbir öğe gizli değil, korunan satırlar da atılır.</summary>
    public void Reset()
    {
        foreach (SectionState state in sections.Values)
        {
            state.Current = state.Default.Select(i => i.Key).ToList();
            state.Hidden.Clear();
        }

        preserved = [];
    }

    /// <summary>Kayıtlı düzenden bir fark var mı (Kaydet düğmesi).</summary>
    public bool IsDirty => Snapshot() != initial;

    /// <summary>Düzen katalog düzeninden farklı mı ("Varsayılana dön" yalnızca o zaman anlamlıdır).</summary>
    public bool IsCustomized => ToRequest().Items.Count > 0;

    /// <summary>Kayıtlı düzeni (sunucu yanıtı) yükler ve "değişiklik yok" durumuna döner.</summary>
    public void Load(IEnumerable<RoleMenuItemDto> saved)
    {
        List<RoleMenuItemDto> rows = saved.ToList();
        preserved = rows.Where(r => !catalog.ContainsKey(r.ItemKey)).ToList();
        foreach (SectionState state in sections.Values)
        {
            state.Hidden.Clear();
            state.Current = state.Default
                .OrderBy(i => rows.FirstOrDefault(r => r.ItemKey == i.Key)?.Order ?? i.DefaultOrder)
                .Select(i => i.Key)
                .ToList();
        }

        foreach (RoleMenuItemDto row in rows.Where(r => r.Hidden))
        {
            SetHidden(row.ItemKey, true);
        }

        initial = Snapshot();
    }

    /// <summary>Sunucuya gönderilecek tam düzen; hiçbir fark yoksa liste boştur (düzen kaldırılır).</summary>
    public SaveRoleMenuRequest ToRequest()
    {
        List<RoleMenuItemDto> items = [];
        foreach (string sectionKey in sectionOrder)
        {
            SectionState state = sections[sectionKey];
            bool reordered = !state.Current.SequenceEqual(state.Default.Select(i => i.Key));
            for (int i = 0; i < state.Current.Count; i++)
            {
                string key = state.Current[i];
                bool hidden = state.Hidden.Contains(key);
                if (reordered || hidden)
                {
                    items.Add(new RoleMenuItemDto(key, reordered ? (i + 1) * OrderStep : null, hidden));
                }
            }
        }

        items.AddRange(preserved);
        return new SaveRoleMenuRequest(items);
    }

    private string Snapshot() =>
        string.Join(
            "|",
            sectionOrder.Select(k => $"{k}:{string.Join(",", sections[k].Current)}:{string.Join(",", sections[k].Hidden.Order(StringComparer.Ordinal))}"));

    private SectionState? StateOf(string key) =>
        catalog.TryGetValue(key, out RoleMenuCatalogItem? item) ? sections[item.SectionKey] : null;

    private static int RootOrder(string sectionKey)
    {
        string rootId = sectionKey[..sectionKey.IndexOf('/')];
        return AppCatalog.All.FirstOrDefault(a => a.Id == rootId)?.Order ?? int.MaxValue;
    }

    private static int GroupOrder(string sectionKey)
    {
        string groupId = sectionKey[(sectionKey.IndexOf('/') + 1)..];
        return AppCatalog.Groups.FirstOrDefault(g => g.Id == groupId)?.Order ?? int.MaxValue;
    }

    private sealed class SectionState
    {
        public List<RoleMenuCatalogItem> Default { get; set; } = [];

        public List<string> Current { get; set; } = [];

        public HashSet<string> Hidden { get; } = new(StringComparer.Ordinal);
    }
}
