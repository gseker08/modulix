using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Calendar;
using Modulix.Core.Contracts.Settings;
using Modulix.Core.Modules;

namespace Modulix.Infrastructure.Records;

/// <summary>
/// Firmanın iş takvimi (ADR-0049 dilim 6c): saat dilimi ve haftalık mesai firma ayarlarından, tatiller <see cref="IHolidayCalendar"/>'dan. Ayarlar kayıt boru hattına
/// bağlı olduğu ve bu servis kayıt depolarına verildiği için ayar okuyucusu çağrı anında çözülür (kurucuda alınırsa servis döngüsü oluşur).
/// </summary>
internal sealed class TenantWorkCalendar(IHolidayCalendar holidays, IServiceProvider services) : ITenantWorkCalendar
{
    public async Task<CalendarSpec> GetWeekAsync(CancellationToken cancellationToken)
    {
        var settings = services.GetRequiredService<ITenantSettingReader>();
        var zone = await settings.GetValueAsync(CoreModule.SettingKeys.TimeZone, cancellationToken);
        var days = await settings.GetValueAsync(CoreModule.SettingKeys.WorkDays, cancellationToken);
        var hours = await settings.GetValueAsync(CoreModule.SettingKeys.WorkHours, cancellationToken);
        return new CalendarSpec(zone, false, WorkWeek.Weekly(days, hours), []);
    }

    public async Task<CalendarSpec> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var week = await GetWeekAsync(cancellationToken);
        return TenantHolidays.Apply(week, (await holidays.GetAsync(from, to, cancellationToken)).Values);
    }
}
