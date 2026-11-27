using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Modulix.Core.Currency;

namespace Modulix.Infrastructure.Currency;

/// <summary>
/// TCMB günlük kur bülteni (ADR-0049 dilim 5d). Dış ağa çıkan tek yer: <c>ExchangeRates:CentralBank:Enabled</c> açıksa
/// <c>ExchangeRates:CentralBank:Url</c> adresinden (varsayılan <c>https://www.tcmb.gov.tr/kurlar/today.xml</c>) bülteni okur. Varsayılan kapalıdır:
/// on-prem kurulumlar dış ağa çıkmak zorunda kalmaz. Bülten tek tenant için değil sunucu için okunur ve kısa süre saklanır (her tenant turu
/// sağlayıcıya gitmez). Ulaşılamama ya da bozuk bülten hatası fırlatmaz; <c>null</c> döner ve eski kur kalır.
/// </summary>
internal sealed class TcmbRateFeed(HttpClient client, IConfiguration configuration, TimeProvider time, ILogger<TcmbRateFeed> logger) : ICentralBankFeed
{
    public const string DefaultUrl = "https://www.tcmb.gov.tr/kurlar/today.xml";

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim gate = new(1, 1);
    private CentralBankSnapshot? cached;
    private DateTimeOffset cachedAt;

    public async Task<CentralBankSnapshot?> FetchAsync(CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("ExchangeRates:CentralBank");
        if (section.GetValue<bool?>("Enabled") != true)
        {
            return null;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null && time.GetUtcNow() - cachedAt < CacheFor)
            {
                return cached;
            }

            var xml = await client.GetStringAsync(section.GetValue<string>("Url") ?? DefaultUrl, cancellationToken);
            if (TcmbRateParser.Parse(xml) is { } snapshot)
            {
                cached = snapshot;
                cachedAt = time.GetUtcNow();
                return snapshot;
            }

            logger.LogWarning("TCMB kur bülteni okunamadı: geçerli satır yok.");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "TCMB kur bültenine ulaşılamadı; eski kur kalır.");
            return null;
        }
        finally
        {
            gate.Release();
        }
    }
}
