using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Pricing;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Pricing;
using Modulix.Modules.Crm.Quotes;
using StoredRule = Modulix.Modules.Crm.Domain.SurchargeRule;

namespace Modulix.Modules.Crm.Tests;

/// <summary>Teklifin ek ücret bağlamı (ADR-0018 devamı, Dilim 2c): anahtar biçimi, grup ve ürün toplamları, özel alanlar ve tarife senaryosu (veritabanı gerektirmez).</summary>
public class QuoteSurchargeContextTests
{
    private static QuoteLine Line(string? group, string? code, decimal quantity, decimal listAmount, decimal discount = 0m, bool included = true) =>
        new() { GroupName = group, ProductCode = code, Quantity = quantity, ListAmount = listAmount, LineDiscount = discount, Included = included };

    [Theory]
    [InlineData("Ardiye Bedeli", "ardiye-bedeli")]
    [InlineData("ARD-PARSIYEL", "ard-parsiyel")]
    [InlineData("Gümrük İşlemleri", "gumruk-islemleri")]
    [InlineData("  Çok   Boşluklu / Ad  ", "cok-bosluklu-ad")]
    [InlineData("ISPARTA", "isparta")]
    [InlineData("---", "")]
    [InlineData(null, "")]
    public void Slug_makes_a_lowercase_ascii_key_from_turkish_text(string? text, string expected) =>
        Assert.Equal(expected, QuoteSurchargeContextBuilder.Slug(text));

    [Fact]
    public void Groups_products_and_the_subtotal_use_line_amount_after_line_discount_and_skip_excluded_lines()
    {
        var context = QuoteSurchargeContextBuilder.Build("EUR",
        [
            Line("Ardiye Bedeli", "ARD-PARSIYEL", 6m, 150m, discount: 10m),
            Line("Ardiye Bedeli", "ARD-EK", 2m, 20m),
            Line("Gümrük", "BEYAN", 1m, 100m),
            Line("Ardiye Bedeli", "ISTEGE-BAGLI", 1m, 999m, included: false),
        ], null);

        Assert.Equal("EUR", context.Currency);
        Assert.Equal(160m, context.Numbers["group.ardiye-bedeli"]); // 140 + 20
        Assert.Equal(100m, context.Numbers["group.gumruk"]);
        Assert.Equal(140m, context.Numbers["line.ard-parsiyel"]);
        Assert.Equal(6m, context.Numbers["quantity.ard-parsiyel"]);
        Assert.Equal(260m, context.Numbers["quote.subtotal"]);
        Assert.DoesNotContain("line.istege-bagli", context.Numbers.Keys);
    }

    [Fact]
    public void Custom_values_become_numbers_or_texts_and_cannot_overwrite_built_in_keys()
    {
        var custom = new JsonObject { ["sure"] = 10, ["otv"] = true, ["yuk"] = " ADR ", ["bos"] = "  ", ["quote.subtotal"] = 1, ["liste"] = new JsonArray("a") };

        var context = QuoteSurchargeContextBuilder.Build("EUR", [Line("g", "c", 1m, 50m)], custom);

        Assert.Equal(10m, context.Numbers["sure"]);
        Assert.Equal("true", context.Texts["otv"]);
        Assert.Equal("ADR", context.Texts["yuk"]);
        Assert.DoesNotContain("bos", context.Texts.Keys);
        Assert.DoesNotContain("liste", context.Texts.Keys);
        Assert.Equal(50m, context.Numbers["quote.subtotal"]); // özel alan yerleşik anahtarı ezmez
    }

    [Fact]
    public void A_full_quote_runs_through_stored_rules_and_the_engine()
    {
        // 6 palet parsiyel ardiye (150 €) + 1 beyanname (100 €); ADR yük, 10 gün, teminat 20.000, ÖTV'li.
        var lines = new[] { Line("Ardiye Bedeli", "ARD-PARSIYEL", 6m, 150m), Line("Gümrük", "BEYAN", 1m, 100m) };
        var custom = new JsonObject { ["yuk"] = "adr", ["sure"] = 10, ["beyan"] = 1, ["teminat"] = 20000, ["otv"] = true };
        var rules = new[]
        {
            new StoredRule { Name = "ADR", Kind = SurchargeRules.Fixed, Currency = "EUR", Priority = 20, Amount = 100m, GroupKey = "ardiye-bedeli", QuantityKeys = "[\"beyan\"]",
                Conditions = "[{\"key\":\"yuk\",\"operator\":\"Equals\",\"value\":\"ADR\"}]" },
            new StoredRule { Name = "ÖTV", Kind = SurchargeRules.Percent, Currency = "EUR", Priority = 30, Percent = 0.5m, BaseKey = "teminat", GroupKey = "ardiye-bedeli",
                Conditions = "[{\"key\":\"otv\",\"operator\":\"Equals\",\"value\":\"true\"}]" },
            new StoredRule { Name = "Demuraj", Kind = SurchargeRules.Duration, Currency = "EUR", Priority = 40, Percent = 10m, BaseKey = "line.ard-parsiyel", DurationKey = "sure",
                FreeUnits = 7m, QuantityKeys = "[\"beyan\"]", GroupKey = "ardiye-bedeli" },
            new StoredRule { Name = "Asgari", Kind = SurchargeRules.Minimum, Currency = "EUR", Priority = 90, Amount = 125m, GroupKey = "ardiye-bedeli" },
        };

        var result = SurchargeEngine.Evaluate(rules.Select(SurchargeRules.ToEngineRule), QuoteSurchargeContextBuilder.Build("EUR", lines, custom));

        Assert.Equal([100m, 100m, 45m], result.Applied.Select(a => a.Amount));
        Assert.Equal(395m, result.GroupTotals["ardiye-bedeli"]);
        Assert.Equal("Asgari", Assert.Single(result.Skipped).Name);
    }

    [Fact]
    public void A_missing_custom_value_skips_the_rule_with_a_reason_instead_of_guessing()
    {
        var rule = new StoredRule { Name = "Demuraj", Kind = SurchargeRules.Duration, Currency = "EUR", Amount = 35m, DurationKey = "sure", FreeUnits = 7m };

        var result = SurchargeEngine.Evaluate([SurchargeRules.ToEngineRule(rule)], QuoteSurchargeContextBuilder.Build("EUR", [Line("g", "c", 1m, 10m)], new JsonObject()));

        Assert.Empty(result.Applied);
        Assert.Contains("Eksik değer: sure", Assert.Single(result.Skipped).Reason);
    }
}
