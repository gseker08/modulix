using Modulix.Core.Contracts.Pricing;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Pricing;
using Modulix.Modules.Crm.Quotes;
using Modulix.Modules.Crm.Records;
using StoredRule = Modulix.Modules.Crm.Domain.SurchargeRule;

namespace Modulix.Modules.Crm.Tests;

/// <summary>Kuraldan teklif satırı üretimi (ADR-0018 devamı, Dilim 2c): saf planlama, kararlı anahtar, vergi oranı ve kalemsiz teklif kuralları (veritabanı gerektirmez).</summary>
public class QuoteSurchargeGeneratorTests
{
    private static StoredRule Rule(string name, decimal amount, decimal? tax = 20m, int priority = 0, string presentation = SurchargeRules.SeparateLine) =>
        new() { Name = name, Kind = SurchargeRules.Fixed, Currency = "EUR", Amount = amount, TaxRatePercent = tax, Priority = priority, Presentation = presentation };

    private static QuoteLine Line(decimal amount, string? group = "Ardiye Bedeli") =>
        new() { GroupName = group, ProductCode = "ARD", Quantity = 1m, ListAmount = amount, Included = true };

    private static SurchargeContext Context(params QuoteLine[] lines) => QuoteSurchargeContextBuilder.Build("EUR", lines, null);

    [Fact]
    public void Applied_rules_become_desired_lines_in_engine_order_with_tax_version_and_presentation()
    {
        var familyId = Guid.NewGuid();
        var rules = new[] { Rule("İkinci", 20m, priority: 2, presentation: SurchargeRules.BreakdownOnly), Rule("Birinci", 10m, tax: 10m, priority: 1) };

        var plan = QuoteSurchargeGenerator.Plan(familyId, rules, QuoteSurchargeGenerator.Evaluate(rules, Context(Line(100m)), hasIncludedLines: true));

        Assert.Equal(["Birinci", "İkinci"], plan.Select(p => p.Name));
        Assert.Equal([10m, 20m], plan.Select(p => p.Amount));
        Assert.Equal([10m, 20m], plan.Select(p => p.TaxRatePercent));
        Assert.Equal([SurchargeRules.SeparateLine, SurchargeRules.BreakdownOnly], plan.Select(p => p.Presentation));
        Assert.All(plan, p => Assert.Equal(1L, p.RuleVersion));
        Assert.Contains("10", plan[0].Explanation);
    }

    [Fact]
    public void A_rule_without_a_tax_rate_is_not_applied_and_the_reason_is_listed()
    {
        var rules = new[] { Rule("Vergili", 10m), Rule("Vergisiz", 99m, tax: null) };

        var result = QuoteSurchargeGenerator.Evaluate(rules, Context(Line(100m)), hasIncludedLines: true);

        Assert.Equal(["Vergili"], result.Applied.Select(a => a.Name));
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(("Vergisiz", "Vergi oranı tanımlı değil."), (skipped.Name, skipped.Reason));
    }

    [Fact]
    public void A_quote_without_included_lines_gets_no_surcharge_at_all()
    {
        var rules = new[] { Rule("Sabit", 10m), Rule("Vergisiz", 5m, tax: null) };

        var result = QuoteSurchargeGenerator.Evaluate(rules, Context(), hasIncludedLines: false);

        Assert.Empty(result.Applied);
        Assert.Equal(2, result.Skipped.Count);
        Assert.All(result.Skipped, s => Assert.Equal("Teklifte kalem yok.", s.Reason));
    }

    [Fact]
    public void The_stable_key_is_deterministic_per_family_and_rule_so_revisions_line_up()
    {
        var family = Guid.NewGuid();
        var rule = Guid.NewGuid();

        Assert.Equal(QuoteSurchargeGenerator.StableKey(family, rule), QuoteSurchargeGenerator.StableKey(family, rule));
        Assert.NotEqual(QuoteSurchargeGenerator.StableKey(family, rule), QuoteSurchargeGenerator.StableKey(Guid.NewGuid(), rule));
        Assert.NotEqual(QuoteSurchargeGenerator.StableKey(family, rule), QuoteSurchargeGenerator.StableKey(family, Guid.NewGuid()));
        Assert.NotEqual(Guid.Empty, QuoteSurchargeGenerator.StableKey(family, rule));
    }

    [Fact]
    public void Generated_lines_never_feed_the_context_so_a_surcharge_cannot_inflate_its_own_base()
    {
        var generated = new QuoteLine { GroupName = "Ardiye Bedeli", Quantity = 1m, ListAmount = 100m, Included = true, SourceRuleId = Guid.NewGuid() };

        var context = QuoteSurchargeContextBuilder.Build("EUR", [Line(150m), generated], null);

        Assert.Equal(150m, context.Numbers["group.ardiye-bedeli"]);
        Assert.Equal(150m, context.Numbers["quote.subtotal"]);
    }

    [Fact]
    public void Long_names_and_explanations_are_cut_to_the_line_column_limits()
    {
        var rule = Rule(new string('A', 500), 10m);

        var plan = QuoteSurchargeGenerator.Plan(Guid.NewGuid(), [rule], QuoteSurchargeGenerator.Evaluate([rule], Context(Line(100m)), true));

        Assert.Equal(QuoteLine.NameMaxLength, Assert.Single(plan).Name.Length);
    }
}
