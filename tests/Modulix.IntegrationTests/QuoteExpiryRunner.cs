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

    /// <summary>Tenant başına kilit kaynağı (zamanlayıcının kilidiyle aynı ad).</summary>
    public static string LockResource(Guid tenantId) => ModuleJobRunner.LockResource(JobKey, tenantId);

    /// <summary>Tur özeti; kilit başka yerde tutuluyorsa <c>Skipped</c>, modül kapalı ya da salt okunursa boş tur.</summary>
    public async Task<QuoteExpiryRun> RunTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<ModuleJobRunner>().RunTenantAsync(JobKey, tenantId, cancellationToken);
        return run.Status switch
        {
            ModuleJobRunStatus.Completed => (QuoteExpiryRun)run.Result!.Details!,
            ModuleJobRunStatus.Locked => new QuoteExpiryRun(0, 0, Skipped: true),
            _ => new QuoteExpiryRun(0, 0),
        };
    }
}
