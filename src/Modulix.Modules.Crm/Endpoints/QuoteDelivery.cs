using System.Globalization;
using System.Net;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Notifications;
using Modulix.Modules.Crm.Quotes;

namespace Modulix.Modules.Crm.Endpoints;

/// <summary>Bir paylaşım bağlantısının e-posta gönderim durumu. <see cref="Status"/> <see cref="QuoteDeliveryStatus"/> değerlerinden biridir.</summary>
public sealed record QuoteLinkDelivery(string Status, string? Error, DateTimeOffset? ProviderAcceptedAt);

/// <summary>
/// Teklif e-postası gönderimi (ADR-0019, dilim 5). E-posta platform kuyruğuna yazılır (<see cref="IEmailQueue"/>); arka plan işleyicisi SMTP'ye
/// verir. Durum kuyruktan okunur ve dürüstçe adlandırılır: <c>Succeeded</c> yalnızca sağlayıcının (SMTP sunucusunun) kabul ettiğini söyler,
/// alıcıya ulaştığını söylemez. Gönderen/SMTP yapılandırılmamışsa e-posta kuyrukta <c>Pending</c> bekler; hiçbir şey sahte "gönderildi" olmaz.
/// Geliştirmede SMTP yerel Mailpit'tir, gerçek müşteriye e-posta gitmez.
/// </summary>
internal sealed class QuoteDelivery(IEmailQueue emails, IEmailDeliveries deliveries, IRecipientLanguage languages)
{
    public const string ReferencePrefix = "crm.quote-link:";

    public static string Reference(Guid linkId) => $"{ReferencePrefix}{linkId:N}";

    /// <summary>
    /// Müşteri bağlantı e-postasını kuyruğa yazar. Müşterinin hesabı olmadığı için e-posta firmanın varsayılan dilindedir (ADR-0034 §6, dilim 18b).
    /// Başarısızlıkta istisna fırlatır (çağıran yakalar ve durumu <c>Failed</c> gösterir).
    /// </summary>
    public async Task EnqueueAsync(
        Guid linkId, string to, string? recipientName, string sellerName, string quoteNumber, string quoteTitle, DateTimeOffset? validUntil, string url,
        CancellationToken cancellationToken)
    {
        LocalizedTexts t = await languages.ForExternalAsync(cancellationToken);
        emails.Enqueue(QuoteEmails.ShareLink(t, to, recipientName, sellerName, quoteNumber, quoteTitle, validUntil, url) with { Reference = Reference(linkId) });
    }

    /// <summary>Bağlantıların e-posta durumu; e-postası olmayan bağlantı sonuçta yoktur (gönderilmedi).</summary>
    public async Task<IReadOnlyDictionary<Guid, QuoteLinkDelivery>> GetAsync(IEnumerable<Guid> linkIds, CancellationToken cancellationToken)
    {
        var references = linkIds.ToDictionary(Reference, id => id);
        if (references.Count == 0)
        {
            return new Dictionary<Guid, QuoteLinkDelivery>();
        }

        // Kuyruk aktif tenant'ın e-postalarıyla süzülür (IEmailDeliveries); başka tenant'ın aynı anahtarlı e-postası görünmez.
        var states = await deliveries.GetAsync(references.Keys, cancellationToken);
        return states.ToDictionary(p => references[p.Key], p => Map(p.Value.Status, p.Value.Attempts, p.Value.LastError, p.Value.SentAt));
    }

    /// <summary>Kuyruk durumunu teklif gönderim durumuna eşler (saf).</summary>
    public static QuoteLinkDelivery Map(EmailDeliveryStatus status, int attempts, string? error, DateTimeOffset? sentAt) => status switch
    {
        EmailDeliveryStatus.Sent => new QuoteLinkDelivery(QuoteDeliveryStatus.Succeeded, null, sentAt),
        EmailDeliveryStatus.Failed => new QuoteLinkDelivery(QuoteDeliveryStatus.Failed, Shorten(error), null),
        // Deneme başlamış ama bitmemişse (tekrar deneme bekliyor) işleniyor; hata kısa gösterilir.
        _ when attempts > 0 => new QuoteLinkDelivery(QuoteDeliveryStatus.Processing, Shorten(error), null),
        _ => new QuoteLinkDelivery(QuoteDeliveryStatus.Pending, null, null),
    };

    /// <summary>Teklifin gönderim durumu: e-postası olan en son bağlantının durumu; hiç e-posta yoksa <c>NotSent</c>.</summary>
    public static string Overall(IEnumerable<(DateTimeOffset CreatedAt, QuoteLinkDelivery? Delivery)> links) =>
        links.Where(l => l.Delivery is not null).OrderByDescending(l => l.CreatedAt).Select(l => l.Delivery!.Status).FirstOrDefault() ?? QuoteDeliveryStatus.NotSent;

    /// <summary>Gönderim durumunun Türkçe adı (tarihçe notu için).</summary>
    public static string Label(string status) => status switch
    {
        QuoteDeliveryStatus.Pending => "Gönderim bekliyor",
        QuoteDeliveryStatus.Processing => "İşleniyor",
        QuoteDeliveryStatus.Succeeded => "Sağlayıcı kabul etti",
        QuoteDeliveryStatus.Failed => "Gönderilemedi",
        _ => "Gönderilmedi",
    };

    private static string? Shorten(string? error) => string.IsNullOrWhiteSpace(error) ? null : error.Length > 200 ? error[..200] : error;
}

/// <summary>Müşteriye giden teklif e-postası. Kullanıcı girdisi (ad, başlık, firma adı) HTML'e kodlanarak eklenir; konu tek satırdır.</summary>
internal static class QuoteEmails
{
    public static EmailMessage ShareLink(
        LocalizedTexts t, string to, string? recipientName, string sellerName, string quoteNumber, string quoteTitle, DateTimeOffset? validUntil, string url)
    {
        var greeting = string.IsNullOrWhiteSpace(recipientName) ? t.Get("email.common.greeting-anonymous") : t.Get("email.common.greeting", recipientName.Trim());
        var title = OneLine(quoteTitle);
        var seller = OneLine(sellerName);
        var paragraphs = new List<string>
        {
            t.Get("email.quote.share.intro", seller, title, quoteNumber),
            t.Get("email.quote.share.instructions"),
        };
        if (validUntil is { } until)
        {
            paragraphs.Add(t.Get("email.quote.share.valid-until", until.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)));
        }

        var footer = t.Get("email.quote.share.footer");
        var html = string.Concat(
            $"<p>{Encode(greeting)}</p>",
            string.Concat(paragraphs.Select(p => $"<p>{Encode(p)}</p>")),
            $"<p><a href=\"{Encode(url)}\">{Encode(t.Get("email.quote.share.action"))}</a></p>",
            $"<p style=\"color:#666\">{Encode(footer)}</p>");
        var text = string.Join("\n\n", [greeting, .. paragraphs, t.Get("email.quote.share.action-text", url), footer]);
        return new EmailMessage(to, t.Get("email.quote.share.subject", quoteNumber, seller), html)
        {
            ToName = string.IsNullOrWhiteSpace(recipientName) ? null : recipientName.Trim(),
            TextBody = text,
            Language = t.Language,
        };
    }

    private static string OneLine(string value) => string.Join(' ', value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
