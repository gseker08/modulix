using System.Text.RegularExpressions;
using Modulix.Core.Contracts.Tenancy;

namespace Modulix.Core.Authorization;

/// <summary>
/// Rolün menü düzeninde bir satır (E8 dilim 2): menü öğesinin (arayüzdeki <c>AppCatalog</c> kimliği, ör. "crm.companies") rol için
/// sırası ve gizli olup olmadığı. Yalnızca görünümdür: gizlemek yetki değildir, uç noktalar izinle korunmaya devam eder.
/// Anahtar, katalogda olup olmadığına bakılmadan yalnızca biçim olarak doğrulanır (katalog arayüzdedir); katalogda olmayan satırı
/// arayüz yok sayar. Tenant verisidir; Row-Level Security ile korunur ve rolle aynı tenant'tadır (bileşik yabancı anahtar);
/// rol silinince satırlar da silinir.
/// </summary>
public sealed partial class RoleMenuItem : ITenantEntity, ICompanyShared
{
    public const int KeyMaxLength = 100;

    public const int MaxOrder = 10_000;

    private RoleMenuItem()
    {
        // EF Core için
    }

    public RoleMenuItem(Guid roleId, string itemKey, int? order, bool hidden)
    {
        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("RoleId boş olamaz.", nameof(roleId));
        }

        if (!IsValidKey(itemKey))
        {
            throw new ArgumentException($"Geçersiz menü öğesi anahtarı: '{itemKey}'.", nameof(itemKey));
        }

        if (order is < 0 or > MaxOrder)
        {
            throw new ArgumentException($"Sıra 0 ile {MaxOrder} arasında olmalıdır.", nameof(order));
        }

        Id = Guid.CreateVersion7();
        RoleId = roleId;
        ItemKey = itemKey;
        SortOrder = order;
        Hidden = hidden;
    }

    public Guid Id { get; private set; }

    /// <summary>Kaydederken aktif tenant'tan atanır; uygulama kodu yazmaz.</summary>
    public Guid TenantId { get; private set; }

    public Guid RoleId { get; private set; }

    public string ItemKey { get; private set; } = string.Empty;

    /// <summary>Rolün menüsündeki sıra; <c>null</c> ise katalogdaki sıra geçerlidir.</summary>
    public int? SortOrder { get; private set; }

    public bool Hidden { get; private set; }

    /// <summary>Küçük harf, rakam, nokta ve tire; ayraç başta, sonda ya da art arda olamaz.</summary>
    public static bool IsValidKey(string? key) =>
        key is { Length: > 0 and <= KeyMaxLength } && KeyPattern().IsMatch(key);

    [GeneratedRegex("^[a-z0-9]+([.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
