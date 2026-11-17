using Modulix.Core.Contracts.Modules;

namespace Modulix.Modules.Support.Lifecycle;

/// <summary>
/// Destek zamanlanmış işi (ADR-0046): host'taki tek zamanlayıcı, destek modülü yazılabilir olan her tenant için <see cref="ISlaScheduler"/> turunu çalıştırır
/// (ihlal işareti, eskalasyon eylemleri, otomatik kapatma). İş "support.scheduler" sistem kimliğiyle ve kendi tenant kapsamında çalışır; kapsamı, lisans kontrolünü,
/// tenant başına kilidi ve kaydı zamanlayıcı yapar. Aynı turun iki kez çalışması güvenlidir (bkz. <see cref="ISlaScheduler"/>).
/// </summary>
internal sealed class SupportJobs(ISlaScheduler scheduler) : IModuleJob
{
    /// <summary>İşin anahtarı: yapılandırma (<c>Jobs:support.jobs</c>), ölçüm adı ve kilit bu ada bağlıdır.</summary>
    public const string JobKey = "support.jobs";

    /// <summary>İşin sistem kimliği (<see cref="SupportSchedulerAnchor"/> aboneliği).</summary>
    public const string Identity = "support.scheduler";

    public async Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken)
    {
        var run = await scheduler.RunAsync(cancellationToken);
        string? summary = run.Breaches + run.Escalations + run.AutoClosed + run.SurveyReminders > 0
            ? $"{run.Breaches} ihlal işaretlendi, {run.Escalations} eskalasyon eylemi uygulandı, {run.AutoClosed} talep otomatik kapatıldı, {run.SurveyReminders} anket hatırlatıldı."
            : null;
        return new ModuleJobResult(summary, run);
    }
}
