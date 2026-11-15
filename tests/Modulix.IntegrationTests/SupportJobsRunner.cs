using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Jobs;
using Modulix.Modules.Support.Lifecycle;

namespace Modulix.IntegrationTests;

/// <summary>
/// Destek (SLA) zamanlanmış işini testten çağırır: zamanlayıcı testlerde kapalıdır (<c>Jobs:WorkerEnabled</c>), iş tek tenant için
/// <see cref="ModuleJobRunner"/> ile doğrudan çalıştırılır (ADR-0046).
/// </summary>
internal sealed class SupportJobsRunner(IServiceProvider services)
{
    public const string JobKey = "support.jobs";

    /// <summary>Tur özeti; <c>Skipped</c>: aynı tenant için tur başka yerde çalıştığından atlandı. <c>Run</c> boşsa tur çalışmadı.</summary>
    public async Task<(SlaSchedulerRun? Run, bool Skipped)> RunTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<ModuleJobRunner>().RunTenantAsync(JobKey, tenantId, cancellationToken);
        return (run.Result?.Details as SlaSchedulerRun, run.Status == ModuleJobRunStatus.Locked);
    }
}
