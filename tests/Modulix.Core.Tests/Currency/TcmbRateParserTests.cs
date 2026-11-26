using Modulix.Core.Currency;

namespace Modulix.Core.Tests.Currency;

/// <summary>TCMB günlük bülteni ayrıştırıcısı (ADR-0049 dilim 5d): satış kuru, birim çarpanı, bozuk satır ve belgelerin elenmesi.</summary>
public class TcmbRateParserTests
{
    private const string Bulletin = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Tarih_Date Tarih="26.11.2026" Date="11/26/2026" Bulten_No="2026/225">
          <Currency CrossOrder="0" Kod="USD" CurrencyCode="USD"><Unit>1</Unit><ForexBuying>34.10</ForexBuying><ForexSelling>34.1234</ForexSelling></Currency>
          <Currency CrossOrder="1" Kod="EUR" CurrencyCode="EUR"><Unit>1</Unit><ForexBuying>37.00</ForexBuying><ForexSelling>37.5000</ForexSelling></Currency>
          <Currency CrossOrder="2" Kod="JPY" CurrencyCode="JPY"><Unit>100</Unit><ForexBuying>22.00</ForexBuying><ForexSelling>22.50</ForexSelling></Currency>
          <Currency CrossOrder="3" Kod="XDR" CurrencyCode="XDR"><Unit>1</Unit><ForexBuying></ForexBuying><ForexSelling></ForexSelling></Currency>
          <Currency CrossOrder="4" Kod="BAD" CurrencyCode="BADCODE"><Unit>1</Unit><ForexSelling>5</ForexSelling></Currency>
          <Currency CrossOrder="5" Kod="ZER" CurrencyCode="ZER"><Unit>1</Unit><ForexSelling>0</ForexSelling></Currency>
        </Tarih_Date>
        """;

    [Fact]
    public void Selling_rate_is_taken_per_unit_and_unusable_rows_are_skipped()
    {
        var snapshot = TcmbRateParser.Parse(Bulletin)!;

        Assert.Equal(new DateOnly(2026, 11, 26), snapshot.Date);
        Assert.Equal(["USD", "EUR", "JPY"], snapshot.Rates.Select(r => r.Code));
        Assert.Equal(34.1234m, snapshot.Rates.Single(r => r.Code == "USD").Rate);
        Assert.Equal(0.225m, snapshot.Rates.Single(r => r.Code == "JPY").Rate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>bakım</html>")]
    [InlineData("<Tarih_Date Date=\"yarın\"><Currency CurrencyCode=\"USD\"><Unit>1</Unit><ForexSelling>1</ForexSelling></Currency></Tarih_Date>")]
    [InlineData("<Tarih_Date Date=\"11/26/2026\"></Tarih_Date>")]
    [InlineData("<Tarih_Date Date=\"11/26/2026\"><Currency CurrencyCode=\"USD\"><Unit>1</Unit><ForexSelling>abc</ForexSelling></Currency></Tarih_Date>")]
    public void Malformed_or_empty_bulletins_give_nothing(string xml) => Assert.Null(TcmbRateParser.Parse(xml));
}
