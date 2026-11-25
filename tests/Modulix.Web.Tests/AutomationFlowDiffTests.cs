using System.Text.Json;
using Modulix.Shared.Automation;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>Taslak ile yayınlanmış sürümün farkı (ADR-0056, E6-3): adımlar kimlikle eşlenir; eklenen, kaldırılan, değişen ve taşınan adım ile tetikleyici, koşul ve sürüm bilgisi.</summary>
public class AutomationFlowDiffTests
{
    private static AutomationVersionDto Version(string definition, string name = "Kural", int priority = 100, bool stop = false, DateTimeOffset? from = null) =>
        new(Guid.NewGuid(), 1, "Published", name, null, priority, stop, from, null, JsonDocument.Parse(definition).RootElement.Clone(), "h", null, null, null);

    private static string Step(string id, string action = "core.flow.wait", string parameters = "{}") =>
        $$"""{ "id": "{{id}}", "action": "{{action}}", "parameters": {{parameters}} }""";

    private static string Rule(string trigger, string? condition, params string[] steps) =>
        $$"""{ "trigger": {{trigger}}, {{(condition is null ? string.Empty : $"\"condition\": {condition},")}} "actions": [ {{string.Join(",", steps)}} ] }""";

    private const string Manual = """{ "type": "manual" }""";

    [Fact]
    public void Identical_versions_have_no_difference()
    {
        var definition = Rule(Manual, null, Step("a"), Step("b"));

        Assert.True(AutomationFlowDiff.Compare(Version(definition), Version(definition)).IsEmpty);
    }

    [Fact]
    public void An_added_and_a_removed_step_are_found_by_id()
    {
        var diff = AutomationFlowDiff.Compare(Version(Rule(Manual, null, Step("a"), Step("b"))), Version(Rule(Manual, null, Step("a"), Step("c"))));

        Assert.Contains(new FlowChange(FlowChangeKind.Added, "c"), diff.Steps);
        Assert.Contains(new FlowChange(FlowChangeKind.Removed, "b"), diff.Steps);
        Assert.DoesNotContain(diff.Steps, s => s.StepId == "a");
    }

    [Fact]
    public void A_step_with_other_parameters_is_changed_and_a_step_with_another_action_too()
    {
        var before = Version(Rule(Manual, null, Step("a", parameters: """{ "days": 1 }"""), Step("b")));
        var after = Version(Rule(Manual, null, Step("a", parameters: """{ "days": 3 }"""), Step("b", "core.flow.stop")));

        var diff = AutomationFlowDiff.Compare(before, after);

        Assert.Equal([new FlowChange(FlowChangeKind.Changed, "a"), new FlowChange(FlowChangeKind.Changed, "b")], diff.Steps.Where(s => s.Kind == FlowChangeKind.Changed));
    }

    [Fact]
    public void Swapped_steps_are_moved_but_a_step_inserted_between_does_not_move_the_others()
    {
        var swapped = AutomationFlowDiff.Compare(Version(Rule(Manual, null, Step("a"), Step("b"))), Version(Rule(Manual, null, Step("b"), Step("a"))));
        Assert.Equal(2, swapped.Steps.Count(s => s.Kind == FlowChangeKind.Moved));

        var inserted = AutomationFlowDiff.Compare(Version(Rule(Manual, null, Step("a"), Step("b"))), Version(Rule(Manual, null, Step("a"), Step("x"), Step("b"))));
        Assert.Equal([new FlowChange(FlowChangeKind.Added, "x")], inserted.Steps);
    }

    [Fact]
    public void A_changed_trigger_condition_and_version_settings_are_reported()
    {
        var leaf = """{ "type": "leaf", "field": "f", "operator": "eq", "values": ["1"] }""";
        var other = """{ "type": "leaf", "field": "f", "operator": "eq", "values": ["2"] }""";
        var before = Version(Rule(Manual, leaf, Step("a")), "Eski", 100, false);
        var after = Version(Rule("""{ "type": "recordCreated" }""", other, Step("a")), "Yeni", 50, true, DateTimeOffset.UtcNow);

        var diff = AutomationFlowDiff.Compare(before, after);

        Assert.True(diff.TriggerChanged);
        Assert.True(diff.ConditionChanged);
        Assert.Equal(["name", "priority", "stopProcessing", "validity"], diff.MetaChanges);
        Assert.Empty(diff.Steps);
    }
}
