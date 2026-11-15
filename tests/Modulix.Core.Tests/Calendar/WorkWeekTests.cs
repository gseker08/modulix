using Modulix.Core.Contracts.Calendar;
using Modulix.Core.Modules;

namespace Modulix.Core.Tests.Calendar;

/// <summary>ADR-0049 dilim 6c: firmanın haftalık mesai seçenekleri, gün değeri ve ayar tanımları.</summary>
public class WorkWeekTests
{
    [Fact]
    public void The_default_week_is_monday_to_friday_nine_to_six()
    {
        var weekly = WorkWeek.Weekly(WorkWeek.DefaultDays, WorkWeek.DefaultHours);

        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            weekly.Select(w => w.Day));
        Assert.All(weekly, w => Assert.Equal((new TimeOnly(9, 0), new TimeOnly(18, 0)), (w.Start, w.End)));
    }

    [Fact]
    public void A_half_saturday_ends_at_one_pm_and_counts_as_half_a_day()
    {
        var weekly = WorkWeek.Weekly(WorkWeek.MondayToFridaySaturdayHalf, "08:30-17:30");

        var saturday = Assert.Single(weekly, w => w.Day == DayOfWeek.Saturday);
        Assert.Equal((new TimeOnly(8, 30), new TimeOnly(13, 0)), (saturday.Start, saturday.End));
        Assert.Equal(0.5m, WorkWeek.DayWorth(weekly, DayOfWeek.Saturday));
        Assert.Equal(1m, WorkWeek.DayWorth(weekly, DayOfWeek.Monday));
        Assert.Equal(0m, WorkWeek.DayWorth(weekly, DayOfWeek.Sunday));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("olmayan", "25:00-26:00")]
    [InlineData("mon-fri", "18:00-09:00")]
    public void Unknown_or_broken_values_fall_back_to_the_default(string? days, string? hours)
    {
        Assert.Equal(WorkWeek.Weekly(WorkWeek.DefaultDays, WorkWeek.DefaultHours), WorkWeek.Weekly(days, hours));
    }

    [Fact]
    public void Every_offered_option_is_valid_and_the_core_settings_offer_them_with_the_defaults()
    {
        // Her saat seçeneği kendi penceresini verir (bozuk seçenek varsayılana düşüp gizlenmesin).
        Assert.All(WorkWeek.HourOptions, o =>
        {
            var monday = WorkWeek.Weekly(WorkWeek.DefaultDays, o.Value)[0];
            Assert.Equal(o.Value, $"{monday.Start:HH\\:mm}-{monday.End:HH\\:mm}");
        });
        Assert.Contains(WorkWeek.HourOptions, o => o.Value == WorkWeek.DefaultHours);
        Assert.Contains(WorkWeek.DayOptions, o => o.Value == WorkWeek.DefaultDays);

        var catalog = new ModuleCatalog([new CoreModule()]);
        var days = catalog.FindSetting(CoreModule.SettingKeys.WorkDays)!.Value.Definition;
        var hours = catalog.FindSetting(CoreModule.SettingKeys.WorkHours)!.Value.Definition;
        Assert.Equal((WorkWeek.DefaultDays, WorkWeek.DefaultHours), (days.DefaultValue, hours.DefaultValue));
        Assert.Equal(WorkWeek.DayOptions.Select(o => o.Value), days.Options!.Select(o => o.Value));
        Assert.Equal(WorkWeek.HourOptions.Select(o => o.Value), hours.Options!.Select(o => o.Value));
    }
}
