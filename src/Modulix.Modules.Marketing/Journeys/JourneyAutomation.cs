using Modulix.Core.Contracts.Automation;
using Modulix.Core.Contracts.Events;

namespace Modulix.Modules.Marketing.Journeys;

/// <summary>
/// Müşteri yolculuğunun kural altyapısı yetenek beyanı (ADR-0056 dilim 2b): yolculuk, ADR-0027 kural sürümünün bir kullanım biçimidir; ayrı bir motor yoktur.
/// Kural, yolculuk kaydı (<see cref="MarketingModule.Entities.JourneyEnrollment"/>) üzerinde çalışır: tetikleyici "yolculuğa girildi" iş olayı, adımlar mevcut akış
/// aksiyonları (bekle, koşullu adım, bitir, bildir, görev aç) ve "yolculuk iletisi gönder"dir. Kuralı belirli bir yolculuğa bağlamak için koşula
/// <c>journeyId</c> yazılır (yolculuk kimliği).
/// </summary>
public static class JourneyAutomation
{
    public const string SendAction = "marketing.journey.send";

    /// <summary>CRM kişi okuma izni: alıcı çözümleyicinin kaynağı (<c>crm.contact</c>). Modül CRM'e başvuramadığı için anahtar burada yazılır; CRM'e bağımlılık beyanlıdır (ADR-0051).</summary>
    public const string CrmContactsRead = "crm.contacts.read";

    public static AutomationCapabilities Capabilities { get; } = new()
    {
        Entities =
        [
            new(MarketingModule.Entities.JourneyEnrollment, "Yolculuk kaydı", MarketingModule.Permissions.JourneysRead),
        ],
        Events =
        [
            new(MarketingModule.Events.JourneyEntered, MarketingModule.Entities.JourneyEnrollment, MarketingModule.Events.JourneyEntered, "Yolculuğa girildi"),
        ],
        Fields =
        [
            new(MarketingModule.Entities.JourneyEnrollment, "journeyId", "Yolculuk kimliği", AutomationFieldType.Text)
            {
                Hint = "Kuralı belirli bir yolculuğa bağlamak için koşulda kullanılır.",
            },
            new(MarketingModule.Entities.JourneyEnrollment, "status", "Kayıt durumu", AutomationFieldType.Choice)
            {
                Options = [new("Active", "Etkin"), new("Completed", "Tamamlandı"), new("Exited", "Çıktı")],
            },
        ],
        Actions =
        [
            AutomationActionRegistration.For<JourneySendAction>(new AutomationActionDefinition(
                SendAction,
                "Yolculuk iletisi gönder",
                "Yolculuktaki kişiye içeriği, kampanya gönderimiyle aynı kurallarla (iletişim izni ve engeli, sıklık sınırı, abonelikten çıkma bağlantısı) gönderir. "
                + "İzin yoksa ileti gitmez, adım atlanır ve yolculuk sürer. Aynı adım aynı kişiye bir kez gider.",
                [
                    new AutomationParameter("contentId", "İçerik", AutomationFieldType.Text)
                    {
                        MaxLength = 36,
                        AllowFieldBinding = false,
                        Description = "Kampanya içeriği kaydının kimliği; kanal ve konu içeriğin kampanyasından gelir.",
                    },
                    new AutomationParameter("recipientRule", "Alıcı (firma hedefinde)", AutomationFieldType.Choice, Required: false)
                    {
                        AllowFieldBinding = false,
                        Options =
                        [
                            new("PrimaryContact", "Birincil irtibat kişisi"),
                            new("DecisionMaker", "Karar verici"),
                            new("CompanyPoints", "Firmanın kendi iletişim noktaları"),
                        ],
                    },
                ],
                [MarketingModule.Permissions.CampaignsRead, MarketingModule.Permissions.JourneysRead, CrmContactsRead],
                [],
                AutomationSideEffect.Write,
                SupportsDryRun: true)),
        ],
    };
}
