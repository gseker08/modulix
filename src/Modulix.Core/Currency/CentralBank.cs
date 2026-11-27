using System.Globalization;
using System.Xml.Linq;

namespace Modulix.Core.Currency;

/// <summary>Merkez bankasının bir para birimi için yayımladığı kur: 1 <see cref="Code"/> = <see cref="Rate"/> TRY (birim çarpanı uygulanmış).</summary>
public sealed record CentralBankRate(string Code, decimal Rate);

/// <summary>Merkez bankasının bir günlük bülteni; <see cref="Date"/> kurun geçerlilik günüdür.</summary>
public sealed record CentralBankSnapshot(DateOnly Date, IReadOnlyList<CentralBankRate> Rates);

/// <summary>
/// Merkez bankası kur kaynağı (ADR-0049 dilim 5d). Dış ağa çıkan tek sınır burasıdır; kapalıysa ya da ulaşılamıyorsa <c>null</c> döner
/// (eski kur kalır, kur uydurulmaz). Yapılandırma ile açılır: varsayılan kapalıdır.
/// </summary>
public interface ICentralBankFeed
{
    Task<CentralBankSnapshot?> FetchAsync(CancellationToken cancellationToken);
}

/// <summary>
/// TCMB günlük kur bülteni (today.xml) ayrıştırıcısı. Satış kuru (<c>ForexSelling</c>) alınır; <c>Unit</c> çarpanı (ör. JPY 100) uygulanır.
/// Bozuk belge, bilinmeyen tarih ya da pozitif olmayan kur için o satır atlanır; hiç geçerli satır yoksa <c>null</c> döner. Saf kuraldır.
/// </summary>
public static class TcmbRateParser
{
    public static CentralBankSnapshot? Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var root = document.Root;
        if (root is null
            || !DateOnly.TryParseExact((string?)root.Attribute("Date"), "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        var rates = new List<CentralBankRate>();
        foreach (var currency in root.Elements("Currency"))
        {
            var code = ((string?)currency.Attribute("CurrencyCode"))?.Trim().ToUpperInvariant();
            if (code is not { Length: 3 }
                || !decimal.TryParse((string?)currency.Element("ForexSelling"), NumberStyles.Number, CultureInfo.InvariantCulture, out var selling)
                || selling <= 0m)
            {
                continue;
            }

            var unit = decimal.TryParse((string?)currency.Element("Unit"), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0m ? parsed : 1m;
            rates.Add(new CentralBankRate(code, selling / unit));
        }

        return rates.Count == 0 ? null : new CentralBankSnapshot(date, rates);
    }
}
