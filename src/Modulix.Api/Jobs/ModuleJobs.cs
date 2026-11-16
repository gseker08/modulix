using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Diagnostics;
using Modulix.Core.Contracts.Events;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Events;
using Modulix.Core.Modules;
using Modulix.Core.Platform;
using Modulix.Infrastructure.Persistence;
using Modulix.Infrastructure.Platform;

namespace Modulix.Api.Jobs;

/// <summary>
/// Bir zamanlanmış işin çalışma ayarı (ADR-0046). Tek kalıp: <c>Jobs:&lt;iş anahtarı&gt;:Enabled</c> ve <c>Jobs:&lt;iş anahtarı&gt;:IntervalSeconds</c>;
/// <c>Jobs:WorkerEnabled</c> zamanlayıcının tamamını kapatır. Yeni ayar verilmemişse işin eski bölümü
/// (<see cref="ModuleJobDefinition.LegacySection"/>: <c>WorkerEnabled</c>, <c>PollIntervalSeconds</c>) okunur. Kuyruk işinde
/// (<see cref="ModuleJobDefinition.BusyInterval"/>) <see cref="BusyInterval"/> da çözülür: <c>Jobs:&lt;iş anahtarı&gt;:BusyIntervalSeconds</c>
/// (eski bölümde <c>BusyIntervalSeconds</c>).
/// </summary>
public sealed record ModuleJobSchedule(bool Enabled, TimeSpan Interval, TimeSpan? BusyInterval = null)
{
    public const string SectionName = "Jobs";

    /// <summary>Zamanlayıcının tamamını açıp kapatan ayar. Entegrasyon testleri kapatır ve işi <see cref="ModuleJobRunner"/> ile kendisi çağırır.</summary>
    public const string WorkerEnabledKey = SectionName + ":WorkerEnabled";

    /// <summary>İşler bundan sık çalışmaz (yanlış yapılandırma veritabanını yormasın).</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);

    /// <summary>Kuyruk işinin boşta aralığı bundan kısa olmaz (eski gönderim işçisinin alt sınırı).</summary>
    public static readonly TimeSpan QueueMinInterval = TimeSpan.FromSeconds(5);

    /// <summary>Kuyruk işinin yoğun aralığı bundan kısa olmaz.</summary>
    public static readonly TimeSpan BusyMinInterval = TimeSpan.FromSeconds(1);

    public static bool WorkerEnabled(IConfiguration configuration) => configuration.GetValue<bool?>(WorkerEnabledKey) ?? true;

    public static ModuleJobSchedule Resolve(IConfiguration configuration, ModuleJobDefinition job)
    {
        var section = configuration.GetSection($"{SectionName}:{job.Key}");
        var legacy = job.LegacySection is { Length: > 0 } name ? configuration.GetSection(name) : null;
        bool enabled = section.GetValue<bool?>("Enabled") ?? legacy?.GetValue<bool?>("WorkerEnabled") ?? true;
        int? seconds = section.GetValue<int?>("IntervalSeconds") ?? legacy?.GetValue<int?>("PollIntervalSeconds");
        var interval = seconds is { } value ? TimeSpan.FromSeconds(value) : job.DefaultInterval;
        if (job.BusyInterval is not { } defaultBusy)
        {
            return new ModuleJobSchedule(enabled, interval < MinInterval ? MinInterval : interval);
        }

        int? busySeconds = section.GetValue<int?>("BusyIntervalSeconds") ?? legacy?.GetValue<int?>("BusyIntervalSeconds");
        var busy = busySeconds is { } b ? TimeSpan.FromSeconds(b) : defaultBusy;
        return new ModuleJobSchedule(enabled, interval < QueueMinInterval ? QueueMinInterval : interval, busy < BusyMinInterval ? BusyMinInterval : busy);
    }

    /// <summary>
    /// İşe verilen düz ayarlar (<see cref="ModuleJobContext.Settings"/>): eski bölümün değerleri, üzerine <c>Jobs:&lt;iş anahtarı&gt;</c> değerleri
    /// (aynı adda yeni bölüm kazanır). Yalnızca doğrudan alt değerler alınır.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Settings(IConfiguration configuration, ModuleJobDefinition job)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (job.LegacySection is { Length: > 0 } name)
        {
            foreach (var child in configuration.GetSection(name).GetChildren().Where(c => c.Value is not null))
            {
                settings[child.Key] = child.Value;
            }
        }

