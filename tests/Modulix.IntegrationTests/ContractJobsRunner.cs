using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Jobs;
using Modulix.Modules.Contracts.Lifecycle;

namespace Modulix.IntegrationTests;

/// <summary>
/// Sözleşme zamanlanmış işini testten çağırır: zamanlayıcı testlerde kapalıdır (<c>Jobs:WorkerEnabled</c>), iş tek tenant için
/// <see cref="ModuleJobRunner"/> ile doğrudan çalıştırılır (ADR-0046).
/// </summary>
internal sealed class ContractJobsRunner(IServiceProvider services)
{
    public const string JobKey = "contracts.jobs";

    /// <summary>Tur özeti; <c>Skipped</c>: aynı tenant için tur başka yerde çalıştığından atlandı. <c>Run</c> boşsa tur çalışmadı.</summary>
    public async Task<(ContractSchedulerRun? Run, bool Skipped)> RunTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<ModuleJobRunner>().RunTenantAsync(JobKey, tenantId, cancellationToken);
        return (run.Result?.Details as ContractSchedulerRun, run.Status == ModuleJobRunStatus.Locked);
    }
}
