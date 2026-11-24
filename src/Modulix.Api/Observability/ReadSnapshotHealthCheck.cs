using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Api.Observability;

/// <summary>Veritabanında <c>READ_COMMITTED_SNAPSHOT</c> açık mı (ADR-0058).</summary>
public interface IReadSnapshotProbe
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);
}

/// <summary>Ayarı <c>sys.databases</c>'ten okur; uygulama kullanıcısı kendi veritabanının satırını görür.</summary>
public sealed class SqlReadSnapshotProbe(IServiceScopeFactory scopes) : IReadSnapshotProbe
{
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
        var values = await db.Database
            .SqlQueryRaw<int>("SELECT CAST(is_read_committed_snapshot_on AS int) AS [Value] FROM sys.databases WHERE name = DB_NAME()")
            .ToListAsync(cancellationToken);
        return values.Count == 1 && values[0] == 1;
    }
}

/// <summary>
/// /health: okuma anlık görüntüsü kapalıysa "degraded". Kapalıyken tablo taraması başka tenant'ın kilitli satırına takılıp
/// kilitlenme (SQL Server 1205) üretebilir ve istek 500 döner (ADR-0058). Canlı veritabanında ayarı açmak işletim adımı olduğundan
/// unutulması görünür olsun; trafik almayı engellemez (/health/ready etkilenmez). Yanıtta ayrıntı yoktur (uçlar oturumsuzdur).
/// </summary>
public sealed class ReadSnapshotHealthCheck(IReadSnapshotProbe probe, ILogger<ReadSnapshotHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await probe.IsEnabledAsync(cancellationToken))
            {
                return HealthCheckResult.Healthy();
            }

            logger.LogWarning("READ_COMMITTED_SNAPSHOT kapalı: eşzamanlı yazma altında okuma kilitlenmesi (1205) görülebilir. docs/tenant-isletim.md \"Okuma anlık görüntüsü\" adımını uygulayın (ADR-0058).");
            return HealthCheckResult.Degraded("Veritabanı okuma yalıtımı beklenen ayarda değil.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Okunamadı: sağlıksız saymayız (ayarın kendisi bilinmiyor); nedeni kayıtta kalır.
            logger.LogWarning(exception, "READ_COMMITTED_SNAPSHOT ayarı okunamadı.");
            return HealthCheckResult.Degraded("Veritabanı okuma yalıtımı denetlenemedi.");
        }
    }
}
