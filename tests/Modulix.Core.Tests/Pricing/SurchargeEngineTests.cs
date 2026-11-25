using Modulix.Core.Contracts.Pricing;

namespace Modulix.Core.Tests.Pricing;

/// <summary>
/// Ek ücret motoru (ADR-0018 devamı): kuralların hepsi veridir, kodda sektör yoktur. Senaryolar bir ardiye tarifesinden alınmıştır
/// (ADR ek ücreti, teminat yüzdesi, demuraj, asgari bedel, mesai) ama motor yalnızca anahtar ve tutar görür.
/// </summary>
public class SurchargeEngineTests
{
    private static SurchargeRule Rule(string name, SurchargeKind kind, int priority = 0, decimal? amount = null, decimal? percent = null,
        string? group = "ardiye", string? baseKey = null, string? durationKey = null, decimal free = 0m, string[]? quantity = null,
        SurchargeCondition[]? conditions = null, SurchargePresentation presentation = SurchargePresentation.SeparateLine,
        bool stackable = true, string currency = "EUR") =>
        new(Guid.NewGuid(), name, kind, currency, priority, amount, percent, group, baseKey, durationKey, free, quantity, conditions, presentation, stackable);

    private static SurchargeContext Context(Dictionary<string, decimal>? numbers = null, Dictionary<string, string>? texts = null, string currency = "EUR") =>
        new(currency, numbers ?? [], texts ?? []);

    private static SurchargeCondition Is(string key, string value) => new(key, ConditionOperator.Equals, value);

    // 6 palet, 10 gün, ADR yük, ÖTV'li (teminat 20.000), 1 beyanname; bant tutarı 150.
    private static SurchargeContext FullScenario() => Context(
        new() { ["group.ardiye"] = 150m, ["line.parsiyel"] = 150m, ["sure"] = 10m, ["beyan"] = 1m, ["teminat"] = 20000m },
        new() { ["yuk"] = "ADR", ["otv"] = "true" });

    private static SurchargeRule[] Tariff() =>
    [
        Rule("ADR ek ücreti", SurchargeKind.Fixed, 20, amount: 100m, quantity: ["beyan"], conditions: [Is("yuk", "ADR")]),
        Rule("ÖTV teminat payı", SurchargeKind.Percent, 30, percent: 0.5m, baseKey: "teminat", conditions: [Is("otv", "true")]),
        Rule("Demuraj", SurchargeKind.Duration, 40, percent: 10m, baseKey: "line.parsiyel", durationKey: "sure", free: 7m, quantity: ["beyan"]),
        Rule("Asgari ardiye bedeli", SurchargeKind.Minimum, 90, amount: 125m),
    ];

    [Fact]
    public void Tariff_example_charges_adr_otv_and_demurrage_and_skips_the_minimum()
    {
        var result = SurchargeEngine.Evaluate(Tariff(), FullScenario());

        Assert.Equal(["ADR ek ücreti", "ÖTV teminat payı", "Demuraj"], result.Applied.Select(a => a.Name));
        Assert.Equal([100m, 100m, 45m], result.Applied.Select(a => a.Amount)); // demuraj: %10 × 150 × 3 gün × 1 beyan
        Assert.Equal(245m, result.Total);
        Assert.Equal(395m, result.GroupTotals["ardiye"]);

        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("Asgari ardiye bedeli", skipped.Name);
        Assert.Contains("aşıldı", skipped.Reason);
    }

    [Fact]
    public void Minimum_adds_only_the_difference_up_to_the_floor()
    {
        // 1 palet, 3 gün: yalnız bant tutarı (100) var; koşullar sağlanmıyor.
        var context = Context(new() { ["group.ardiye"] = 100m, ["sure"] = 3m, ["beyan"] = 1m, ["line.parsiyel"] = 100m });
        var result = SurchargeEngine.Evaluate(Tariff(), context);

        var minimum = Assert.Single(result.Applied);
        Assert.Equal(25m, minimum.Amount);
        Assert.Equal(125m, result.GroupTotals["ardiye"]);
        Assert.Contains(result.Skipped, s => s.Name == "Demuraj" && s.Reason.Contains("serbest süre"));
    }

    [Fact]
    public void Minimum_does_not_charge_the_full_floor_when_the_group_has_no_lines()
    {
        var result = SurchargeEngine.Evaluate([Rule("Asgari", SurchargeKind.Minimum, amount: 125m)], Context(new() { ["group.baska"] = 500m }));

        Assert.Empty(result.Applied);
        Assert.Contains("kalem yok", Assert.Single(result.Skipped).Reason);
    }

