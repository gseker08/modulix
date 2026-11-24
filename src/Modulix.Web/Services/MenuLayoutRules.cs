using Modulix.Shared.Access;

namespace Modulix.Web.Services;

/// <summary>
/// Rol bazlı menü düzeninin (E8 dilim 2) kullanıcının menüsüne uygulanması. Yalnızca görünümdür: gizlemek yetki değildir; sayfa
/// erişimini AuthGate ve sunucu izinleri denetlemeye devam eder.
///
/// Kural: yalnızca düzeni tanımlı (en az bir satırı olan) roller sayılır; hiçbiri yoksa katalog düzeni geçerlidir. Bir öğe,
/// düzeni tanımlı rollerinden <b>herhangi birinde</b> görünüyorsa (o rolde gizli satırı yoksa) görünür. Sıra, öğeye sıra veren
/// ve öğeyi gösteren rollerin en küçüğüdür. Bölümde en az bir öğeye açık sıra verilmişse, sıra verilmeyen öğeler (hiçbir rolün
/// satırında sırası yok) açık sıralı öğelerin <b>arkasına</b> katalog sırasıyla dizilir: katalog numaraları ile rolün verdiği
/// numaralar (10, 20, …) aynı ölçekte olmadığından karıştırılmaz. Bölümde hiçbir öğeye sıra verilmemişse katalog sırası kalır.
/// Sıra bölüm içinde uygulanır, öğe bölümünü değiştirmez. Katalogda olmayan anahtarlı satır yok sayılır.
///
/// Bölüm sırası (E8 dilim 3) aynı tabloda <see cref="SectionKey"/> anahtarlı satırla saklanır (şema değişmez): <c>Order</c> bölümün
/// uygulamanın menüsündeki sırasıdır, <c>Hidden</c> kullanılmaz. Kural öğeyle aynıdır: sıra veren rollerin en küçüğü; sırası
/// verilmeyen bölüm açık sıralıların arkasına katalog sırasıyla gelir. Yalnızca başlıklı, alt sabitli olmayan bölümler yer değiştirir; alt sabitli (<c>Footer</c>) ve başlıksız
/// bölümler yerinde kalır.
/// </summary>
public static class MenuLayoutRules
{
    public static IReadOnlyList<MenuEntry> Apply(IReadOnlyList<MenuEntry> entries, IReadOnlyList<RoleMenuRowDto>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return entries;
        }

        HashSet<Guid> layoutRoles = rows.Select(r => r.RoleId).ToHashSet();
        List<(MenuEntry Entry, int? Explicit)> shown = [];
        foreach (MenuEntry entry in entries)
        {
            Dictionary<Guid, RoleMenuRowDto> mine = rows.Where(r => r.ItemKey == entry.App.Id).ToDictionary(r => r.RoleId);

            // Rolde satır yoksa öğe o rolde görünür (katalog varsayılanı); yalnızca açıkça gizlenmişse görünmez.
            List<Guid> showing = layoutRoles.Where(role => !mine.TryGetValue(role, out RoleMenuRowDto? row) || !row.Hidden).ToList();
            if (showing.Count == 0)
            {
                continue;
            }

            shown.Add((entry, showing.Where(mine.ContainsKey).Select(role => mine[role].Order).Where(o => o is not null).Min()));
        }

        // Bölüm (GroupId) başına en büyük açık sıra: sırasız öğeler bunun arkasına katalog sırasıyla gider.
        Dictionary<string, int> lastExplicit = shown
            .Where(s => s.Explicit is not null)
            .GroupBy(s => s.Entry.GroupId ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Max(s => s.Explicit!.Value));

