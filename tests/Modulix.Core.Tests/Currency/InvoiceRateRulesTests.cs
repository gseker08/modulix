using Modulix.Core.Contracts.Currency;

namespace Modulix.Core.Tests.Currency;

/// <summary>Fatura kuru kuralı (ADR-0049 dilim 5d devamı): aranacak gün; kur seçimi okuyucunun "en yeni önceki kur" kuralına bırakılır.</summary>
public class InvoiceRateRulesTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    [Fact]
    public void Previous_day_is_the_default_and_looks_one_day_back()
    {
        Assert.Equal(InvoiceRateRules.PreviousDay, InvoiceRateRules.Default);
        Assert.Equal(Monday.AddDays(-1), InvoiceRateRules.LookupDay(Monday, InvoiceRateRules.PreviousDay));
        Assert.Equal(Monday.AddDays(-1), InvoiceRateRules.LookupDay(Monday, null));
        Assert.Equal(Monday.AddDays(-1), InvoiceRateRules.LookupDay(Monday, "bilinmeyen"));
    }

    [Fact]
    public void Same_day_rule_uses_the_invoice_date()
    {
        Assert.Equal(Monday, InvoiceRateRules.LookupDay(Monday, InvoiceRateRules.SameDay));
        Assert.Equal(["previous-day", "same-day"], InvoiceRateRules.All);
    }
}
