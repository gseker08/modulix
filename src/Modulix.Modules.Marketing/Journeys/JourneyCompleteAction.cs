using Modulix.Core.Contracts.Automation;
using Modulix.Core.Contracts.Records;

namespace Modulix.Modules.Marketing.Journeys;

/// <summary>
/// "Yolculuğu tamamla" aksiyonu (ADR-0056 dilim 2c-1): kuralın son adımı olarak kullanılır; etkin yolculuk kaydını <c>Completed</c> yapar. Kayıt daha önce çıkmış ya da
/// tamamlanmışsa (ör. abonelikten çıktı, elle çıkarıldı) adım atlanır ve akış biter; tamamlanmış kayıt tekrar tamamlanmaz (idempotent).
/// </summary>
internal sealed class JourneyCompleteAction(IJourneyEnrollments enrollments) : IAutomationAction
{
    public async Task<AutomationActionResult> ExecuteAsync(AutomationActionRequest request, AutomationCallContext context, CancellationToken cancellationToken)
    {
        if (request.Target is not { } target || target.EntityKey != MarketingModule.Entities.JourneyEnrollment)
        {
            return new AutomationActionResult(AutomationActionStatus.Failed, "Aksiyon bir yolculuk kaydı üzerinde çalışır.");
        }

        if (request.DryRun)
        {
            return new AutomationActionResult(AutomationActionStatus.Planned, "Yolculuk tamamlanacaktı (deneme: yazılmadı).");
        }

        var result = await enrollments.CompleteAsync(target.Id, cancellationToken);
        if (result.IsSuccess)
        {
            return new AutomationActionResult(AutomationActionStatus.Succeeded, "Yolculuk tamamlandı.") { Effect = new RecordRef(target.EntityKey, target.Id) };
        }

        return result.Error!.Code == RecordErrorCode.Conflict
            ? new AutomationActionResult(AutomationActionStatus.Skipped, "Kayıt zaten yolculukta değil; adım atlandı.") { StopFlow = true }
            : new AutomationActionResult(AutomationActionStatus.Failed, "Yolculuk tamamlanamadı.");
    }
}
