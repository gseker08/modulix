namespace Modulix.Modules.Hr.Records;

/// <summary>
/// İzin gün sayısı (ADR-0042 dilim 2): başlangıç ve bitiş dahil, cumartesi ve pazar sayılmaz, firmanın resmî tatil listesindeki günler düşülür
/// (liste Core'dadır, ADR-0049 dilim 6a: <see cref="Modulix.Core.Contracts.Calendar.IHolidayCalendar"/>)
/// (yarım gün tatil yarım gün düşer). Yarım günlük talep tek günlüktür ve iş gününe denk geliyorsa 0,5 gün sayılır. Cumartesinin iş günü
/// sayılıp sayılmayacağı mevzuat doğrulaması bekler (ADR-0042 bölüm 8); bugün hafta sonu sabit cumartesi–pazardır.
/// </summary>
internal static class LeaveCalendar
{
    /// <summary>Bir talepte en çok bu kadar takvim günü (bir yıl) sayılır.</summary>
    public const int MaxSpanDays = 366;

    /// <summary>Gün sayısı: hafta sonu ve firmanın tatil listesi düşülür. Bir yıldan uzun ya da ters aralık 0 gündür.</summary>
    public static async Task<decimal> DaysAsync(HrStoreContext context, DateOnly start, DateOnly end, bool halfDay, CancellationToken cancellationToken)
    {
        if (end < start || end.DayNumber - start.DayNumber >= MaxSpanDays)
        {
            return 0m;
        }

        var holidays = await context.Holidays.GetAsync(start, end, cancellationToken);
        return WorkingDays(start, end, halfDay, holidays.ToDictionary(h => h.Key, h => h.Value.HalfDay));
    }

    public static bool IsWeekend(DateOnly day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <param name="holidays">Tatil günleri → yarım gün mü.</param>
    public static decimal WorkingDays(DateOnly start, DateOnly end, bool halfDay, IReadOnlyDictionary<DateOnly, bool> holidays)
    {
        if (end < start)
        {
            return 0m;
        }

        var total = 0m;
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (IsWeekend(day))
            {
                continue;
            }

            var worth = holidays.TryGetValue(day, out var halfHoliday) ? (halfHoliday ? 0.5m : 0m) : 1m;
            total += halfDay ? Math.Min(worth, 0.5m) : worth;
        }

        return total;
    }
}
