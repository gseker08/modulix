using System.Text.Json;
using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Pricing;
using DomainRule = Modulix.Modules.Crm.Domain.SurchargeRule;
using EngineRule = Modulix.Core.Contracts.Pricing.SurchargeRule;

namespace Modulix.Modules.Crm.Pricing;

/// <summary>Ek ücret kuralı sabitleri ve saf kuralları (ADR-0018 devamı): alan doğrulaması ve kayıttan motor kuralına dönüşüm. Veritabanı gerektirmez.</summary>
public static class SurchargeRules
{
    public const string Fixed = "Fixed";
    public const string Percent = "Percent";
    public const string Minimum = "Minimum";
    public const string Duration = "Duration";
    public const string SeparateLine = "SeparateLine";
    public const string IncludedInGroup = "IncludedInGroup";
    public const string BreakdownOnly = "BreakdownOnly";

    public static IReadOnlyList<string> Kinds { get; } = [Fixed, Percent, Minimum, Duration];

    public static IReadOnlyList<string> Presentations { get; } = [SeparateLine, IncludedInGroup, BreakdownOnly];

    public static IReadOnlyList<string> Operators { get; } = [.. Enum.GetNames<ConditionOperator>()];

    /// <summary>Anahtar: harf, rakam, nokta, alt çizgi ve tire; en çok 100 karakter (özel alan anahtarları, <c>group.ad</c>, <c>line.kod</c>).</summary>
    public static bool IsValidKey(string? key) =>
        key is { Length: > 0 and <= DomainRule.KeyMaxLength } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>Çarpan anahtarı dizisi: yalnızca geçerli anahtar metinleri, tekrarsız.</summary>
    public static bool IsValidKeyList(JsonArray array) =>
        array.All(i => i is JsonValue v && v.GetValueKind() == JsonValueKind.String && IsValidKey(v.GetValue<string>()))
        && array.Select(i => i!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() == array.Count;

    /// <summary>Koşul dizisi: her öğe <c>key</c> (geçerli anahtar), <c>operator</c> (bilinen işleç) ve <c>value</c> (boş olmayan metin ≤ 100) içerir.</summary>
    public static bool IsValidConditionList(JsonArray array) =>
        array.All(i => i is JsonObject o
            && o["key"] is JsonValue key && key.GetValueKind() == JsonValueKind.String && IsValidKey(key.GetValue<string>())
            && o["operator"] is JsonValue op && op.GetValueKind() == JsonValueKind.String && Operators.Contains(op.GetValue<string>())
            && o["value"] is JsonValue value && value.GetValueKind() == JsonValueKind.String
            && value.GetValue<string>() is { Length: > 0 and <= 100 });

    /// <summary>Kayıt alanlarını kural türüne göre denetler; sonuç (alan, ileti) çiftleridir. Motorun kendi doğrulamasıyla aynı kuralları alan adlarıyla verir.</summary>
    public static IReadOnlyList<(string Field, ServerMessage Message)> Validate(DomainRule rule)
    {
        var errors = new List<(string, ServerMessage)>();
        var keys = ParseKeys(rule.QuantityKeys);
        switch (rule.Kind)
        {
            case Fixed:
                if (rule.Amount is null) { errors.Add(("amount", CrmMessages.Pricing.SurchargeAmountRequired)); }
                if (rule.Percent is not null) { errors.Add(("percent", CrmMessages.Pricing.SurchargeAmountOrPercent)); }
                break;
            case Percent:
                if (rule.Percent is not > 0) { errors.Add(("percent", CrmMessages.Pricing.SurchargePercentRequired)); }
                if (rule.Amount is not null) { errors.Add(("amount", CrmMessages.Pricing.SurchargeAmountOrPercent)); }
                if (string.IsNullOrWhiteSpace(rule.BaseKey)) { errors.Add(("baseKey", CrmMessages.Pricing.SurchargeBaseRequired)); }
                break;
            case Minimum:
                if (rule.Amount is not > 0) { errors.Add(("amount", CrmMessages.Pricing.SurchargeAmountRequired)); }
                if (rule.Percent is not null) { errors.Add(("percent", CrmMessages.Pricing.SurchargeAmountOrPercent)); }
                if (string.IsNullOrWhiteSpace(rule.GroupKey)) { errors.Add(("group", CrmMessages.Pricing.SurchargeGroupRequired)); }
                if (keys.Count > 0) { errors.Add(("quantityKeys", CrmMessages.Pricing.SurchargeMinimumNoMultiplier)); }
                break;
            case Duration:
                if (string.IsNullOrWhiteSpace(rule.DurationKey)) { errors.Add(("durationKey", CrmMessages.Pricing.SurchargeDurationRequired)); }
                if ((rule.Amount is null) == (rule.Percent is null)) { errors.Add(("amount", CrmMessages.Pricing.SurchargeAmountOrPercent)); }
                if (rule.Percent is not null && string.IsNullOrWhiteSpace(rule.BaseKey)) { errors.Add(("baseKey", CrmMessages.Pricing.SurchargeBaseRequired)); }
                break;
        }

        foreach (var (field, value) in new[] { ("baseKey", rule.BaseKey), ("durationKey", rule.DurationKey) })
        {
            if (value is not null && !IsValidKey(value)) { errors.Add((field, CrmMessages.Pricing.SurchargeKeyInvalid)); }
        }

        return errors;
    }

    /// <summary>Kayıttan motor kuralına dönüşüm (yalnızca etkin ve geçerli kayıtlar için; geçersiz JSON boş liste sayılır, doğrulama kayıtta yapılmıştır).</summary>
    public static EngineRule ToEngineRule(DomainRule rule) => new(
        rule.Id, rule.Name, Enum.Parse<SurchargeKind>(rule.Kind), rule.Currency, rule.Priority, rule.Amount, rule.Percent, rule.GroupKey, rule.BaseKey,
        rule.DurationKey, rule.FreeUnits, ParseKeys(rule.QuantityKeys), ParseConditions(rule.Conditions), Enum.Parse<SurchargePresentation>(rule.Presentation), rule.Stackable);

    /// <summary>Kuralların koşul, taban, süre ve çarpan olarak başvurduğu anahtarlar (bağlam anahtarı ya da özel alan anahtarı olabilir).</summary>
    public static IReadOnlySet<string> UsedKeys(IEnumerable<DomainRule> rules)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            keys.UnionWith(ParseConditions(rule.Conditions).Select(c => c.Key));
            keys.UnionWith(ParseKeys(rule.QuantityKeys));
            foreach (var key in new[] { rule.BaseKey, rule.DurationKey })
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    public static IReadOnlyList<string> ParseKeys(string? json) =>
        string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonArray array ? [] : [.. array.Select(i => i!.GetValue<string>())];

    public static IReadOnlyList<SurchargeCondition> ParseConditions(string? json) =>
        string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonArray array
            ? []
            : [.. array.Select(i => new SurchargeCondition(i!["key"]!.GetValue<string>(), Enum.Parse<ConditionOperator>(i["operator"]!.GetValue<string>()), i["value"]!.GetValue<string>()))];
}
