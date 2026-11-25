using System.Text.Json;
using Modulix.Shared.Automation;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>Deneme çalıştırması sonucunun akıştaki adımlara eşlenmesi (ADR-0056, E6-3b): hiçbir şey çalıştırılmaz; örnek kayıt yoksa işaret yoktur.</summary>
public class AutomationFlowOutcomesTests
{
    private static PlannedActionDto Planned(string id, bool permitted = true, bool? stepPassed = null) =>
        new(id, "core.flow.wait", "Bekle", "Read", JsonDocument.Parse("{}").RootElement.Clone(), permitted, null, stepPassed);

    private static AutomationPreviewDto Preview(bool evaluated, bool passed, params PlannedActionDto[] planned) =>
        new([], "tetikleyici", "koşul", evaluated, passed, null, planned, []);

    private static readonly string[] Steps = ["a", "b", "c"];

    [Fact]
    public void Passing_conditions_mark_each_step_as_it_would_run_be_skipped_or_be_unavailable()
    {
        var preview = Preview(true, true, Planned("a"), Planned("b", stepPassed: false), Planned("c", permitted: false));

        var outcomes = AutomationFlowOutcomes.From(preview, Steps);

        Assert.Equal(FlowOutcome.WouldRun, outcomes["a"]);
        Assert.Equal(FlowOutcome.SkippedByStepCondition, outcomes["b"]);
        Assert.Equal(FlowOutcome.NotPermitted, outcomes["c"]);
    }

    [Fact]
    public void A_rule_condition_that_fails_marks_every_step_as_not_run()
    {
        var outcomes = AutomationFlowOutcomes.From(Preview(true, false), Steps);

        Assert.Equal(Steps, outcomes.Keys);
        Assert.All(outcomes.Values, o => Assert.Equal(FlowOutcome.RuleNotMatched, o));
    }

    [Fact]
    public void Without_an_evaluated_sample_record_nothing_is_marked()
    {
        Assert.Empty(AutomationFlowOutcomes.From(null, Steps));
        Assert.Empty(AutomationFlowOutcomes.From(Preview(false, false), Steps));
    }

    [Fact]
    public void A_step_that_was_not_planned_gets_no_mark()
    {
        var outcomes = AutomationFlowOutcomes.From(Preview(true, true, Planned("a")), Steps);

        Assert.Equal(["a"], outcomes.Keys);
    }
}
