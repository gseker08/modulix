namespace Modulix.Core.Authorization;

/// <summary>Rolün menü düzenindeki bir satır: öğe anahtarı, sırası (boşsa katalog sırası) ve gizli olup olmadığı.</summary>
public sealed record RoleMenuEntry(string ItemKey, int? Order, bool Hidden);

/// <summary>Bir rolün bir menü satırı; kullanıcının rollerinin düzeni birleştirilirken kullanılır.</summary>
public sealed record RoleMenuLayoutRow(Guid RoleId, string ItemKey, int? Order, bool Hidden);

/// <summary>
/// Rol başına menü düzeni (E8 dilim 2). Aktif tenant'ın rollerinde çalışır. Düzen yalnızca görünümdür; gizlemek yetki değildir.
/// Sistem rolünde (yönetici) düzen tutulmaz: yönetici menüyü her zaman tam görür, böylece düzen ekranına erişim gizlenerek kaybedilemez.
/// Düzen değişikliği rolün denetim kaydına yazılır.
/// </summary>
public interface IRoleMenuLayouts
{
    /// <summary>Rol bu tenant'ta yoksa <c>null</c>; düzeni tanımsızsa boş liste.</summary>
    Task<IReadOnlyList<RoleMenuEntry>?> GetAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Rolün düzenini verilen satırlarla değiştirir (boş liste düzeni kaldırır). Rol yoksa <c>null</c>. Geçersiz ya da tekrarlı anahtar,
    /// aralık dışı sıra için <see cref="ArgumentException"/>, sistem rolü için <see cref="RoleConflictException"/>.
    /// </summary>
    Task<IReadOnlyList<RoleMenuEntry>?> SaveAsync(Guid roleId, IReadOnlyCollection<RoleMenuEntry> entries, CancellationToken cancellationToken);

    /// <summary>Verilen rollerin düzenleri (kullanıcının kendi menüsü için).</summary>
    Task<IReadOnlyList<RoleMenuLayoutRow>> GetForRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);
}
