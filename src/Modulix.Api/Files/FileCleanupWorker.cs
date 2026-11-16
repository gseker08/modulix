using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulix.Core.Contracts.Diagnostics;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Files;
using Modulix.Core.Platform;
using Modulix.Infrastructure.Persistence;
using Modulix.Infrastructure.Platform;

namespace Modulix.Api.Files;

/// <summary>"FileCleanup" yapılandırma bölümü (ADR-0040 dilim 3).</summary>
public sealed class FileCleanupOptions
{
    public const string SectionName = "FileCleanup";

    /// <summary>Arka plan işi çalışsın mı? Entegrasyon testleri kapatır ve <see cref="FileCleanupRunner"/>'ı kendisi çağırır.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>Turlar arasındaki süre (dakika); varsayılan 6 saat.</summary>
    public int IntervalMinutes { get; set; } = 360;

    /// <summary>
    /// Üst verisi olmayan içerik bu kadar saatten eskiyse silinir; varsayılan 24. Yükleme önce içeriği, sonra üst veriyi yazar; bekleme süresi süren
    /// yüklemeyi korur. En az 1 saattir.
    /// </summary>
    public int OrphanGraceHours { get; set; } = 24;
}

/// <summary>
/// Bir tenant için yetim içerik temizliğini çalıştırır (<see cref="IOrphanBlobSweeper"/>). İş kendi tenant kapsamında çalışır (RLS oturum değeri). Birden
/// fazla uygulama örneği aynı anda çalışabilir: tur, tenant başına oturum kilidiyle (<c>sp_getapplock</c>) tek örnekte koşar; kilidi alamayan atlar.
/// </summary>
public sealed class FileCleanupRunner(IServiceScopeFactory scopes)
{
    public const string LockName = "modulix.files.cleanup";

    public static string LockResource(Guid tenantId) => $"{LockName}:{tenantId:N}";

    /// <returns>Silinen yetim içerik sayısı; kilit başka örnekteyse null.</returns>
    public async Task<int?> RunTenantAsync(Guid tenantId, TimeSpan grace, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tenantScopes = scope.ServiceProvider.GetRequiredService<ITenantScopeFactory>();
        await using var tenantScope = tenantScopes.Create(new TenantContext(tenantId, Guid.Empty, Guid.Empty));
        var services = tenantScope.ServiceProvider;

        await using var lockConnection = new SqlConnection(services.GetRequiredService<ModulixDatabaseOptions>().AppConnectionString);
        await lockConnection.OpenAsync(cancellationToken);
        if (!await TryLockAsync(lockConnection, tenantId, cancellationToken))
        {
            return null;
        }

        try
        {
            return await services.GetRequiredService<IOrphanBlobSweeper>().SweepAsync(grace, cancellationToken);
        }
        finally
        {
            await ReleaseLockAsync(lockConnection, tenantId, cancellationToken);
        }
    }

    private static async Task<bool> TryLockAsync(SqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource = @name, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0; SELECT @r;";
        command.Parameters.AddWithValue("@name", LockResource(tenantId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) >= 0;
    }

    private static async Task ReleaseLockAsync(SqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXEC sys.sp_releaseapplock @Resource = @name, @LockOwner = N'Session';";
            command.Parameters.AddWithValue("@name", LockResource(tenantId));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException)
        {
            // Bağlantı kapanınca oturum kilidi zaten düşer.
        }
    }
}

/// <summary>
/// Yetim içerik temizliğini düzenli aralıklarla çalıştırır (<c>FileCleanup:WorkerEnabled</c> ile kapatılır). Erişimi açık her tenant ayrı çalışır; biri
/// başarısız olursa diğerleri etkilenmez ve bir sonraki turda yeniden denenir. Silinen kaydın üst verisini olay abonesi siler (<see cref="FileCleanupSubscriber"/>).
/// </summary>
public sealed class FileCleanupWorker(
    IServiceScopeFactory scopes, FileCleanupRunner runner, IOptions<FileCleanupOptions> options, IJobMetrics metrics, ILogger<FileCleanupWorker> logger) : BackgroundService
{
    public const string JobName = "files.cleanup";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.WorkerEnabled)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(5, settings.IntervalMinutes));
        var grace = TimeSpan.FromHours(Math.Max(1, settings.OrphanGraceHours));

        // Açılışta bir an beklenir: uygulama ısınırken veritabanına ve depoya ek yük binmesin.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
                var tenantIds = await db.Tenants.AsNoTracking().Where(t => TenantStatusRules.Operational.Contains(t.Status)).Select(t => t.Id).ToListAsync(stoppingToken);
                foreach (var tenantId in tenantIds)
                {
                    try
                    {
                        if (await runner.RunTenantAsync(tenantId, grace, stoppingToken) is > 0 and var removed)
                        {
                            logger.LogInformation("Tenant {TenantId} yetim içerik temizliği: {Removed} içerik silindi.", tenantId, removed);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        metrics.TenantFailed(JobName);
                        logger.LogWarning(exception, "Tenant {TenantId} yetim içerik temizliği bu turda çalıştırılamadı.", tenantId);
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
                logger.LogError(exception, "Yetim içerik temizliği çalıştırılamadı.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
