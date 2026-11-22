using Modulix.Shared.Access;

namespace Modulix.Web.Services;

/// <summary>
/// Rol bazlı menü düzenini (E8 dilim 2) sol menü bağlantılarına uygular. Saf bir kuraldır: oturum ve katalog bilmez.
/// Gizlemek yetki değildir; bu kural yalnızca modül/izin/lisans süzmesinden geçmiş bağlantıları yeniden sıralar ya da
/// gizler, hiçbir zaman yeni bağlantı eklemez. Öğe anahtarı <see cref="AppDefinition.Id"/>'dir; katalogda olmayan
/// anahtarlar kendiliğinden yok sayılır. "Footer" bölümlerdeki (CRM Ayarları) bağlantılar düzenle değişmez.
/// Birleştirme: düzeni tanımlı rol = kullanıcının rolleri arasında en az bir satırı olan rol. Öğe, düzeni tanımlı
/// rollerinden herhangi birinde gizli değilse görünür (rolün düzeninde satırı olmayan öğe o rol için görünürdür);
/// sıra, o öğeye sıra veren rollerin en küçüğüdür, hiçbiri sıra vermediyse katalog sırası. Düzeni tanımlı rol yoksa
/// (yönetici de dahil, yöneticide düzen boş gelir) katalog düzeni aynen korunur.
/// </summary>
public static class MenuLayoutRules
{
    public static IReadOnlyList<MenuEntry> Apply(
        IReadOnlyList<MenuEntry> entries,
        IReadOnlyCollection<Guid> roleIds,
        IReadOnlyList<RoleMenuRowDto> layout,
        IReadOnlyCollection<string> footerGroupIds)
    {
        // Yalnızca kullanıcının kendi rollerinin satırları sayılır.
        List<RoleMenuRowDto> rows = layout.Where(r => roleIds.Contains(r.RoleId)).ToList();
        if (rows.Count == 0)
        {
            return entries;
        }

        HashSet<Guid> layoutRoles = rows.Select(r => r.RoleId).ToHashSet();
        List<MenuEntry> result = [];
        foreach (MenuEntry entry in entries)
        {
            if (entry.GroupId is not null && footerGroupIds.Contains(entry.GroupId))
            {
                result.Add(entry);
                continue;
            }

            List<RoleMenuRowDto> own = rows.Where(r => r.ItemKey == entry.App.Id).ToList();

            // Düzeni tanımlı rollerden biri öğeyi gizlemiyorsa (satırı yoksa da) öğe görünür.
            bool visible = layoutRoles.Any(role => !own.Any(r => r.RoleId == role && r.Hidden));
            if (!visible)
            {
                continue;
            }

            int[] orders = own.Where(r => !r.Hidden && r.Order is not null).Select(r => r.Order!.Value).ToArray();
            result.Add(orders.Length > 0 ? entry with { Order = orders.Min() } : entry);
        }

        return result;
    }
}
