using System.Text.RegularExpressions;

namespace Modulix.Infrastructure.Files;

/// <summary>
/// Depolama anahtarının biçimi ve tenant'a göre konumu; bütün adaptörler aynı düzeni kullanır: <c>{tenantId:N}/{anahtar[0..2]}/{anahtar}</c>.
/// Anahtar rastgele ve biçimi sabittir (64 küçük onaltılık karakter); başka biçim reddedilir, böylece anahtardan yol çıkışı (<c>..</c>, ayırıcı) olamaz.
/// </summary>
internal static partial class BlobKeys
{
    public static bool IsValid(string key) => KeyPattern().IsMatch(key);

    public static string TenantSegment(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant kimliği boş olamaz.", nameof(tenantId));
        }

        return tenantId.ToString("N");
    }

    /// <summary>Tenant kökünün altındaki göreli konum (<c>{anahtar[0..2]}/{anahtar}</c>, ayırıcısız iki parça).</summary>
    public static (string Shard, string Key) Locate(string key)
    {
        if (!IsValid(key))
        {
            throw new ArgumentException("Geçersiz depolama anahtarı.", nameof(key));
        }

        return (key[..2], key);
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex KeyPattern();
}
