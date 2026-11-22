using Modulix.Shared.Access;

namespace Modulix.Web.Services;

/// <summary>
/// Rol menü düzeni kaydının arayüzden bağımsız kuralları (E8). <c>PUT /api/roles/{id}/menu</c> bütün düzeni değiştirir; ekranda
/// listelenmeyen bir öğenin kayıtlı satırı (ör. modülü şu an kapalı) istekte yer almazsa silinir ve modül yeniden açılınca rolün
/// o öğedeki düzeni kaybolur. Bu yüzden ekrandaki öğelerin dışında kalan kayıtlı satırlar olduğu gibi geri gönderilir.
/// </summary>
public static class RoleMenuSaveRules
{
    /// <summary>Kayıtlı düzenden, ekranda düzenlenen öğelerde olmayan satırlar (kayıt isteğine olduğu gibi eklenir).</summary>
    public static IReadOnlyList<RoleMenuItemDto> Preserved(IEnumerable<RoleMenuItemDto> saved, IEnumerable<string> editableKeys)
    {
        HashSet<string> editable = new(editableKeys, StringComparer.Ordinal);
        return [.. saved.Where(row => !editable.Contains(row.ItemKey))];
    }
}
