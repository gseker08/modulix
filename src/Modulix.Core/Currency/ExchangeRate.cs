using Modulix.Core.Contracts.Currency;
using Modulix.Core.Contracts.Tenancy;

namespace Modulix.Core.Currency;

/// <summary>
/// Döviz kuru (ADR-0049 dilim 5a; ADR-0018'de CRM'deydi): 1 <see cref="FromCurrency"/> = <see cref="Rate"/> <see cref="ToCurrency"/>,
/// <see cref="ValidOn"/> gününden itibaren. Aynı çift ve gün için tek kayıt vardır. Tenant verisidir: Row-Level Security ile korunur, tenant
/// dışa aktarımına ve silmesine girer; şirketler arasında ortaktır (K5.3: tenant bazı). Kayıt boru hattından yazılır (<c>core.exchange-rate</c>).
/// </summary>
public sealed class ExchangeRate : ITenantEntity, ICompanyShared
{
    public const int CurrencyLength = 3;
    public const int NoteMaxLength = 500;

    private ExchangeRate()
    {
        // EF Core için
    }

    public ExchangeRate(string fromCurrency, string toCurrency, decimal rate, DateOnly validOn, string source, string? note)
    {
        Id = Guid.CreateVersion7();
        FromCurrency = fromCurrency;
        ToCurrency = toCurrency;
        Rate = rate;
        ValidOn = validOn;
        Source = source;
        Note = note;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string FromCurrency { get; private set; } = string.Empty;

    public string ToCurrency { get; private set; } = string.Empty;

    public decimal Rate { get; private set; }

    public DateOnly ValidOn { get; private set; }

    /// <summary><see cref="ExchangeRateSources"/>.</summary>
    public string Source { get; private set; } = ExchangeRateSources.Manual;

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    public void Change(string fromCurrency, string toCurrency, decimal rate, DateOnly validOn, string source, string? note)
    {
        FromCurrency = fromCurrency;
        ToCurrency = toCurrency;
        Rate = rate;
        ValidOn = validOn;
        Source = source;
        Note = note;
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
    }
}
