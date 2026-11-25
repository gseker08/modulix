using System.Text.Json.Nodes;

namespace Modulix.Shared.Crm;

// Ek ücret kuralı (ADR-0018 devamı, Dilim 2b). Kind: Fixed | Percent | Minimum | Duration. Presentation: SeparateLine | IncludedInGroup | BreakdownOnly.
// QuantityKeys: metin dizisi (çarpan anahtarları). Conditions: [{ "key", "operator", "value" }]; operator: Equals | NotEquals | GreaterThan | GreaterOrEqual | LessThan | LessOrEqual.
// Fixed: Amount (× QuantityKeys). Percent: Percent × BaseKey. Minimum: Amount ve Group (grup toplamı bunun altındaysa fark eklenir). Duration: DurationKey, FreeUnits ve
// Amount ya da Percent (+BaseKey). Tutar ve yüzdeden yalnızca biri girilir. Kural yalnızca Currency'deki tekliflere uygulanır.

public sealed record SurchargeRuleDto(
    Guid Id,
    long Version,
    string Name,
    string? Description,
    int Priority,
    bool IsActive,
    string Kind,
    string Currency,
    decimal? Amount,
    decimal? Percent,
    string? Group,
    string? BaseKey,
    string? DurationKey,
    decimal FreeUnits,
    JsonArray? QuantityKeys,
    JsonArray? Conditions,
    string Presentation,
    bool Stackable);

public sealed record SaveSurchargeRuleRequest(
    string Name,
    string Kind = "Fixed",
    string Currency = "TRY",
    string? Description = null,
    int Priority = 0,
    bool IsActive = true,
    decimal? Amount = null,
    decimal? Percent = null,
    string? Group = null,
    string? BaseKey = null,
    string? DurationKey = null,
    decimal FreeUnits = 0m,
    JsonArray? QuantityKeys = null,
    JsonArray? Conditions = null,
    string Presentation = "SeparateLine",
    bool Stackable = true,
    long? Version = null);
