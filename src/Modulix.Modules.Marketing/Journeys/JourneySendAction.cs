using System.Security.Cryptography;
using System.Text;
using Modulix.Core.Contracts.Automation;
using Modulix.Modules.Marketing.Contracts;
using Modulix.Modules.Marketing.Domain;

namespace Modulix.Modules.Marketing.Journeys;

/// <summary>
/// "Yolculuk iletisi gönder" aksiyonu (ADR-0056 dilim 2b): kuralın adımını <see cref="IJourneySender"/>'a çevirir. Kayıt + adım başına tek ileti kuralı, aksiyonun
/// tekilleştirme anahtarından türetilen adım anahtarıyla sağlanır: aynı adım yeniden çağrılsa (yeniden deneme, çökme sonrası) ileti ikinci kez gitmez.
/// Reddedilen ileti hata değildir (<see cref="AutomationActionStatus.Skipped"/>); yolculuk sürer.
/// </summary>
internal sealed class JourneySendAction(IJourneySender sender) : IAutomationAction
{
    public async Task<AutomationActionResult> ExecuteAsync(AutomationActionRequest request, AutomationCallContext context, CancellationToken cancellationToken)
    {
        if (request.Target is not { } target || target.EntityKey != MarketingModule.Entities.JourneyEnrollment)
        {
            return new AutomationActionResult(AutomationActionStatus.Failed, "Aksiyon bir yolculuk kaydı üzerinde çalışır.");
        }

        if (!Guid.TryParse(request.Parameters["contentId"]?.GetValue<string>(), out var contentId))
        {
            return new AutomationActionResult(AutomationActionStatus.Failed, "İçerik kimliği geçerli değil.");
        }

        var rule = RecipientRule.PrimaryContact;
        if (request.Parameters["recipientRule"]?.GetValue<string>() is { Length: > 0 } text && !Enum.TryParse(text, out rule))
        {
            return new AutomationActionResult(AutomationActionStatus.Failed, "Alıcı kuralı geçerli değil.");
        }

        if (request.DryRun)
        {
            return new AutomationActionResult(AutomationActionStatus.Planned, "İleti gönderilecekti (deneme: gönderilmedi).");
        }

        var result = await sender.SendAsync(new JourneySendRequest(target.Id, contentId, StepKey(request.IdempotencyKey), rule), cancellationToken);
        return result.Outcome switch
        {
            JourneyMessageOutcomes.Accepted => new AutomationActionResult(AutomationActionStatus.Succeeded, "İleti sağlayıcıya iletildi."),
            JourneyMessageOutcomes.Skipped => new AutomationActionResult(AutomationActionStatus.Skipped, $"İleti gönderilmedi ({result.Code})."),
            JourneyMessageOutcomes.Failed => new AutomationActionResult(AutomationActionStatus.Failed, $"İleti gönderilemedi ({result.Code})."),

            // Belirsiz sonuç (sağlayıcı kabul etmiş olabilir) ya da yarım kalmış önceki deneme: yeniden gönderilmez, akış sürer.
            _ => new AutomationActionResult(AutomationActionStatus.Succeeded, $"İletinin sonucu belirsiz ({result.Code ?? "işleniyor"}); yeniden gönderilmedi."),
        };
    }

    /// <summary>Adım anahtarı: tekilleştirme anahtarının SHA-256'sı (günlükteki sütuna sığar; anahtar tenant, olay, sürüm, kayıt ve adımı içerir).</summary>
    private static string StepKey(string idempotencyKey) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
}
