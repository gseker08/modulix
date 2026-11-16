using Modulix.Core.Contracts.Modules;

namespace Modulix.Modules.Contracts.Lifecycle;

/// <summary>
/// Sözleşme zamanlanmış işi (ADR-0046): host'taki tek zamanlayıcı, sözleşme modülü yazılabilir olan her tenant için <see cref="IContractScheduler"/> turunu
/// çalıştırır (yürürlük geçişleri, otomatik uzatma, fesih, hatırlatma ve eskalasyon, imza ve taahhüt süreleri). İş "contracts.scheduler" sistem kimliğiyle ve
/// kendi tenant kapsamında çalışır; kapsamı, lisans kontrolünü, tenant başına kilidi ve kaydı (kuyruğa yazılan e-postalar dahil) zamanlayıcı yapar. Aynı turun
/// iki kez çalışması güvenlidir (dönem kararı, hatırlatma ve eskalasyon benzersiz kayıtlarla bir kez üretilir).
/// </summary>
internal sealed class ContractJobs(IContractScheduler scheduler) : IModuleJob
{
    /// <summary>İşin anahtarı: yapılandırma (<c>Jobs:contracts.jobs</c>), ölçüm adı ve kilit (<c>modulix.contracts.jobs:&lt;tenant&gt;</c>) bu ada bağlıdır.</summary>
    public const string JobKey = "contracts.jobs";

    /// <summary>İşin sistem kimliği (<see cref="ContractSchedulerAnchor"/> aboneliği).</summary>
    public const string Identity = "contracts.scheduler";

    public async Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken)
    {
        var r = await scheduler.RunAsync(cancellationToken);
        int total = r.AutoRenewed + r.RemindersCreated + r.EscalationsCreated + r.PeriodsExpired + r.Terminated + r.Activated + r.SignaturesExpired
            + r.ApprovalReminders + r.CommitmentsBreached;
        string? summary = total > 0
            ? $"{r.Activated} aktif, {r.PeriodsExpired} süresi doldu, {r.Terminated} fesih, {r.AutoRenewed} otomatik uzatma, {r.RemindersCreated} hatırlatma, "
                + $"{r.EscalationsCreated} eskalasyon, {r.SignaturesExpired} imza süresi, {r.ApprovalReminders} onay hatırlatması, {r.CommitmentsBreached} taahhüt ihlali."
            : null;
        return new ModuleJobResult(summary, r);
    }
}