        foreach (var child in configuration.GetSection($"{SectionName}:{job.Key}").GetChildren().Where(c => c.Value is not null))
        {
            settings[child.Key] = child.Value;
        }

        return settings;
    }
}

public enum ModuleJobRunStatus
{
    /// <summary>İş tenant için çalıştı ve değişiklikleri kaydedildi.</summary>
    Completed = 1,

    /// <summary>Modül tenant için kapalı ya da (yazan işte) salt okunur; iş çalışmadı.</summary>
    ModuleNotAllowed = 2,

    /// <summary>Aynı iş aynı tenant için başka bir yerde çalışıyor; tur atlandı.</summary>
    Locked = 3,
}

/// <summary>Bir tenant turunun sonucu; <see cref="Result"/> yalnızca <see cref="ModuleJobRunStatus.Completed"/> durumunda doludur.</summary>
public sealed record ModuleJobRun(ModuleJobRunStatus Status, ModuleJobResult? Result = null);

/// <summary>
/// Bir modül işini tek tenant için çalıştırır (ADR-0046). Her işin tenant başına çalıştırılması buradan geçer: kapsam açılır, tenant bağlamı
/// kurulur, modül lisansı denetlenir (ADR-0028), tenant başına oturum kilidi (<c>sp_getapplock</c>) alınır, iş kendi sistem kimliğiyle
/// (<see cref="ModuleJobDefinition.Identity"/>) çalıştırılır ve değişiklikler kaydedilir. Kilidi alamayan çağrı turu atlar; aynı turun iki kez
/// çalışması işin sorumluluğunda güvenlidir.
/// </summary>
public sealed class ModuleJobRunner(IServiceScopeFactory scopes, ModuleCatalog catalog, TimeProvider time, IConfiguration configuration)
{
    /// <summary>Kilit adı işin anahtarından türer (ör. "modulix.orders.jobs:&lt;tenant&gt;").</summary>
    public static string LockResource(string jobKey, Guid tenantId) => $"modulix.{jobKey}:{tenantId:N}";

    public async Task<ModuleJobRun> RunTenantAsync(string jobKey, Guid tenantId, CancellationToken cancellationToken)
    {
        var (moduleKey, job) = catalog.FindJob(jobKey) ?? throw new InvalidOperationException($"Zamanlanmış iş tanımlı değil: '{jobKey}'.");
        var identity = catalog.FindSubscription(job.Identity)?.Subscription
            ?? throw new InvalidOperationException($"'{jobKey}' işinin sistem kimliği '{job.Identity}' katalogda yok.");

        await using var scope = scopes.CreateAsyncScope();
        var tenantScopes = scope.ServiceProvider.GetRequiredService<ITenantScopeFactory>();
        await using var tenantScope = tenantScopes.Create(new TenantContext(tenantId, Guid.Empty, Guid.Empty));
        var services = tenantScope.ServiceProvider;
        if (!await services.GetRequiredService<IModuleLicense>().AllowsAsync(tenantId, moduleKey, job.RequiresWritableModule, cancellationToken))
        {
            return new ModuleJobRun(ModuleJobRunStatus.ModuleNotAllowed);
        }

        string lockResource = LockResource(jobKey, tenantId);
        await using var lockConnection = new SqlConnection(services.GetRequiredService<ModulixDatabaseOptions>().AppConnectionString);
        await lockConnection.OpenAsync(cancellationToken);
        if (!await TryLockAsync(lockConnection, lockResource, cancellationToken))
        {
            return new ModuleJobRun(ModuleJobRunStatus.Locked);
        }

        try
        {
            // Oturumsuz çalışan iş, sistem kimliğinin aboneliğiyle "bir olayı işliyormuş" gibi kurulur: izinleri o aboneliğin bildirdikleridir.
            var cause = new EventEnvelope(
                Guid.NewGuid(), tenantId, identity.EventTypes[0], 1, identity.EntityKeys.Count > 0 ? identity.EntityKeys[0] : string.Empty, Guid.Empty, null, Guid.Empty,
                ExecutionSource.Import, null, null, 0, new JsonObject(), time.GetUtcNow());
            services.GetRequiredService<EventHandlingScope>().Begin(cause, job.Identity);
            var runner = (IModuleJob)services.GetRequiredService(job.Runner);
            var context = new ModuleJobContext(tenantId) { Settings = ModuleJobSchedule.Settings(configuration, job) };
            var result = await runner.RunAsync(context, cancellationToken);
            await services.GetRequiredService<ModulixDbContext>().SaveChangesAsync(cancellationToken);
            return new ModuleJobRun(ModuleJobRunStatus.Completed, result);
        }
        finally
        {
            await ReleaseLockAsync(lockConnection, lockResource, cancellationToken);
        }
    }

