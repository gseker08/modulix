using Modulix.Core.Contracts.Calendar;

namespace Modulix.Modules.Support.Sla;

/// <summary>SLA sayacı hesapları: harcanan iş süresi, hedef anı ve eşik anları. Hedef ve eşikler gerçek takvim anlarıdır; işin çalıştığı ana bağlı değildir.</summary>
public static class SlaClock
{
    /// <summary>Çalışan sayaç için hedef anı: çalışmaya başlanan andan, hedefin kalan iş süresi kadar sonrası.</summary>
    public static DateTimeOffset DueAt(WorkCalendar calendar, DateTimeOffset runningSince, TimeSpan target, TimeSpan worked) =>
        calendar.AddWorkingTime(runningSince, Max(TimeSpan.Zero, target - worked));

    /// <summary>Hedefin yüzdesine ulaşılan an (yüzde 100 hedeftir; 100 üstü ihlal sonrasıdır).</summary>
    public static DateTimeOffset ThresholdAt(WorkCalendar calendar, DateTimeOffset runningSince, TimeSpan target, TimeSpan worked, int percent) =>
        calendar.AddWorkingTime(runningSince, Max(TimeSpan.Zero, TimeSpan.FromTicks(target.Ticks * percent / 100) - worked));

    /// <summary>Şu ana kadar harcanan iş süresi: duraklatma öncesi birikmiş süre + çalışan dilim.</summary>
    public static TimeSpan WorkedAt(WorkCalendar calendar, TimeSpan worked, DateTimeOffset? runningSince, DateTimeOffset now) =>
        worked + (runningSince is { } since ? calendar.WorkingTimeBetween(since, now) : TimeSpan.Zero);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