        List<MenuEntry> result = [];
        foreach ((MenuEntry entry, int? explicitOrder) in shown)
        {
            if (explicitOrder is { } given)
            {
                result.Add(entry with { Order = given });
            }
            else if (lastExplicit.TryGetValue(entry.GroupId ?? string.Empty, out int last))
            {
                result.Add(entry with { Order = last + 1 + entry.Order });
            }
            else
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>Bir uygulamanın menüsündeki bölümün düzen anahtarı (ör. <c>section.crm.crm.customers</c>); uygulama kimliği ve bölüm kimliği küçük harf, rakam, nokta ve tiredir.</summary>
    public static string SectionKey(string rootId, string groupId) => $"section.{rootId}.{groupId}";

    /// <summary>Uygulamanın uygulama panelindeki sıra anahtarı (ör. <c>app.crm</c>); uygulama kimliği küçük harf, rakam, nokta ve tiredir.</summary>
    public static string AppKey(string rootId) => $"app.{rootId}";

    /// <summary>Uygulama panelindeki grubun sıra anahtarı (ör. <c>group.operations</c>); grup kimliği küçük harf, rakam, nokta ve tiredir.</summary>
    public static string GroupKey(string groupId) => $"group.{groupId}";

    /// <summary>
    /// Rolün uygulama paneli grup sırasını uygular (E8 dilim 5). Kural öğeyle aynıdır: sıra veren satırların en küçüğü; açık sıralı
    /// gruplar sırasıyla, sırası verilmeyenler onların arkasında katalog sırasıyla gelir. Satır yoksa sıra olduğu gibi döner.
    /// Gruptaki uygulamaların sırası ve bir uygulamanın hangi grupta olduğu değişmez (<see cref="OrderApps"/>, katalog).
    /// </summary>
    public static IReadOnlyList<AppGroup> OrderGroups(IReadOnlyList<AppGroup> groups, IReadOnlyList<RoleMenuRowDto>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return groups;
        }

        int? Explicit(AppGroup group) => rows
            .Where(r => r.ItemKey == GroupKey(group.Id) && r.Order is not null)
            .Select(r => r.Order)
            .Min();

        return [.. groups.Select((group, position) => (group, position))
            .OrderBy(x => Explicit(x.group) ?? int.MaxValue).ThenBy(x => x.position).Select(x => x.group)];
    }

    /// <summary>
    /// Rolün uygulama sırasını uygular (E8 dilim 4): uygulamalar kendi uygulama paneli grubu içinde yer değiştirir, grupların sırası ve
    /// her uygulamanın grubu sabittir. Kural öğeyle aynıdır: sıra veren satırların en küçüğü; bir grupta en az bir uygulamaya açık sıra
    /// verilmişse sırası verilmeyen uygulamalar açık sıralıların arkasına gelen sıradaki (katalog) sırasıyla dizilir. Satır yoksa sıra olduğu gibi döner.
    /// </summary>
    public static IReadOnlyList<AppDefinition> OrderApps(IReadOnlyList<AppDefinition> roots, IReadOnlyList<RoleMenuRowDto>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return roots;
        }

        int? Explicit(AppDefinition app) => rows
            .Where(r => r.ItemKey == AppKey(app.Id) && r.Order is not null)
            .Select(r => r.Order)
            .Min();

        // Grup içinde: açık sıralılar sırasıyla, sonra sırasızlar gelen sırada; grubun listedeki yerleri korunur.
        List<AppDefinition> result = [.. roots];
        foreach (IGrouping<string, int> slots in Enumerable.Range(0, roots.Count).GroupBy(i => roots[i].Group ?? string.Empty))
        {
            List<int> indexes = [.. slots];
            List<AppDefinition> ordered = [.. indexes.Select(i => roots[i]).Select((app, position) => (app, position))
                .OrderBy(x => Explicit(x.app) ?? int.MaxValue).ThenBy(x => x.position).Select(x => x.app)];
            for (int k = 0; k < indexes.Count; k++)
            {
                result[indexes[k]] = ordered[k];
            }
        }

        return result;
    }

    /// <summary>Rolün bölüm sırasını uygular; sıra veren satır yoksa bölümler olduğu gibi döner; sırası verilmeyen bölüm açık sıralıların arkasına gider. Yalnızca <paramref name="rows"/>'taki bölüm satırlarına bakar.</summary>
    public static IReadOnlyList<MenuSection> OrderSections(string rootId, IReadOnlyList<MenuSection> sections, IReadOnlyList<RoleMenuRowDto>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return sections;
        }

        static bool Movable(MenuSection s) => s.Group is { Footer: false };

        // Bölümün açık sırası: sıra veren satırların en küçüğü; hiçbir satırda yoksa null.
        int? Explicit(MenuSection s) => rows
            .Where(r => r.ItemKey == SectionKey(rootId, s.Group!.Id) && r.Order is not null)
            .Select(r => r.Order)
            .Min();

        // Açık sıralılar sırasıyla, sırası verilmeyenler arkada katalog sırasıyla (öğe ve uygulamalardaki kuralla aynı): katalog numarası
        // ile rolün yazdığı numaralar (10, 20, …) aynı ölçekte olmadığından karıştırılmaz. OrderBy kararlıdır, eşitlikte katalog sırası kalır.
        List<MenuSection> ordered = [.. sections.Where(Movable).OrderBy(s => Explicit(s) ?? int.MaxValue)];
        if (ordered.SequenceEqual(sections.Where(Movable)))
        {
            return sections;
        }

        // Taşınabilir bölümlerin yerleri korunur; alt sabitli ve başlıksız bölümler eski yerinde kalır.
        Queue<MenuSection> queue = new(ordered);
        return [.. sections.Select(s => Movable(s) ? queue.Dequeue() : s)];
    }
}
