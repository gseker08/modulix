using System.Text.Json;
using Modulix.Shared.Automation;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>Yolculuğa bağlı kuralı bulma (ADR-0056): bağ, kural koşulundaki <c>journeyId</c> satırıdır; başka yolculuk, başka alan ve koşulsuz kural bağlı sayılmaz.</summary>
public class JourneyRuleLinksTests
{
    private static readonly Guid Journey = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Rule(string condition) =>
        $$"""{ "trigger": { "type": "domainEvent", "eventKey": "marketing.journey.entered" }, "condition": {{condition}}, "actions": [] }""";

    private static string Leaf(string field, string value) =>
        $$"""{ "type": "leaf", "field": "{{field}}", "operator": "eq", "values": ["{{value}}"] }""";

    [Fact]
    public void A_condition_naming_the_journey_in_the_journey_field_binds_the_rule()
    {
        Assert.True(JourneyRuleLinks.References(Json(Rule(Leaf(JourneyRuleLinks.JourneyField, Journey.ToString()))), Journey));
        Assert.True(JourneyRuleLinks.References(Json(Rule(Leaf(JourneyRuleLinks.JourneyField, Journey.ToString().ToUpperInvariant()))), Journey));
    }

    [Fact]
    public void A_nested_group_is_searched_too()
    {
        var nested = $$"""{ "type": "group", "logic": "or", "items": [ { "type": "group", "logic": "and", "items": [ {{Leaf("crm.x", "1")}}, {{Leaf(JourneyRuleLinks.JourneyField, Journey.ToString())}} ] } ] }""";

        Assert.True(JourneyRuleLinks.References(Json(Rule(nested)), Journey));
    }

    [Fact]
    public void Another_journey_another_field_and_a_missing_condition_do_not_bind()
    {
        Assert.False(JourneyRuleLinks.References(Json(Rule(Leaf(JourneyRuleLinks.JourneyField, Guid.NewGuid().ToString()))), Journey));
        Assert.False(JourneyRuleLinks.References(Json(Rule(Leaf("crm.company.name", Journey.ToString()))), Journey));
        Assert.False(JourneyRuleLinks.References(Json("""{ "trigger": { "type": "manual" }, "actions": [] }"""), Journey));
        Assert.False(JourneyRuleLinks.References(Json("[]"), Journey));
    }

    private static AutomationVersionDto Version(string definition) =>
        new(Guid.NewGuid(), 1, "Published", "Kural", null, 100, false, null, null, Json(definition), "h", null, null, null);

    private static AutomationRuleDetailDto Detail(string? published, string? draft) =>
        new(new AutomationRuleSummaryDto(Guid.NewGuid(), "Kural", "marketing", JourneyRuleLinks.EntityKey, "Active", 1, draft is not null, 100, DateTimeOffset.UtcNow, 1),
            draft is null ? null : Version(draft), published is null ? null : Version(published), [], []);

    [Fact]
    public void A_published_binding_runs_and_a_draft_only_binding_waits_for_publication()
    {
        var bound = Rule(Leaf(JourneyRuleLinks.JourneyField, Journey.ToString()));
        var other = Rule(Leaf(JourneyRuleLinks.JourneyField, Guid.NewGuid().ToString()));

        Assert.Equal(JourneyRuleBinding.Published, JourneyRuleLinks.BindingOf(Detail(bound, null), Journey));
        Assert.Equal(JourneyRuleBinding.DraftOnly, JourneyRuleLinks.BindingOf(Detail(other, bound), Journey));
        Assert.Equal(JourneyRuleBinding.None, JourneyRuleLinks.BindingOf(Detail(other, other), Journey));
    }

    [Fact]
    public void Only_rules_on_the_journey_enrollment_type_can_bind()
    {
        var onEnrollment = Detail(null, null).Rule;
        var onCompany = onEnrollment with { EntityKey = "crm.company" };

        Assert.True(JourneyRuleLinks.IsJourneyRule(onEnrollment));
        Assert.False(JourneyRuleLinks.IsJourneyRule(onCompany));
    }
}
