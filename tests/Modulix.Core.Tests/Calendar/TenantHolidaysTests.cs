using Modulix.Core.Contracts.Calendar;

namespace Modulix.Core.Tests.Calendar;

/// <summary>ADR-0049 dilim 6b: firmanın tatil listesinin bir iş takvimine eklenmesi (tam gün, yarım gün, takvimin kendi tatilinin önceliği, 7/24).</summary>
public class TenantHolidaysTests
{
    private const string Istanbul = "Europe/Istanbul";

    private static readonly DateOnly Monday = new(2026, 11, 9);
    private static readonly DateOnly Tuesday = new(2026, 11, 10);
    private static readonly DateOnly Saturday = new(2026, 11, 14);

    private static CalendarSpec Weekdays(params CalendarHoliday[] own) =>
        new(Istanbul, false,
            [.. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }.Select(d => new DayHours(d, new(9, 0), new(18, 0)))],
            own);

    /// <summary>Yerel (İstanbul, UTC+3) saatten UTC an.</summary>
    private static DateTimeOffset Local(int day, int hour) => new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(3)).ToUniversalTime();

    [Fact]
    public void A_full_day_closes_the_day_and_a_half_day_stays_open_until_one_pm()
    {
        var spec = TenantHolidays.Apply(Weekdays(), [new HolidayDay(Monday, "Bayram", false), new HolidayDay(Tuesday, "Arife", true)]);

        Assert.Equal(
            [new CalendarHoliday(Monday, "Bayram", null, null), new CalendarHoliday(Tuesday, "Arife", new TimeOnly(9, 0), new TimeOnly(13, 0))],
            spec.Holidays);

        // Cuma 17:00 + 5 iş saati: Cuma 1 saat, Pazartesi kapalı, Salı 09:00-13:00 (4 saat) → Salı 13:00.
        Assert.Equal(Local(10, 13), new WorkCalendar(spec).AddWorkingTime(Local(6, 17), TimeSpan.FromHours(5)));
    }

    [Fact]
    public void The_calendars_own_holiday_wins_on_the_same_day_and_other_days_are_kept()
    {
        var own = new CalendarHoliday(Monday, "Destek nöbeti", new TimeOnly(10, 0), new TimeOnly(12, 0));

        var spec = TenantHolidays.Apply(Weekdays(own), [new HolidayDay(Monday, "Bayram", false), new HolidayDay(Tuesday, "Bayram 2. gün", false)]);

        Assert.Equal([own, new CalendarHoliday(Tuesday, "Bayram 2. gün", null, null)], spec.Holidays);
    }

    [Fact]
    public void A_half_day_without_working_hours_is_closed_and_always_open_calendars_ignore_holidays()
    {
        var weekend = TenantHolidays.Apply(Weekdays(), [new HolidayDay(Saturday, "Arife", true)]);
        Assert.Equal([new CalendarHoliday(Saturday, "Arife", null, null)], weekend.Holidays);

        // Pencere 13:00'te bitiyorsa yarım gün o pencere kadardır; 13:00'ten sonra başlayan gün kapalıdır.
        var morning = new CalendarSpec(Istanbul, false, [new DayHours(DayOfWeek.Monday, new(8, 0), new(12, 0))], []);
        Assert.Equal(new CalendarHoliday(Monday, "Arife", new TimeOnly(8, 0), new TimeOnly(12, 0)),
            Assert.Single(TenantHolidays.Apply(morning, [new HolidayDay(Monday, "Arife", true)]).Holidays));
        var evening = new CalendarSpec(Istanbul, false, [new DayHours(DayOfWeek.Monday, new(14, 0), new(22, 0))], []);
        Assert.Equal(new CalendarHoliday(Monday, "Arife", null, null), Assert.Single(TenantHolidays.Apply(evening, [new HolidayDay(Monday, "Arife", true)]).Holidays));

        Assert.Same(CalendarSpec.TwentyFourSeven, TenantHolidays.Apply(CalendarSpec.TwentyFourSeven, [new HolidayDay(Monday, "Bayram", false)]));
    }
}
