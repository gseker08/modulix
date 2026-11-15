using Modulix.Core.Contracts.Calendar;

namespace Modulix.Modules.Hr.Records;

/// <summary>
/// İzin gün sayısı (ADR-0042 dilim 2): başlangıç ve bitiş dahil; firmanın çalışma günleri sayılır, çalışılmayan günler ve firmanın resmî tatil listesindeki
/// günler düşülür (yarım gün tatil yarım gün düşer). Çalışma günleri ve tatil listesi Core'dadır (ADR-0049 dilim 6a ve 6c: <see cref="ITenantWorkCalendar"/>,
/// <see cref="IHolidayCalendar"/>); varsayılan Pazartesi–Cuma'dır. Yarım çalışma günü (ör. Cumartesi 13:00'e kadar) yarım gün sayılır. Yarım günlük talep
/// tek günlüktür ve iş gününe denk geliyorsa en çok 0,5 gün sayılır. Cumartesinin iş günü sayılıp sayılmayacağı firmanın ayarıdır; mevzuat doğrulaması
/// ADR-0042 bölüm 8'dedir.
/// </summary>
internal static class LeaveCalendar
{
    /// <summary>Bir talepte en çok bu kadar takvim günü (bir yıl) sayılır.</summary>
    public const int MaxSpanDays = 366;

    /// <summary>Gün sayısı: çalışılmayan günler ve firmanın tatil listesi düşülür. Bir yıldan uzun ya da ters aralık 0 gündür.</summary>
    public static async Task<decimal> DaysAsync(HrStoreContext context, DateOnly start, DateOnly end, bool halfDay, CancellationToken cancellationToken)
    {
        if (end < start || end.DayNumber - start.DayNumber >= MaxSpanDays)
        {
            return 0m;
        }

        var week = await context.WorkCalendar.GetWeekAsync(cancellationToken);
        var holidays = await context.Holidays.GetAsync(start, end, cancellationToken);
        return WorkingDays(start, end, halfDay, holidays.ToDictionary(h => h.Key, h => h.Value.HalfDay), DayWorths(week.Weekly));
    }

    /// <summary>Haftanın her gününün gün sayımındaki değeri (0, 0,5 ya da 1).</summary>
    public static IReadOnlyDictionary<DayOfWeek, decimal> DayWorths(IReadOnlyList<DayHours> weekly) =>
        Enum.GetValues<DayOfWeek>().ToDictionary(d => d, d => WorkWeek.DayWorth(weekly, d));

    /// <summary>Pazartesi–Cuma (varsayılan çalışma haftası).</summary>
    public static IReadOnlyDictionary<DayOfWeek, decimal> MondayToFriday { get; } = DayWorths(WorkWeek.Weekly(WorkWeek.MondayToFriday, WorkWeek.DefaultHours));

    /// <param name="holidays">Tatil günleri → yarım gün mü.</param>
    /// <param name="dayWorths">Haftanın günlerinin değeri; verilmezse Pazartesi–Cuma.</param>
    public static decimal WorkingDays(
        DateOnly start, DateOnly end, bool halfDay, IReadOnlyDictionary<DateOnly, bool> holidays, IReadOnlyDictionary<DayOfWeek, decimal>? dayWorths = null)
    {
        if (end < start)
        {
            return 0m;
        }

        dayWorths ??= MondayToFriday;
        var total = 0m;
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var worth = dayWorths.GetValueOrDefault(day.DayOfWeek);
            if (worth <= 0)
            {
                continue;
            }

            if (holidays.TryGetValue(day, out var halfHoliday))
            {
                worth = halfHoliday ? Math.Min(worth, 0.5m) : 0m;
            }

            total += halfDay ? Math.Min(worth, 0.5m) : worth;
        }

        return total;
    }
}
