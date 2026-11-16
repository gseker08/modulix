using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Jobs;
using Modulix.Modules.Crm.Quotes;

namespace Modulix.IntegrationTests;

/// <summary>
/// Teklif süre dolumu zamanlanmış işini testten çağırır: zamanlayıcı testlerde kapalıdır (<c>Jobs:WorkerEnabled</c>), iş tek tenant için
/// <see cref="ModuleJobRunner"/> ile doğrudan çalıştırılır (ADR-0046).
/// </summary>
internal sealed class QuoteExpiryRunner(IServiceProvider services)
{
    public const string JobKey = "crm.quote-expiry";

    public static string LockResource(Guid tenantId) => ModuleJobRunner.LockResource(JobKey, tenantId);

    /// <summary>Tur özeti; <c>Skipped</c>: aynı tenant için tur başka yerde çalıştığından atlandı. Modül kapalıysa boş tur döner.</summary>
    public async Task<QuoteExpiryRun> RunTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<ModuleJobRunner>().RunTenantAsync(JobKey, tenantId, cancellationToken);
        return run.Result?.Details as QuoteExpiryRun ?? new QuoteExpiryRun(0, 0, Skipped: run.Status == ModuleJobRunStatus.Locked);
    }
}