    /// <summary>İşin çalışacağı tenant'lar: erişime açık (ADR-0028) ve modülü açık olanlar (tek platform sorgusu).</summary>
    public async Task<IReadOnlyList<Guid>> TenantIdsAsync(string moduleKey, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
        if (ModuleCatalog.IsAlwaysEnabled(moduleKey))
        {
            return await db.Tenants.AsNoTracking()
                .Where(tenant => TenantStatusRules.Operational.Contains(tenant.Status))
                .Select(tenant => tenant.Id)
                .ToListAsync(cancellationToken);
        }

        return await (
            from tenant in db.Tenants.AsNoTracking()
            join entitlement in db.ModuleEntitlements.AsNoTracking() on tenant.Id equals entitlement.TenantId
            where TenantStatusRules.Operational.Contains(tenant.Status) && entitlement.ModuleKey == moduleKey && entitlement.IsEnabled
            select tenant.Id)
            .ToListAsync(cancellationToken);
    }

    private static async Task<bool> TryLockAsync(SqlConnection connection, string resource, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource = @name, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0; SELECT @r;";
        command.Parameters.AddWithValue("@name", resource);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) >= 0;
    }

    private static async Task ReleaseLockAsync(SqlConnection connection, string resource, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXEC sys.sp_releaseapplock @Resource = @name, @LockOwner = N'Session';";
            command.Parameters.AddWithValue("@name", resource);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException)
        {
            // Bağlantı kapanınca oturum kilidi zaten düşer.
        }
    }
}

/// <summary>
/// Modüllerin zamanlanmış işlerini çalıştıran tek zamanlayıcı (ADR-0046). Host modülü tanımaz: işleri modül kaydından (<c>IModule.Jobs</c>)
/// okur. Her iş kendi aralığıyla ve diğerlerinden bağımsız döner (bir işin uzun sürmesi ya da hatası diğerini geciktirmez); bir turda
/// tenant'lar sırayla gezilir, biri başarısız olursa diğerleri etkilenmez ve bir sonraki turda yeniden denenir. Kuyruk işinde bir tenant
/// sırada iş kaldığını bildirdiyse (<see cref="ModuleJobResult.HasMoreWork"/>) bir sonraki tur yoğun aralıkla başlar.
/// </summary>
public sealed class ModuleJobsWorker(
    ModuleCatalog catalog, ModuleJobRunner runner, IConfiguration configuration, IJobMetrics metrics, ILogger<ModuleJobsWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ModuleJobSchedule.WorkerEnabled(configuration))
        {
            return Task.CompletedTask;
        }

        var loops = new List<Task>();
        foreach (var (moduleKey, job) in catalog.Jobs)
        {
            var schedule = ModuleJobSchedule.Resolve(configuration, job);
            if (schedule.Enabled)
            {
                loops.Add(Task.Run(() => RunJobAsync(moduleKey, job, schedule, stoppingToken), CancellationToken.None));
            }
        }

        return Task.WhenAll(loops);
    }

    private async Task RunJobAsync(string moduleKey, ModuleJobDefinition job, ModuleJobSchedule schedule, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool moreWork = false;
            try
            {
                foreach (var tenantId in await runner.TenantIdsAsync(moduleKey, stoppingToken))
                {
                    try
                    {
                        var run = await runner.RunTenantAsync(job.Key, tenantId, stoppingToken);
                        moreWork |= run.Result?.HasMoreWork == true;
                        if (run.Result is { Summary: { Length: > 0 } summary } result)
                        {
                            logger.Log(
                                result.IsWarning ? LogLevel.Warning : LogLevel.Information, "Tenant {TenantId}, {Job} işi: {Summary}", tenantId, job.Key, summary);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        metrics.TenantFailed(job.Key);
                        logger.LogWarning(exception, "Tenant {TenantId} için {Job} işi bu turda çalıştırılamadı.", tenantId, job.Key);
                    }
                }

                metrics.TurnCompleted(job.Key);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                metrics.TurnFailed(job.Key);
                logger.LogError(exception, "{Job} işi çalıştırılamadı.", job.Key);
            }

            try
            {
                await Task.Delay(moreWork && schedule.BusyInterval is { } busy ? busy : schedule.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
