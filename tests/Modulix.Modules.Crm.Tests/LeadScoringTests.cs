using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Leads;

namespace Modulix.Modules.Crm.Tests;

/// <summary>Müşteri adayı puan hesabı (ADR-0050, dilim 1; veritabanı gerektirmez).</summary>
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
}
