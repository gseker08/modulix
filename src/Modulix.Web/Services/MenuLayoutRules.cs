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
}
