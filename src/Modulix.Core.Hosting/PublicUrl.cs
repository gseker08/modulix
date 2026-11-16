using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Modulix.Core.Hosting;

/// <summary>
/// E-postadaki bağlantıların kökü: <c>App:PublicUrl</c> (ör. https://app.modulix.com.tr). Geliştirmede boşsa isteğin
/// adresi kullanılır. Üretimde zorunludur: isteğin Host başlığına güvenilirse saldırgan, kendi adresine giden şifre
/// sıfırlama bağlantıları ürettirebilir. Modül uç noktaları da (ör. teklif paylaşım bağlantısı) kullansın diye <c>Modulix.Api/Users</c>'tan
/// buraya alındı (ADR-0046).
/// </summary>
public static class PublicUrl
{
    public const string ConfigurationKey = "App:PublicUrl";

    public static string Resolve(HttpContext http)
    {
        var configured = http.RequestServices.GetRequiredService<IConfiguration>()[ConfigurationKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.TrimEnd('/');
        }

        // Host başlığı istemciden gelir: yalnızca geliştirmede ve yerel adreste kullanılır. Aksi halde saldırgan
        // "şifremi unuttum" isteğine kendi alan adını yazıp gerçek sıfırlama bağlantısını kendine yönlendirebilir.
        if (http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() && IsLoopbackHost(http.Request.Host.Host))
        {
            return $"{http.Request.Scheme}://{http.Request.Host}";
        }

        throw new InvalidOperationException($"{ConfigurationKey} ayarı gerekli: e-postadaki bağlantılar bu adresle oluşturulur.");
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || (System.Net.IPAddress.TryParse(host, out System.Net.IPAddress? address) && System.Net.IPAddress.IsLoopback(address));
}
