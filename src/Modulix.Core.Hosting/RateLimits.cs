using Microsoft.Extensions.Configuration;

namespace Modulix.Core.Hosting;

/// <summary>
/// Uygulama içi hız sınırlarının ortak hesabı (ADR-0054). Sayaçlar her sunucunun belleğindedir; küme genelindeki ortak sınır ters vekilde
/// uygulanır (docs/tenant-isletim.md). Yapılandırmadaki dakikalık değerler <b>küme toplamıdır</b>: bu sınıf onları
/// <c>RateLimiting:Nodes</c> (sunucu sayısı, varsayılan 1) ile böler, yukarı yuvarlar. Böylece N sunucunun toplamı ayarı aşmaz; tek sunucuda
/// değer olduğu gibi kalır. Her hız sınırı ilkesi izin sayısını buradan alır (<c>RateLimitRulesTests</c> denetler).
/// </summary>
public static class RateLimits
{
    /// <summary>Uygulamayı çalıştıran sunucu (örnek) sayısı. Ölçekleme değişince güncellenir.</summary>
    public const string NodesKey = "RateLimiting:Nodes";

    /// <summary>
    /// <paramref name="key"/> ayarındaki (yoksa <paramref name="defaultPerMinute"/>) küme toplamının bu sunucuya düşen payı; en az
    /// <paramref name="minimum"/>. <paramref name="minimum"/> 0 verilirse 0 ya da negatif ayar 0 döner (çağıran sınırı kapatır).
    /// </summary>
    public static int PerNode(IConfiguration configuration, string key, int defaultPerMinute, int minimum = 1)
    {
        int total = configuration.GetValue(key, defaultPerMinute);
        int nodes = Math.Max(1, configuration.GetValue(NodesKey, 1));
        return Math.Max(minimum, (int)Math.Ceiling(total / (double)nodes));
    }
}
