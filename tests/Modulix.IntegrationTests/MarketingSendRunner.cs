using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Jobs;
using Modulix.Modules.Marketing.Runs;

namespace Modulix.IntegrationTests;

/// <summary>
/// Pazarlama gönderim işini testten çağırır: zamanlayıcı testlerde kapalıdır (<c>Jobs:WorkerEnabled</c>), iş tek tenant için
/// <see cref="ModuleJobRunner"/> ile doğrudan çalıştırılır (ADR-0046).
/// </summary>
internal sealed class MarketingSendRunner(IServiceProvider services)
{
    public const string JobKey = "marketing.send";

    /// <summary>Tur sonuçları; <c>Skipped</c>: aynı tenant için tur başka yerde çalıştığından atlandı. Modül kapalıysa liste boştur.</summary>
    public async Task<(IReadOnlyList<RunningProcess> Runs, bool Skipped)> RunTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<ModuleJobRunner>().RunTenantAsync(JobKey, tenantId, cancellationToken);
        return (run.Result?.Details as IReadOnlyList<RunningProcess> ?? [], run.Status == ModuleJobRunStatus.Locked);
    }
}
