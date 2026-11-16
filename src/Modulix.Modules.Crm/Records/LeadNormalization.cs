using Modulix.Core.Contracts.Numbering;

namespace Modulix.Modules.Crm.Records;

/// <summary>Lead'e özgü normalizasyon yardımcıları (numara üretimi, alan adı çıkarma).</summary>
internal static class LeadNormalization
{
    /// <summary>
    /// "LD-20261028-7K2M4Q": tarih + 6 rastgele karakter (ortak yardımcı, ADR-0049 dilim 3a). Müşteri adayı belge değildir ve toplu içe aktarmayla
    /// çok sayıda açılır; sıralı seriye geçmedi. Çakışma (neredeyse imkânsız) benzersiz indeksle 409 olur ve istemci yeniden dener.
    /// </summary>
    public static string NewNumber(DateTimeOffset createdAt) => RandomDocumentNumber.New("LD", createdAt);

    /// <summary>"https://www.Acme.com/x" → "acme.com"; alan adı çıkarılamazsa null.</summary>
    public static string? Domain(string? website)
    {
        if (string.IsNullOrWhiteSpace(website))
        {
            return null;
        }

        var text = website.Trim();
        var host = Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text, UriKind.Absolute, out var uri)
            ? uri.Host
            : text;
        host = host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        return host.Contains('.') && !host.Any(char.IsWhiteSpace) ? host : null;
    }
}