    [Fact]
    public void Minimum_sees_surcharges_that_were_added_to_the_same_group()
    {
        // Bant 100, ADR +100 → grup 200 ≥ 125: asgari bedel çalışmaz.
        var context = Context(new() { ["group.ardiye"] = 100m, ["beyan"] = 1m }, new() { ["yuk"] = "ADR" });
        var result = SurchargeEngine.Evaluate(
            [Rule("ADR", SurchargeKind.Fixed, 1, amount: 100m, quantity: ["beyan"], conditions: [Is("yuk", "ADR")]), Rule("Asgari", SurchargeKind.Minimum, 9, amount: 125m)],
            context);

        Assert.Equal(["ADR"], result.Applied.Select(a => a.Name));
        Assert.Equal(200m, result.GroupTotals["ardiye"]);
    }

    [Fact]
    public void Multiple_quantity_keys_multiply_and_weekend_overtime_is_a_second_conditional_rule()
    {
        var rules = new[]
        {
            Rule("Mesai hafta içi", SurchargeKind.Fixed, 1, amount: 100m, group: null, quantity: ["saat", "kisi"], conditions: [new("gun", ConditionOperator.NotEquals, "hafta_sonu")]),
            Rule("Mesai hafta sonu", SurchargeKind.Fixed, 2, amount: 150m, group: null, quantity: ["saat", "kisi"], conditions: [Is("gun", "hafta_sonu")]),
        };
        var numbers = new Dictionary<string, decimal> { ["saat"] = 2m, ["kisi"] = 3m };

        var weekday = SurchargeEngine.Evaluate(rules, Context(numbers, new() { ["gun"] = "hafta_ici" }));
        Assert.Equal(600m, Assert.Single(weekday.Applied).Amount); // 100 × 2 saat × 3 kişi

        var weekend = SurchargeEngine.Evaluate(rules, Context(numbers, new() { ["gun"] = "hafta_sonu" }));
        Assert.Equal(900m, Assert.Single(weekend.Applied).Amount); // 150 × 2 × 3
        Assert.Contains(weekend.Skipped, s => s.Name == "Mesai hafta içi");
    }

    [Fact]
    public void Missing_inputs_never_become_a_guessed_amount()
    {
        var withoutBase = SurchargeEngine.Evaluate([Rule("ÖTV", SurchargeKind.Percent, percent: 0.5m, baseKey: "teminat")], Context());
        Assert.Empty(withoutBase.Applied);
        Assert.Contains("Eksik taban", Assert.Single(withoutBase.Skipped).Reason);

        var withoutQuantity = SurchargeEngine.Evaluate([Rule("ADR", SurchargeKind.Fixed, amount: 100m, quantity: ["beyan"])], Context());
        Assert.Contains("Eksik değer: beyan", Assert.Single(withoutQuantity.Skipped).Reason);

        // Değeri olmayan alan "eşit değil" koşulunu da sağlamaz.
        var withoutField = SurchargeEngine.Evaluate(
            [Rule("Hafta içi", SurchargeKind.Fixed, amount: 10m, conditions: [new("gun", ConditionOperator.NotEquals, "hafta_sonu")])], Context());
        Assert.Empty(withoutField.Applied);
    }

    [Fact]
    public void Currency_mismatch_and_zero_amounts_are_reported_not_applied()
    {
        var result = SurchargeEngine.Evaluate([Rule("TL kural", SurchargeKind.Fixed, amount: 100m, currency: "TRY")], Context(currency: "EUR"));
        Assert.Contains("TRY", Assert.Single(result.Skipped).Reason);

        var zero = SurchargeEngine.Evaluate([Rule("Sıfır", SurchargeKind.Fixed, amount: 0m)], Context());
        Assert.Contains("sıfır", Assert.Single(zero.Skipped).Reason);
    }

    [Fact]
    public void A_non_stackable_rule_closes_its_group_for_later_rules_but_not_other_groups()
    {
        var rules = new[]
        {
            Rule("Kampanya ücreti", SurchargeKind.Fixed, 1, amount: 50m, stackable: false),
            Rule("Sonraki ardiye ücreti", SurchargeKind.Fixed, 2, amount: 10m),
            Rule("Başka grup", SurchargeKind.Fixed, 3, amount: 5m, group: "gumruk"),
        };

        var result = SurchargeEngine.Evaluate(rules, Context());

        Assert.Equal(["Kampanya ücreti", "Başka grup"], result.Applied.Select(a => a.Name));
        Assert.Contains(result.Skipped, s => s.Name == "Sonraki ardiye ücreti" && s.Reason.Contains("birleşmeyen"));
    }

