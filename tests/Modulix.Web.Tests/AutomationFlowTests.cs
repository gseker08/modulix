using System.Text.Json;
using Modulix.Shared.Automation;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>Kural sürümünün salt okunur akışa çevrilmesi (ADR-0056, E6-1): tetikleyici → koşul → adımlar; bilinmeyen parça silinmez, teknik anahtarıyla görünür.</summary>
public class AutomationFlowTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static readonly AutomationEntityDto Entity = new(
        "marketing", "marketing.journey-enrollment", "Yolculuk kaydı",
        [new AutomationFieldDto("lastResponseKind", "Son yanıt türü", "Choice", ["IsEmpty", "IsNotEmpty"], [], null, null)],
        [new AutomationEventDto("marketing.journey.entered", "marketing.journey.entered", "Yolculuğa girildi")],
        []);

    private static AutomationActionDto Action(string key, string name) => new(key, "marketing", name, string.Empty, "None", false, true, null, []);

    private static readonly AutomationActionDto[] Actions =
    [
        Action("marketing.journey.send", "Yolculuk iletisi gönder"),
        Action("core.flow.wait", "Bekle"),
        Action("core.flow.stop", "Akışı bitir"),
    ];

    private const string BranchingRule = """
        {
          "trigger": { "type": "domainEvent", "eventKey": "marketing.journey.entered" },
          "condition": { "type": "group", "logic": "and", "items": [
            { "type": "leaf", "field": "journeyId", "operator": "eq", "values": ["abc"] } ] },
          "actions": [
            { "id": "bekle", "action": "core.flow.wait", "parameters": { "days": 3 } },
            { "id": "yanit-var", "action": "marketing.journey.send", "parameters": { "contentId": "c1" },
              "when": { "type": "leaf", "field": "lastResponseKind", "operator": "isNotEmpty", "values": [] } },
            { "id": "bitir", "action": "core.flow.stop", "parameters": {} }
          ]
        }
        """;

    [Fact]
    public void A_rule_becomes_trigger_condition_and_steps_in_order()
    {
        var nodes = AutomationFlow.Build(Json(BranchingRule), Entity, Actions);

        Assert.Equal(
            [FlowNodeKind.Trigger, FlowNodeKind.Condition, FlowNodeKind.Wait, FlowNodeKind.Action, FlowNodeKind.Stop],
            nodes.Select(n => n.Kind));
        Assert.Equal("Yolculuğa girildi", Assert.Single(nodes[0].Lines));
        Assert.Equal(["Bekle", "Yolculuk iletisi gönder", "Akışı bitir"], nodes.Skip(2).Select(n => n.Title));
    }

    [Fact]
    public void A_conditional_step_is_marked_as_a_branch_and_an_unconditional_one_is_not()
    {
        var nodes = AutomationFlow.Build(Json(BranchingRule), Entity, Actions);

        var branch = Assert.Single(nodes, n => n.Branch);
        Assert.Equal("Yolculuk iletisi gönder", branch.Title);
        Assert.Contains("Son yanıt türü", Assert.Single(branch.BranchLines));
        Assert.All(nodes.Where(n => n != branch), n => Assert.False(n.Branch));
    }

    [Fact]
    public void A_rule_without_a_condition_has_no_condition_node_and_a_rule_without_steps_still_shows_the_trigger()
    {
        var nodes = AutomationFlow.Build(Json("""{ "trigger": { "type": "recordCreated" }, "actions": [] }"""), Entity, Actions);

        Assert.Equal(FlowNodeKind.Trigger, Assert.Single(nodes).Kind);
    }

    [Fact]
    public void An_unknown_action_and_field_keep_their_technical_keys_and_nothing_is_dropped()
    {
        var nodes = AutomationFlow.Build(Json("""
            { "trigger": { "type": "recordCreated" },
              "condition": { "type": "leaf", "field": "gizemli", "operator": "isEmpty", "values": [] },
              "actions": [ { "id": "a", "action": "yeni.modul.aksiyon", "parameters": { "x": "1" } } ] }
            """), null, []);

        Assert.Equal(3, nodes.Count);
        Assert.Contains("gizemli", Assert.Single(nodes[1].Lines));
        Assert.Equal("yeni.modul.aksiyon", nodes[2].Title);
        Assert.Equal("x: 1", Assert.Single(nodes[2].Lines));
    }

    [Fact]
    public void A_nested_condition_is_shown_as_advanced_without_listing_its_rows()
    {
        var nodes = AutomationFlow.Build(Json("""
            { "trigger": { "type": "manual" },
              "condition": { "type": "group", "logic": "or", "items": [
                { "type": "group", "logic": "and", "items": [ { "type": "leaf", "field": "a", "operator": "eq", "values": ["1"] } ] } ] },
              "actions": [] }
            """), Entity, Actions);

        var condition = Assert.Single(nodes, n => n.Kind == FlowNodeKind.Condition);
        Assert.Empty(condition.Lines);
    }

    [Fact]
    public void Step_nodes_carry_their_position_and_a_step_without_an_action_is_still_drawn()
    {
        var nodes = AutomationFlow.Build(Json("""
            { "trigger": { "type": "manual" }, "actions": [
              { "id": "a", "action": "core.flow.wait", "parameters": {} },
              { "id": "b", "action": "", "parameters": {} } ] }
            """), Entity, Actions);

        Assert.Null(nodes[0].StepIndex);
        Assert.Equal([0, 1], nodes.Where(n => n.StepIndex is not null).Select(n => n.StepIndex!.Value));
        Assert.Equal(FlowNodeKind.Action, nodes[^1].Kind);
    }
}
