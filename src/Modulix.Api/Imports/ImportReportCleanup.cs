using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulix.Core.Contracts.Diagnostics;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Platform;
using Modulix.Infrastructure.Persistence;
using Modulix.Infrastructure.Platform;

namespace Modulix.Api.Imports;

/// <summary>"ImportReportCleanup" yapılandırma bölümü.</summary>
public sealed class ImportReportCleanupOptions
{
    public const string SectionName = "ImportReportCleanup";

    /// <summary>Arka plan işi çalışsın mı? Entegrasyon testleri kapatır ve <see cref="ImportReportCleanup"/>'ı kendisi çağırır.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>Turlar arasındaki süre (dakika); varsayılan 15. Rapor ömrü bir saat olduğundan rapor en geç bu kadar gecikmeyle silinir.</summary>
    public int IntervalMinutes { get; set; } = 15;
}

/// <summary>
/// Süresi dolan içe aktarma hata raporlarını dosya deposundan siler (<see cref="ImportErrorReports"/>). İş kendi tenant kapsamında
/// çalışır (RLS oturum değeri). Kilit gerekmez: silme ve işaret kaldırma tekrarlanabilir, iki örnek aynı raporu silse de sonuç aynıdır.
/// Kişisel veri temizliği olduğu için askıdaki ve kapalı firmalar da taranır; silinen firmanın raporları firma silmeyle gider.
/// </summary>
public sealed class ImportReportCleanup(IServiceScopeFactory scopes, ImportErrorReports reports, TimeProvider time)
{
    private const int BatchSize = 200;

    /// <returns>Silinen rapor sayısı.</returns>
    public async Task<int> RunTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScopeFactory>().Create(new TenantContext(tenantId, Guid.Empty, Guid.Empty));
        var db = tenantScope.ServiceProvider.GetRequiredService<ModulixDbContext>();

        var deleted = 0;
        while (true)
        {
            var now = time.GetUtcNow();
            var expired = await db.ImportRuns
                .Where(r => r.ErrorReportExpiresAt != null && r.ErrorReportExpiresAt <= now)
                .OrderBy(r => r.ErrorReportExpiresAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            foreach (var run in expired)
            {
                await reports.DeleteAsync(tenantId, run.Id, cancellationToken);
                run.ClearErrorReport();
            }

            await db.SaveChangesAsync(cancellationToken);
            deleted += expired.Count;
            if (expired.Count < BatchSize)
            {
                return deleted;
            }
        }
    }
}

/// <summary>
/// <see cref="ImportReportCleanup"/>'ı düzenli aralıklarla çalıştırır (<c>ImportReportCleanup:WorkerEnabled</c> ile kapatılır). Her tenant ayrı
/// çalışır; biri başarısız olursa diğerleri etkilenmez ve bir sonraki turda yeniden denenir.
/// </summary>
public sealed class ImportReportCleanupWorker(
    IServiceScopeFactory scopes, ImportReportCleanup cleanup, IOptions<ImportReportCleanupOptions> options, IJobMetrics metrics,
    ILogger<ImportReportCleanupWorker> logger) : BackgroundService
{
    public const string JobName = "imports.report-cleanup";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.WorkerEnabled)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                List<Guid> tenantIds;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    tenantIds = await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().Tenants.AsNoTracking()
                        .Select(t => t.Id).ToListAsync(stoppingToken);
                }

                foreach (var tenantId in tenantIds)
                {
                    try
                    {
                        if (await cleanup.RunTenantAsync(tenantId, stoppingToken) is > 0 and var deleted)
                        {
                            logger.LogInformation("Tenant {TenantId}: süresi dolan {Count} içe aktarma hata raporu silindi.", tenantId, deleted);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        metrics.TenantFailed(JobName);
                        logger.LogWarning(exception, "Tenant {TenantId} içe aktarma hata raporu temizliği bu turda çalıştırılamadı.", tenantId);
                    }
                }

                metrics.TurnCompleted(JobName);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                metrics.TurnFailed(JobName);
                logger.LogError(exception, "İçe aktarma hata raporu temizliği çalıştırılamadı.");
            }
        }
    }
}