    [Fact]
    public void Numeric_conditions_compare_numbers_and_ignore_case_for_text()
    {
        var rules = new[]
        {
            Rule("Ağır yük", SurchargeKind.Fixed, 1, amount: 75m, conditions: [new("agirlik", ConditionOperator.GreaterThan, "5")]),
            Rule("ADR", SurchargeKind.Fixed, 2, amount: 100m, conditions: [Is("yuk", "adr")]),
        };

        var heavy = SurchargeEngine.Evaluate(rules, Context(new() { ["agirlik"] = 6.5m }, new() { ["yuk"] = "ADR" }));
        Assert.Equal([75m, 100m], heavy.Applied.Select(a => a.Amount));

        var five = SurchargeEngine.Evaluate(rules, Context(new() { ["agirlik"] = 5m }));
        Assert.Empty(five.Applied); // 5 > 5 değil
    }

    [Fact]
    public void Presentation_is_carried_per_rule_so_the_tenant_decides_how_each_one_is_shown()
    {
        var rules = new[]
        {
            Rule("Ayrı", SurchargeKind.Fixed, 1, amount: 10m, presentation: SurchargePresentation.SeparateLine),
            Rule("Dahil", SurchargeKind.Fixed, 2, amount: 20m, presentation: SurchargePresentation.IncludedInGroup),
            Rule("Dökümde", SurchargeKind.Fixed, 3, amount: 30m, presentation: SurchargePresentation.BreakdownOnly),
        };

        var result = SurchargeEngine.Evaluate(rules, Context());

        Assert.Equal(
            [SurchargePresentation.SeparateLine, SurchargePresentation.IncludedInGroup, SurchargePresentation.BreakdownOnly],
            result.Applied.Select(a => a.Presentation));
        Assert.Equal(60m, result.Total); // görünüm tutarı değiştirmez
    }

    [Fact]
    public void Results_are_rounded_per_line_and_independent_of_the_order_rules_are_supplied()
    {
        var rules = new[]
        {
            Rule("B", SurchargeKind.Percent, 2, percent: 0.5m, baseKey: "teminat"),
            Rule("A", SurchargeKind.Percent, 1, percent: 0.3333m, baseKey: "teminat"),
        };
        var context = Context(new() { ["teminat"] = 1000m });

        var forward = SurchargeEngine.Evaluate(rules, context);
        var reversed = SurchargeEngine.Evaluate(rules.Reverse(), context);

        Assert.Equal([3.33m, 5m], forward.Applied.Select(a => a.Amount)); // 3,333 → 3,33
        Assert.Equal(forward.Applied.Select(a => (a.Name, a.Amount)), reversed.Applied.Select(a => (a.Name, a.Amount)));
    }

    [Fact]
    public void A_flat_duration_rate_charges_per_day_after_the_free_period()
    {
        // Komple araç demurajı: 7 gün serbest, sonraki her gün sabit tutar.
        var rule = Rule("Demuraj kapalı saha", SurchargeKind.Duration, amount: 35m, durationKey: "sure", free: 7m);

        Assert.Equal(105m, Assert.Single(SurchargeEngine.Evaluate([rule], Context(new() { ["sure"] = 10m })).Applied).Amount);
        Assert.Empty(SurchargeEngine.Evaluate([rule], Context(new() { ["sure"] = 7m })).Applied);
    }

    [Fact]
    public void Invalid_rule_definitions_are_rejected_with_a_stable_code()
    {
        SurchargeRule[] invalid =
        [
            Rule("", SurchargeKind.Fixed, amount: 1m),
            Rule("küçük harf", SurchargeKind.Fixed, amount: 1m, currency: "eur"),
            Rule("negatif", SurchargeKind.Fixed, amount: -1m),
            Rule("sabit ve yüzde", SurchargeKind.Fixed, amount: 1m, percent: 1m),
            Rule("tabansız yüzde", SurchargeKind.Percent, percent: 1m),
            Rule("grupsuz asgari", SurchargeKind.Minimum, amount: 1m, group: null),
            Rule("çarpanlı asgari", SurchargeKind.Minimum, amount: 1m, quantity: ["beyan"]),
            Rule("süresiz demuraj", SurchargeKind.Duration, amount: 1m),
            Rule("iki ücretli demuraj", SurchargeKind.Duration, amount: 1m, percent: 1m, baseKey: "x", durationKey: "sure"),
        ];

        Assert.All(invalid, rule =>
            Assert.Equal(PricingErrors.InvalidRule, Assert.Throws<PricingException>(() => SurchargeEngine.Evaluate([rule], Context())).Code));
    }
}
