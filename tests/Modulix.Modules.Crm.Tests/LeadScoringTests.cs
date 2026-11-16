using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Leads;

namespace Modulix.Modules.Crm.Tests;

/// <summary>Müşteri adayı puan hesabı (ADR-0050, dilim 1 ve 2; veritabanı gerektirmez).</summary>
public class LeadScoringTests
{
    private static LeadScoreRule Rule(string field, string op, string? value, int points, bool active = true) =>
        new() { Name = field, Field = field, Operator = op, Value = value, Points = points, IsActive = active };

    [Fact]
    public void Matching_active_rules_are_summed()
    {
        var lead = new Lead { Source = "web", Country = "TR", Email = "a@b.com" };
        var rules = new[]
        {
            Rule("source", LeadScoreOperator.EqualsTo, "WEB", 20),
            Rule("country", LeadScoreOperator.EqualsTo, "TR", 15),
            Rule("email", LeadScoreOperator.IsSet, null, 10),
            Rule("phone", LeadScoreOperator.IsSet, null, 30),
            Rule("source", LeadScoreOperator.EqualsTo, "web", 40, active: false),
        };

        Assert.Equal(45, LeadScoring.Calculate(lead, rules));
    }

    [Fact]
    public void Total_is_clamped_between_zero_and_one_hundred()
    {
        var lead = new Lead { Source = "web" };
        Assert.Equal(100, LeadScoring.Calculate(lead, [Rule("source", LeadScoreOperator.EqualsTo, "web", 80), Rule("phone", LeadScoreOperator.IsEmpty, null, 80)]));
        Assert.Equal(0, LeadScoring.Calculate(lead, [Rule("source", LeadScoreOperator.EqualsTo, "web", -50)]));
        Assert.Equal(0, LeadScoring.Calculate(lead, []));
    }

    [Theory]
    [InlineData("Satın Alma Müdürü", LeadScoreOperator.Contains, "müdür", true)]
    [InlineData("Satın Alma Müdürü", LeadScoreOperator.Contains, "uzman", false)]
    [InlineData("  CEO ", LeadScoreOperator.EqualsTo, "ceo", true)]
    [InlineData(null, LeadScoreOperator.NotEquals, "ceo", true)]
    [InlineData("CEO", LeadScoreOperator.NotEquals, "ceo", false)]
    [InlineData(null, LeadScoreOperator.IsEmpty, null, true)]
    [InlineData("", LeadScoreOperator.IsSet, null, false)]
    public void Text_comparison_ignores_case_and_surrounding_spaces(string? jobTitle, string op, string? value, bool expected)
    {
        Assert.Equal(expected, LeadScoring.Matches(new Lead { JobTitle = jobTitle }, Rule("jobTitle", op, value, 10)));
    }

    [Fact]
    public void Service_scope_contains_matches_a_whole_code_only()
    {
        var lead = new Lead { ServiceScope = "NAKLIYE,DEPO" };
        Assert.True(LeadScoring.Matches(lead, Rule("serviceScope", LeadScoreOperator.Contains, "depo", 10)));
        Assert.False(LeadScoring.Matches(lead, Rule("serviceScope", LeadScoreOperator.Contains, "DEP", 10)));
    }

    [Fact]
    public void Unknown_fields_and_operators_not_allowed_for_the_field_never_match()
    {
        var lead = new Lead { Email = "a@b.com", EstimatedBudget = 1_000_000m };
        Assert.False(LeadScoring.Matches(lead, Rule("estimatedBudget", LeadScoreOperator.IsSet, null, 10)));
        Assert.False(LeadScoring.Matches(lead, Rule("email", LeadScoreOperator.Contains, "b.com", 10)));
    }

    [Fact]
    public void Budget_is_not_a_scoring_field()
    {
        Assert.DoesNotContain(LeadScoring.Fields, f => f.Key == "estimatedBudget");
    }

    [Fact]
    public void Behavior_counts_score_with_at_least_rules_and_default_to_zero()
    {
        var lead = new Lead();
        var rules = new[]
        {
            Rule("contacts", LeadScoreOperator.AtLeast, "2", 20),
            Rule("meetings", LeadScoreOperator.AtLeast, "1", 30),
            Rule("repeatInquiries", LeadScoreOperator.AtLeast, "1", 10),
        };

        Assert.Equal(0, LeadScoring.Calculate(lead, rules));
        Assert.Equal(20, LeadScoring.Calculate(lead, rules, new LeadBehavior(2, 0, 0)));
        Assert.Equal(60, LeadScoring.Calculate(lead, rules, new LeadBehavior(3, 1, 4)));
        Assert.True(LeadScoring.NeedsBehavior(rules));
        Assert.False(LeadScoring.NeedsBehavior([Rule("country", LeadScoreOperator.EqualsTo, "TR", 10), Rule("contacts", LeadScoreOperator.AtLeast, "1", 10, active: false)]));
    }

    [Fact]
    public void Inactive_days_with_negative_points_decay_the_score()
    {
        var lead = new Lead { Country = "TR" };
        var rules = new[]
        {
            Rule("country", LeadScoreOperator.EqualsTo, "TR", 40),
            Rule("inactiveDays", LeadScoreOperator.AtLeast, "14", -15),
            Rule("inactiveDays", LeadScoreOperator.AtLeast, "30", -15),
        };

        Assert.Equal(40, LeadScoring.Calculate(lead, rules, new LeadBehavior(1, 0, 0, InactiveDays: 13)));
        Assert.Equal(25, LeadScoring.Calculate(lead, rules, new LeadBehavior(1, 0, 0, InactiveDays: 14)));
        Assert.Equal(10, LeadScoring.Calculate(lead, rules, new LeadBehavior(1, 0, 0, InactiveDays: 45)));
        Assert.Equal(0, LeadScoring.Calculate(new Lead(), rules, new LeadBehavior(0, 0, 0, InactiveDays: 45)));
        Assert.True(LeadScoring.NeedsBehavior(rules));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData(" 03 ", 3)]
    [InlineData("1000", 1000)]
    [InlineData("0", null)]
    [InlineData("1001", null)]
    [InlineData("-2", null)]
    [InlineData("2.5", null)]
    [InlineData("iki", null)]
    public void At_least_value_is_a_whole_number_between_one_and_a_thousand(string value, int? expected)
    {
        Assert.Equal(expected, LeadScoring.ParseCount(value));
    }

    [Fact]
    public void At_least_is_only_valid_on_counters_and_counters_only_take_at_least()
    {
        Assert.False(LeadScoring.Matches(new Lead { JobTitle = "5" }, Rule("jobTitle", LeadScoreOperator.AtLeast, "1", 10)));
        Assert.False(LeadScoring.Matches(new Lead(), new LeadBehavior(5, 0, 0), Rule("contacts", LeadScoreOperator.IsSet, null, 10)));
        Assert.True(LeadScoring.Matches(new Lead(), new LeadBehavior(5, 0, 0), Rule("contacts", LeadScoreOperator.AtLeast, "5", 10)));
    }
}
