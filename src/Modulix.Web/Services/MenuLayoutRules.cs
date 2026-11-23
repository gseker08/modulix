using Modulix.Shared.Access;

namespace Modulix.Web.Services;

/// <summary>
/// Rol bazlı menü düzeninin (E8 dilim 2) kullanıcının menüsüne uygulanması. Yalnızca görünümdür: gizlemek yetki değildir; sayfa
/// erişimini AuthGate ve sunucu izinleri denetlemeye devam eder.
///
/// Kural: yalnızca düzeni tanımlı (en az bir satırı olan) roller sayılır; hiçbiri yoksa katalog düzeni geçerlidir. Bir öğe,
/// düzeni tanımlı rollerinden <b>herhangi birinde</b> görünüyorsa (o rolde gizli satırı yoksa) görünür. Sıra, öğeye sıra veren
/// ve öğeyi gösteren rollerin en küçüğüdür; hiçbiri sıra vermezse katalog sırası kalır. Sıra bölüm içinde uygulanır, öğe
/// bölümünü değiştirmez. Katalogda olmayan anahtarlı satır yok sayılır.
///
/// Bölüm sırası (E8 dilim 3) aynı tabloda <see cref="SectionKey"/> anahtarlı satırla saklanır (şema değişmez): <c>Order</c> bölümün
/// uygulamanın menüsündeki sırasıdır, <c>Hidden</c> kullanılmaz. Kural öğeyle aynıdır: sıra veren rollerin en küçüğü; hiçbiri vermezse
/// katalog sırası kalır. Yalnızca başlıklı, alt sabitli olmayan bölümler yer değiştirir; alt sabitli (<c>Footer</c>) ve başlıksız
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
        List<MenuEntry> result = [];
        foreach (MenuEntry entry in entries)
        {
            Dictionary<Guid, RoleMenuRowDto> mine = rows.Where(r => r.ItemKey == entry.App.Id).ToDictionary(r => r.RoleId);

            // Rolde satır yoksa öğe o rolde görünür (katalog varsayılanı); yalnızca açıkça gizlenmişse görünmez.
            List<Guid> showing = layoutRoles.Where(role => !mine.TryGetValue(role, out RoleMenuRowDto? row) || !row.Hidden).ToList();
            if (showing.Count == 0)
            {
                continue;
            }

            int? order = showing.Where(mine.ContainsKey).Select(role => mine[role].Order).Where(o => o is not null).Min();
            result.Add(order is { } explicitOrder ? entry with { Order = explicitOrder } : entry);
        }

        return result;
    }

    /// <summary>Bir uygulamanın menüsündeki bölümün düzen anahtarı (ör. <c>section.crm.crm.customers</c>); uygulama kimliği ve bölüm kimliği küçük harf, rakam, nokta ve tiredir.</summary>
    public static string SectionKey(string rootId, string groupId) => $"section.{rootId}.{groupId}";

    /// <summary>Rolün bölüm sırasını uygular; sıra veren satır yoksa bölümler olduğu gibi döner. Yalnızca <paramref name="rows"/>'taki bölüm satırlarına bakar.</summary>
    public static IReadOnlyList<MenuSection> OrderSections(string rootId, IReadOnlyList<MenuSection> sections, IReadOnlyList<RoleMenuRowDto>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return sections;
        }

        static bool Movable(MenuSection s) => s.Group is { Footer: false };

        // Bölümün sırası: sıra veren satırların en küçüğü; yoksa katalog sırası.
        int Order(MenuSection s) => rows
            .Where(r => r.ItemKey == SectionKey(rootId, s.Group!.Id) && r.Order is not null)
            .Select(r => r.Order!.Value)
            .DefaultIfEmpty(s.Group!.Order)
            .Min();

        List<MenuSection> ordered = [.. sections.Where(Movable).OrderBy(Order)];
        if (ordered.SequenceEqual(sections.Where(Movable)))
        {
            return sections;
        }

        // Taşınabilir bölümlerin yerleri korunur; alt sabitli ve başlıksız bölümler eski yerinde kalır.
        Queue<MenuSection> queue = new(ordered);
        return [.. sections.Select(s => Movable(s) ? queue.Dequeue() : s)];
    }
}
