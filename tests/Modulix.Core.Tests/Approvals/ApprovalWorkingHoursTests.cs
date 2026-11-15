using Modulix.Core.Approvals;
using Modulix.Core.Contracts.Calendar;

namespace Modulix.Core.Tests.Approvals;

/// <summary>ADR-0049 dilim 6d: süreç seçeneğiyle onay adımı süresi firmanın iş takvimiyle (mesai, çalışma günleri, tatil) sayılır.</summary>
public class ApprovalWorkingHoursTests
{
    // Cuma 16:00 İstanbul (UTC+3).
    private static readonly DateTimeOffset FridayAfternoon = new(2026, 11, 13, 13, 0, 0, TimeSpan.Zero);
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    private static readonly CalendarSpec Week = new("Europe/Istanbul", false, WorkWeek.Weekly(WorkWeek.MondayToFriday, WorkWeek.DefaultHours), []);

    private static ApprovalDefinition Definition(bool workingHours, params ApprovalStepDefinition[] steps) =>
        ApprovalDefinition.Create("Satın alma", null, null, steps, Requester, FridayAfternoon, dueInWorkingHours: workingHours);

    private static ApprovalStepDefinition Step(string name, int order, int? due) => new(name, order, Approver, null, null, due);

    [Fact]
    public void Working_hours_skip_the_evening_and_the_weekend()
    {
        var definition = Definition(true, Step("Yönetici", 1, 8));

        var (request, steps) = ApprovalRequest.Open(definition, "Yazıcı", null, null, null, Requester, FridayAfternoon, workCalendar: new WorkCalendar(Week));

        Assert.True(request.DueInWorkingHours);

        // Cuma 2 saat + Pazartesi 6 saat: Pazartesi 15:00 İstanbul.
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero), Assert.Single(steps).DueAt);
    }

    [Fact]
    public void A_company_holiday_pushes_the_deadline_one_more_day()
    {
        var definition = Definition(true, Step("Yönetici", 1, 8));
        var calendar = new WorkCalendar(TenantHolidays.Apply(Week, [new HolidayDay(new DateOnly(2026, 11, 16), "Tatil", false)]));

        var (_, steps) = ApprovalRequest.Open(definition, "Yazıcı", null, null, null, Requester, FridayAfternoon, workCalendar: calendar);

        Assert.Equal(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero), Assert.Single(steps).DueAt);
    }

    [Fact]
    public void Without_the_option_the_deadline_stays_on_wall_clock_even_if_a_calendar_is_given()
    {
        var definition = Definition(false, Step("Yönetici", 1, 8));

        var (request, steps) = ApprovalRequest.Open(definition, "Yazıcı", null, null, null, Requester, FridayAfternoon, workCalendar: new WorkCalendar(Week));

        Assert.False(request.DueInWorkingHours);
        Assert.Equal(FridayAfternoon.AddHours(8), Assert.Single(steps).DueAt);
    }

    [Fact]
    public void The_next_step_opened_by_an_approval_also_counts_in_working_hours()
    {
        var definition = Definition(true, Step("Yönetici", 1, null), Step("Genel müdür", 2, 4));
        var calendar = new WorkCalendar(Week);
        var (request, steps) = ApprovalRequest.Open(definition, "Yazıcı", null, null, null, Requester, FridayAfternoon, workCalendar: calendar);

        // Cuma 17:00 İstanbul onay: 1 saat Cuma + 3 saat Pazartesi = Pazartesi 12:00 İstanbul.
        request.Approve(steps[0], steps, Approver, null, FridayAfternoon.AddHours(1), workCalendar: calendar);

        Assert.Equal(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero), steps[1].DueAt);
    }

    [Fact]
    public void A_working_hours_request_needs_the_calendar_to_open_a_timed_step()
    {
        var definition = Definition(true, Step("Yönetici", 1, 8));

        Assert.Throws<InvalidOperationException>(() =>
            ApprovalRequest.Open(definition, "Yazıcı", null, null, null, Requester, FridayAfternoon));
    }

    [Fact]
    public void Working_hours_limit_the_step_time_to_about_a_year_of_work()
    {
        Assert.Throws<ArgumentException>(() => Definition(true, Step("Yönetici", 1, ApprovalDefinition.MaxWorkingDueHours + 1)));
        Assert.Equal(ApprovalDefinition.MaxWorkingDueHours + 1, Definition(false, Step("Yönetici", 1, ApprovalDefinition.MaxWorkingDueHours + 1)).Steps[0].DueHours);
    }

    [Fact]
    public void Turning_the_option_on_is_a_new_revision()
    {
        var definition = Definition(false, Step("Yönetici", 1, 8));

        definition.Update("Satın alma", null, null, definition.Steps, Requester, FridayAfternoon, dueInWorkingHours: true);

        Assert.True(definition.DueInWorkingHours);
        Assert.Equal(2, definition.Revision);
    }
}
