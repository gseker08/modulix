namespace Modulix.Shared.Currency;

// Firmanın döviz kurları (ADR-0049 dilim 5a; ADR-0018'de CRM'deydi). Kayıt türü core.exchange-rate: /api/records/core.exchange-rate.
// Kaynak Manual | CentralBank | Import; aynı çift ve gün için tek kayıt vardır; 1 FromCurrency = Rate ToCurrency.

public sealed record ExchangeRateDto(Guid Id, long Version, string FromCurrency, string ToCurrency, decimal Rate, DateOnly ValidOn, string Source, string? Note);

public sealed record SaveExchangeRateRequest(
    string FromCurrency, string ToCurrency, decimal Rate, DateOnly ValidOn, string Source = "Manual", string? Note = null, long? Version = null);
