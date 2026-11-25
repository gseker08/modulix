using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Pricing;
using Modulix.Modules.Crm.Pricing;
using StoredRule = Modulix.Modules.Crm.Domain.SurchargeRule;

namespace Modulix.Modules.Crm.Tests;

/// <summary>Ek ücret kuralı kaydı (ADR-0018 devamı, Dilim 2b): türe göre alan doğrulaması, anahtar ve liste kuralları, kayıttan motora dönüşüm (veritabanı gerektirmez).</summary>
public class SurchargeRuleTests
{
    private static StoredRule Rule(string kind, decimal? amount = null, decimal? percent = null, string? group = null, string? baseKey = null,
        string? durationKey = null, string? quantityKeys = null) =>
        new() { Name = "Kural", Kind = kind, Currency = "EUR", Amount = amount, Percent = percent, GroupKey = group, BaseKey = baseKey, DurationKey = durationKey, QuantityKeys = quantityKeys };

    private static string[] Fields(StoredRule rule) => [.. SurchargeRules.Validate(rule).Select(e => e.Field).Distinct()];

    [Fact]
    public void Each_kind_demands_exactly_its_own_fields()
    {
        Assert.Empty(SurchargeRules.Validate(Rule(SurchargeRules.Fixed, amount: 100m, quantityKeys: "[\"beyan\"]")));
        Assert.Empty(SurchargeRules.Validate(Rule(SurchargeRules.Percent, percent: 0.5m, baseKey: "teminat")));
        Assert.Empty(SurchargeRules.Validate(Rule(SurchargeRules.Minimum, amount: 125m, group: "ardiye")));
        Assert.Empty(SurchargeRules.Validate(Rule(SurchargeRules.Duration, percent: 10m, baseKey: "line.parsiyel", durationKey: "sure")));
        Assert.Empty(SurchargeRules.Validate(Rule(SurchargeRules.Duration, amount: 35m, durationKey: "sure")));

        Assert.Equal(["amount"], Fields(Rule(SurchargeRules.Fixed)));
        Assert.Equal(["percent"], Fields(Rule(SurchargeRules.Fixed, amount: 1m, percent: 1m)));
        Assert.Equal(["percent", "baseKey"], Fields(Rule(SurchargeRules.Percent)));
        Assert.Equal(["amount", "group"], Fields(Rule(SurchargeRules.Minimum)));
        Assert.Equal(["quantityKeys"], Fields(Rule(SurchargeRules.Minimum, amount: 1m, group: "g", quantityKeys: "[\"x\"]")));
        Assert.Equal(["durationKey", "amount"], Fields(Rule(SurchargeRules.Duration)));
        Assert.Equal(["amount"], Fields(Rule(SurchargeRules.Duration, amount: 1m, percent: 1m, baseKey: "b", durationKey: "d")));
        Assert.Equal(["baseKey"], Fields(Rule(SurchargeRules.Duration, percent: 1m, durationKey: "sure")));
    }

    [Theory]
    [InlineData("yuk_ozelligi", true)]
    [InlineData("group.ardiye", true)]
    [InlineData("line.ARD-PARSIYEL", true)]
    [InlineData("", false)]
    [InlineData("boşluklu anahtar", false)]
    [InlineData("a;b", false)]
    public void Keys_allow_letters_digits_dot_underscore_and_hyphen_only(string key, bool valid) =>
        Assert.Equal(valid, SurchargeRules.IsValidKey(key));

    [Fact]
    public void Key_and_condition_lists_reject_malformed_items()
    {
        Assert.True(SurchargeRules.IsValidKeyList(new JsonArray("saat", "kisi")));
        Assert.True(SurchargeRules.IsValidKeyList([]));
        Assert.False(SurchargeRules.IsValidKeyList(new JsonArray("saat", "saat"))); // tekrar
        Assert.False(SurchargeRules.IsValidKeyList(new JsonArray("a b")));
        Assert.False(SurchargeRules.IsValidKeyList(new JsonArray(5)));

        Assert.True(SurchargeRules.IsValidConditionList(new JsonArray(new JsonObject { ["key"] = "yuk", ["operator"] = "Equals", ["value"] = "ADR" })));
        Assert.False(SurchargeRules.IsValidConditionList(new JsonArray(new JsonObject { ["key"] = "yuk", ["operator"] = "Benzer", ["value"] = "ADR" })));
        Assert.False(SurchargeRules.IsValidConditionList(new JsonArray(new JsonObject { ["key"] = "yuk", ["operator"] = "Equals", ["value"] = "" })));
        Assert.False(SurchargeRules.IsValidConditionList(new JsonArray(new JsonObject { ["operator"] = "Equals", ["value"] = "x" })));
        Assert.False(SurchargeRules.IsValidConditionList(new JsonArray("Equals")));
    }

    [Fact]
    public void Stored_rules_evaluate_through_the_engine_exactly_as_the_pure_rules_do()
    {
        // 6 palet, 10 gün, ADR yük, teminat 20.000: kayıt → motor kuralı → sonuç (Dilim 2a tarife senaryosu).
        var rules = new[]
        {
            new StoredRule
            {
                Name = "ADR ek ücreti", Kind = SurchargeRules.Fixed, Currency = "EUR", Priority = 20, Amount = 100m, GroupKey = "ardiye",
                QuantityKeys = "[\"beyan\"]", Conditions = "[{\"key\":\"yuk\",\"operator\":\"Equals\",\"value\":\"ADR\"}]",
            },
            new StoredRule
            {
                Name = "Demuraj", Kind = SurchargeRules.Duration, Currency = "EUR", Priority = 40, Percent = 10m, GroupKey = "ardiye", BaseKey = "line.parsiyel",
                DurationKey = "sure", FreeUnits = 7m, QuantityKeys = "[\"beyan\"]", Presentation = SurchargeRules.IncludedInGroup,
            },
            new StoredRule { Name = "Asgari", Kind = SurchargeRules.Minimum, Currency = "EUR", Priority = 90, Amount = 125m, GroupKey = "ardiye", Stackable = false },
        };
        Assert.All(rules, r => Assert.Empty(SurchargeRules.Validate(r)));

        var context = new SurchargeContext("EUR",
            new Dictionary<string, decimal> { ["group.ardiye"] = 150m, ["line.parsiyel"] = 150m, ["sure"] = 10m, ["beyan"] = 1m },
            new Dictionary<string, string> { ["yuk"] = "ADR" });
        var result = SurchargeEngine.Evaluate(rules.Select(SurchargeRules.ToEngineRule), context);

        Assert.Equal([100m, 45m], result.Applied.Select(a => a.Amount));
        Assert.Equal(SurchargePresentation.IncludedInGroup, result.Applied[1].Presentation);
        Assert.Equal("Asgari", Assert.Single(result.Skipped).Name);
    }

    [Fact]
    public void Surcharge_rule_has_no_sector_specific_text_in_its_domain_constants()
    {
        // Kodda sektör yok: tür ve görünüm adları genel kavramlardır.
        string[] all = [.. SurchargeRules.Kinds, .. SurchargeRules.Presentations, .. SurchargeRules.Operators];
        Assert.DoesNotContain(all, v => v.Contains("antrepo", StringComparison.OrdinalIgnoreCase) || v.Contains("lojistik", StringComparison.OrdinalIgnoreCase));
    }
}
