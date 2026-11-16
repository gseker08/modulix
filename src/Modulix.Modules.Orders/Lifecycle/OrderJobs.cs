using Modulix.Core.Contracts.Modules;

namespace Modulix.Modules.Orders.Lifecycle;

/// <summary>
/// Sipariş zamanlanmış işi (ADR-0046): host'taki tek zamanlayıcı, sipariş modülü açık her tenant için <see cref="IOrderScheduler"/> turunu çalıştırır.
/// İş "orders.scheduler" sistem kimliğiyle ve kendi tenant kapsamında çalışır; kapsamı, lisans kontrolünü, tenant başına kilidi ve kaydı zamanlayıcı yapar.
/// </summary>
internal sealed class OrderJobs(IOrderScheduler scheduler) : IModuleJob
{
    /// <summary>İşin anahtarı: yapılandırma (<c>Jobs:orders.jobs</c>), ölçüm adı ve kilit bu ada bağlıdır.</summary>
    public const string JobKey = "orders.jobs";

    /// <summary>İşin sistem kimliği (<see cref="OrdersSchedulerAnchor"/> aboneliği).</summary>
    public const string Identity = "orders.scheduler";

    public async Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken)
    {
        var run = await scheduler.RunAsync(cancellationToken);
        string? summary = run.HandoverEscalations + run.UnassignedJobAlerts + run.OverdueJobAlerts > 0
            ? $"{run.HandoverEscalations} devir kabul gecikmesi, {run.UnassignedJobAlerts} atama bekleyen iş, {run.OverdueJobAlerts} geciken iş takibi."
            : null;
        return new ModuleJobResult(summary, run);
    }
}
