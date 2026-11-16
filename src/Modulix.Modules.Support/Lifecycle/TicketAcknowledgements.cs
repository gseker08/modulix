using System.Net;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Notifications;
using Modulix.Modules.Support.Domain;
using Modulix.Modules.Support.Records;

namespace Modulix.Modules.Support.Lifecycle;

/// <summary>
/// Otomatik "alındı" yanıtı (ADR-0024 §4.12): talep açılınca başvurana talep numarasını bildirir. Firma ayarıyla açılır (varsayılan kapalı). Talep oluşturmayla aynı işlemde
/// e-posta kuyruğuna ve talebin mesajlarına otomatik mesaj olarak yazılır; ilk insan yanıtı sayılmaz (<see cref="FirstResponseRules"/>), SLA sayacını durdurmaz.
/// Alıcı adresi yoksa hiçbir şey yazılmaz; geçersiz adreste mesaj "gönderilemedi" olarak görünür. Konu, yanıtlarla aynı etiketi (<c>[numara] konu</c>) taşır:
/// müşterinin bu e-postaya yanıtı gelen kanalda talebe eşlenir.
/// </summary>
internal sealed class AcknowledgementSender(SupportReader reader, IEmailQueue emails, IRecipientLanguage languages, ISupportPolicyProvider? policy = null)
{
    public async Task SendAsync(SupportStoreContext context, SupportTicket ticket, CancellationToken cancellationToken)
    {
        if (policy is null || !(await policy.GetAsync(cancellationToken)).AcknowledgementEnabled)
        {
            return;
        }

        var (address, name) = await reader.RecipientAsync(ticket, cancellationToken);
        if (address is null)
        {
            return;
        }

        // Müşterinin hesabı yoktur: firmanın varsayılan dili (ADR-0034 §6).
        LocalizedTexts t = await languages.ForExternalAsync(cancellationToken);
        var recipient = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var greeting = recipient is null ? t.Get("email.common.greeting-anonymous") : t.Get("email.common.greeting", recipient);
        var body = t.Get("email.ticket-received.body", ticket.Number);
        var footer = t.Get("email.ticket-received.footer");
        var text = $"{greeting}\n\n{body}\n\n{footer}";

        string delivery;
        try
        {
            emails.Enqueue(new EmailMessage(address, $"[{ticket.Number}] {ticket.Subject}",
                $"<p>{WebUtility.HtmlEncode(greeting)}</p><p>{WebUtility.HtmlEncode(body)}</p><p style=\"color:#666\">{WebUtility.HtmlEncode(footer)}</p>")
            {
                ToName = recipient,
                TextBody = text,
                Reference = $"support.ack:{ticket.Id}",
                Language = t.Language,
            });
            delivery = DeliveryStatus.Queued;
        }
        catch (ArgumentException)
        {
            delivery = DeliveryStatus.Failed;
        }

        context.Data.Set<TicketMessage>().Add(new TicketMessage
        {
            TicketId = ticket.Id,
            Visibility = MessageVisibility.Public,
            Direction = MessageDirection.Outbound,
            Body = text,
            AuthorId = null,
            DeliveryStatus = delivery,
            IsAutomatic = true,
        });
    }
}

