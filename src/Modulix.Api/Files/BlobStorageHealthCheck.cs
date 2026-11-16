using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modulix.Core.Files;

namespace Modulix.Api.Files;

/// <summary>
/// /health/ready: dosya deposu kullanılabilir mi (yerel kök oluşturulabilir, S3 kovası erişilebilir; mimari değerlendirme A4).
/// Yanlış kova adı ya da kimlik bilgisi ilk yüklemede değil, örnek trafiğe açılmadan görünür.
/// </summary>
public sealed class BlobStorageHealthCheck(IBlobStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.CheckAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Ayrıntı (kova adı, uç nokta) yanıtta değil kayıtta kalır; /health uçları oturumsuzdur.
            return HealthCheckResult.Unhealthy("Dosya deposuna erişilemiyor.", exception);
        }
    }
}
