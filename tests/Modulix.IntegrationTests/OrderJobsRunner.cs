using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Jobs;
using Modulix.Modules.Orders.Lifecycle;

namespace Modulix.IntegrationTests;

/// <summary>
/// Sipariş zamanlanmış işini testten çağırır: zamanlayıcı testlerde kapalıdır (<c>Jobs:WorkerEnabled</c>), iş tek tenant için
/// <see cref="ModuleJobRunner"/> ile doğrudan çalıştırılır (ADR-0046).
/// </summary>
internal sealed class OrderJobsRunner(IServiceProvider services)
{
    public const string JobKey = "orders.jobs";

    /// <summary>Tur özeti; <c>Skipped</c>: aynı tenant için tur başka yerde çalıştığından atlandı. <c>Run</c> boşsa tur çalışmadı.</summary>
    public async Task<(OrderSchedulerRun? Run, bool Skipped)> RunTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<ModuleJobRunner>().RunTenantAsync(JobKey, tenantId, cancellationToken);
        return (run.Result?.Details as OrderSchedulerRun, run.Status == ModuleJobRunStatus.Locked);
    }
}
