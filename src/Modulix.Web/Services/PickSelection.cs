namespace Modulix.Web.Services;

/// <summary>
/// Atama penceresindeki (<c>PickDialog</c>) bir satır. <see cref="Group"/> doluysa liste gruplu çizilir (ör. izinler modüle
/// göre); <see cref="Locked"/> satırın işareti değiştirilemez (ör. yalnızca yöneticinin verebildiği rol).
/// </summary>
public sealed record PickItem<TKey>(TKey Key, string Name, string? Note = null, string? Group = null, bool Locked = false)
    where TKey : notnull;

/// <summary>
/// Atama penceresinin çalışma kopyası: pencere kapanana kadar sayfadaki seçim değişmez, "İptal" hiçbir şey bırakmaz.
/// Kilitli satır değişmez; listede görünmeyen (ör. modülü kapalı izin) başlangıç seçimleri sonuçta korunur.
/// </summary>
public sealed class PickSelection<TKey>
    where TKey : notnull
{
    private readonly IReadOnlyList<PickItem<TKey>> items;
    private readonly Dictionary<TKey, PickItem<TKey>> byKey;
    private readonly HashSet<TKey> selected;
    private readonly List<TKey> hidden;
    private readonly Func<TKey, bool, IEnumerable<TKey>>? linked;

    /// <param name="linked">Bir satır işaretlenince/kaldırılınca aynı duruma getirilecek diğer satırlar (ör. düzenleme izni görüntülemeyi gerektirir).</param>
    public PickSelection(IReadOnlyList<PickItem<TKey>> items, IEnumerable<TKey> initial, Func<TKey, bool, IEnumerable<TKey>>? linked = null)
    {
        this.items = items;
        this.linked = linked;
        byKey = items.ToDictionary(i => i.Key);
        var start = initial.Distinct().ToList();
        selected = start.Where(byKey.ContainsKey).ToHashSet();
        hidden = start.Where(k => !byKey.ContainsKey(k)).ToList();
    }

    /// <summary>Listede görünen satırlardan seçili olanların sayısı.</summary>
    public int Count => selected.Count;

    public bool IsSelected(TKey key) => selected.Contains(key);

    /// <summary>Seçili satırlar, listedeki sırayla.</summary>
    public IEnumerable<PickItem<TKey>> SelectedItems => items.Where(i => selected.Contains(i.Key));

    /// <summary>Kaydedilecek küme: görünen seçimler ve listede olmayan başlangıç seçimleri.</summary>
    public IReadOnlyList<TKey> Result => [.. items.Where(i => selected.Contains(i.Key)).Select(i => i.Key), .. hidden];

    public void Toggle(TKey key, bool on)
    {
        if (!Set(key, on) || linked is null)
        {
            return;
        }

        foreach (var other in linked(key, on))
        {
            Set(other, on);
        }
    }

    /// <summary>Verilen satırların (ör. bir grubun görünen satırları) hepsini işaretler ya da kaldırır; kilitliler değişmez.</summary>
    public void SetAll(IEnumerable<PickItem<TKey>> targets, bool on)
    {
        foreach (var item in targets.ToList())
        {
            Toggle(item.Key, on);
        }
    }

    /// <summary>Arama (ad ve not içinde), grup ve "yalnızca seçilenler" süzgecinden geçen satırlar.</summary>
    public IReadOnlyList<PickItem<TKey>> Visible(string? search, string? group = null, bool onlySelected = false)
    {
        var term = search?.Trim();
        return items
            .Where(i => string.IsNullOrEmpty(group) || i.Group == group)
            .Where(i => !onlySelected || selected.Contains(i.Key))
            .Where(i => string.IsNullOrEmpty(term)
                || i.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || (i.Note?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .ToList();
    }

    /// <summary>
    /// Tek seçimli pencere: verilen satır seçilir, diğer bütün seçimler (listede görünmeyenler dahil) kalkar. Kilitli satır seçilemez;
    /// bağlı satır kuralı tek seçimde uygulanmaz.
    /// </summary>
    public void SelectOnly(TKey key)
    {
        if (!byKey.TryGetValue(key, out var item) || item.Locked)
        {
            return;
        }

        selected.Clear();
        hidden.Clear();
        selected.Add(key);
    }

    private bool Set(TKey key, bool on)
    {
        if (!byKey.TryGetValue(key, out var item) || item.Locked)
        {
            return false;
        }

        if (on)
        {
            selected.Add(key);
        }
        else
        {
            selected.Remove(key);
        }

        return true;
    }
}
