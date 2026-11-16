namespace Modulix.Core.Contracts.Calendar;

/// <summary>Çekirdeğin takvim kayıt türleri (ADR-0049 dilim 6a).</summary>
public static class CalendarEntities
{
    /// <summary>Firmanın resmî tatil ve idari izin günleri: <c>/api/records/core.holiday</c>.</summary>
    public const string Holiday = "core.holiday";
}

/// <summary>Firmanın tatil listesindeki bir gün. Yarım gün (arife) çalışma gününün yarısı sayılır.</summary>
public sealed record HolidayDay(DateOnly Date, string Name, bool HalfDay);

/// <summary>
/// Firmanın resmî tatil listesi (ADR-0049 dilim 6a): bir kez girilir, izin gün sayısı, SLA, onay eskalasyonu ve teslim süresi aynı listeyi kullanır.
/// Liste tenant verisidir; okuma için izin gerekmez (yalnızca aktif tenant). Tarihler koda yazılmaz: dinî bayramlar her yıl değişir.
/// </summary>
public interface IHolidayCalendar
{
    /// <summary>İki tarih arasındaki (ikisi de dahil) tatil günleri, tarihe göre. Ters aralık boştur.</summary>
    Task<IReadOnlyDictionary<DateOnly, HolidayDay>> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
