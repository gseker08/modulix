namespace Modulix.Core.Contracts.Calendar;

/// <summary>
/// Firmanın iş takvimi (ADR-0049 dilim 6c): saat dilimi (bölgesel ayar), haftalık mesai (çalışma takvimi ayarları) ve resmî tatil listesi tek takvimde.
/// İzin gün sayısı ve iş saatiyle sayılan onay adımı süresi (dilim 6d) bu takvimi kullanır, ileride teslim süresi de; kendi takvimi olan özellik (ör. destek SLA takvimi) onu kullanmaya devam eder.
/// </summary>
public interface ITenantWorkCalendar
{
    /// <summary>Saat dilimi ve haftalık mesai; tatil günleri olmadan.</summary>
    Task<CalendarSpec> GetWeekAsync(CancellationToken cancellationToken);

    /// <summary>Haftalık mesai ve iki tarih arasındaki (ikisi de dahil) tatil günleri (<see cref="TenantHolidays.Apply"/>).</summary>
    Task<CalendarSpec> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>
/// Firmanın haftalık mesai seçenekleri (ADR-0049 dilim 6c). Ayar ekranı seçenekli ayar gösterir; değerler burada çözülür. Yarım gün (ör. Cumartesi
/// 09:00–13:00) gün sayımında yarım gün sayılır (<see cref="DayWorth"/>). Seçenek dışı ya da bozuk değer varsayılana düşer.
/// </summary>
public static class WorkWeek
{
    public const string MondayToFriday = "mon-fri";
    public const string MondayToSaturday = "mon-sat";
    public const string MondayToFridaySaturdayHalf = "mon-fri-sat-half";
    public const string SundayToThursday = "sun-thu";
    public const string DefaultDays = MondayToFriday;

    public const string DefaultHours = "09:00-18:00";

    /// <summary>Çalışma günleri seçenekleri: değer → görünen ad.</summary>
    public static IReadOnlyList<(string Value, string Label)> DayOptions { get; } =
    [
        (MondayToFriday, "Pazartesi–Cuma"),
        (MondayToFridaySaturdayHalf, "Pazartesi–Cuma, Cumartesi yarım gün (13:00'e kadar)"),
        (MondayToSaturday, "Pazartesi–Cumartesi"),
        (SundayToThursday, "Pazar–Perşembe"),
    ];

    /// <summary>Mesai saatleri seçenekleri (yerel saat): değer → görünen ad.</summary>
    public static IReadOnlyList<(string Value, string Label)> HourOptions { get; } =
    [
        ("08:00-17:00", "08:00–17:00"),
        ("08:30-17:30", "08:30–17:30"),
        ("08:30-18:00", "08:30–18:00"),
        (DefaultHours, "09:00–18:00"),
        ("09:00-17:00", "09:00–17:00"),
        ("08:00-18:00", "08:00–18:00"),
    ];

    /// <summary>Seçeneklerden haftalık çalışma pencereleri. Yarım gün, mesai başlangıcından <see cref="TenantHolidays.HalfDayEnd"/>'e kadardır.</summary>
    public static IReadOnlyList<DayHours> Weekly(string? days, string? hours)
    {
        var (start, end) = ParseHours(hours) ?? ParseHours(DefaultHours)!.Value;
        DayOfWeek[] full = days switch
        {
            MondayToSaturday => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday],
            SundayToThursday => [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday],
            _ => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        };

        var weekly = full.Select(d => new DayHours(d, start, end)).ToList();
        if (days == MondayToFridaySaturdayHalf && start < TenantHolidays.HalfDayEnd)
        {
            weekly.Add(new DayHours(DayOfWeek.Saturday, start, end < TenantHolidays.HalfDayEnd ? end : TenantHolidays.HalfDayEnd));
        }

        return weekly;
    }

    /// <summary>
    /// Bir günün gün sayımındaki değeri: çalışma penceresi yoksa 0; toplam süresi haftanın en uzun gününün %60'ından azsa (yarım gün) 0,5; değilse 1.
    /// </summary>
    public static decimal DayWorth(IReadOnlyList<DayHours> weekly, DayOfWeek day)
    {
        var longest = weekly.GroupBy(w => w.Day).Select(g => g.Sum(w => (w.End - w.Start).TotalMinutes)).DefaultIfEmpty(0).Max();
        var total = weekly.Where(w => w.Day == day).Sum(w => (w.End - w.Start).TotalMinutes);
        return total <= 0 ? 0m : total < longest * 0.6 ? 0.5m : 1m;
    }

    private static (TimeOnly Start, TimeOnly End)? ParseHours(string? value)
    {
        var parts = value?.Split('-');
        return parts is { Length: 2 }
            && TimeOnly.TryParseExact(parts[0], "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var start)
            && TimeOnly.TryParseExact(parts[1], "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var end)
            && end > start
                ? (start, end)
                : null;
    }
}
