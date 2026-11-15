using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Numbering;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Modules;
using Modulix.Core.Numbering;
using Modulix.Core.Records;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Numbering;

/// <summary>
/// Belge serilerinin firma yönetimi (ADR-0049 dilim 2a). Yapılandırma <see cref="NumberSeriesSetting"/>'e yazılır; numaralama servisi her numarada
/// okur. Sayacı ileri alma tek SQL ifadesiyle ve yalnızca ileriye yapılır (aynı anda verilen numarayla yarışmaz). Her değişiklik denetim kaydına girer.
/// </summary>
internal sealed class NumberSeriesAdmin(
    ModulixDbContext db, ModuleCatalog modules, ITenantContextAccessor tenantAccessor, ITenantClock clock, IAuditWriter audit, TimeProvider time) : INumberSeriesAdmin
{
    public async Task<IReadOnlyList<NumberSeriesView>> ListAsync(CancellationToken cancellationToken)
    {
        int year = (await clock.GetAsync(cancellationToken)).Today.Year;
        Dictionary<string, NumberSeriesSetting> settings = await db.NumberSeriesSettings.AsNoTracking().ToDictionaryAsync(s => s.SeriesKey, StringComparer.Ordinal, cancellationToken);
        List<NumberCounter> counters = await db.NumberCounters.AsNoTracking().ToListAsync(cancellationToken);
        return [.. modules.NumberSeries.Select(s => View(s.ModuleKey, s.Series, settings.GetValueOrDefault(s.Series.Key), counters, year))];
    }

    public async Task<NumberSeriesView?> ConfigureAsync(
        string seriesKey, string? prefix, int digits, NumberSeriesReset reset, long expectedVersion, CancellationToken cancellationToken)
    {
        if (Find(seriesKey) is not { } found)
        {
            return null;
        }

        TenantContext tenant = RequireTenant();
        NumberSeriesSetting setting = await LoadAsync(seriesKey, expectedVersion, tenant, cancellationToken);
        JsonObject before = Snapshot(found.Series, setting);
        try
        {
            setting.Configure(found.Series, prefix, digits, reset, tenant.UserId, time.GetUtcNow());
        }
        catch (ArgumentException ex)
        {
            throw new NumberSeriesException(ex.Message);
        }

        AddAudit(tenant, setting, before, Snapshot(found.Series, setting));
        await SaveAsync(cancellationToken);
        return await ViewAsync(found.ModuleKey, found.Series, setting, cancellationToken);
    }

    public async Task<NumberSeriesView?> AdvanceAsync(string seriesKey, long nextValue, long expectedVersion, CancellationToken cancellationToken)
    {
        if (Find(seriesKey) is not { } found)
        {
            return null;
        }

        TenantContext tenant = RequireTenant();
        NumberSeriesSetting setting = await LoadAsync(seriesKey, expectedVersion, tenant, cancellationToken);
        NumberSeriesDefinition series = setting.Apply(found.Series);
        int year = (await clock.GetAsync(cancellationToken)).Today.Year;
        int period = series.PeriodOf(year);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        NumberCounter? counter = await db.NumberCounters.AsNoTracking()
            .SingleOrDefaultAsync(c => c.SeriesKey == seriesKey && c.Period == period, cancellationToken);
        if (counter is null)
        {
            throw new NumberSeriesException("Bu dönemde henüz numara verilmedi; ilk belge açıldıktan sonra sayaç ileri alınabilir.");
        }

        // Yalnızca ileri: koşul ifadenin içinde, aynı anda verilen numara değeri geçmişse güncelleme olmaz.
        int updated = await db.NumberCounters
            .Where(c => c.SeriesKey == seriesKey && c.Period == period && c.LastValue < nextValue - 1)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.LastValue, nextValue - 1).SetProperty(c => c.UpdatedAt, time.GetUtcNow()), cancellationToken);
        if (updated == 0)
        {
            throw new NumberSeriesException($"Sıradaki numara yalnızca ileri alınabilir; şu anki sıradaki sıra {counter.LastValue + 1}.");
        }

        JsonObject before = new() { ["series"] = found.Series.DisplayName, ["nextNumber"] = series.Format(counter.LastValue + 1, year) };
        setting.Touch(tenant.UserId, time.GetUtcNow());
        AddAudit(tenant, setting, before, new JsonObject { ["series"] = found.Series.DisplayName, ["nextNumber"] = series.Format(nextValue, year) });
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ViewAsync(found.ModuleKey, found.Series, setting, cancellationToken);
    }

    private (string ModuleKey, NumberSeriesDefinition Series)? Find(string seriesKey)
    {
        foreach ((string ModuleKey, NumberSeriesDefinition Series) entry in modules.NumberSeries)
        {
            if (entry.Series.Key == seriesKey)
            {
                return entry;
            }
        }

        return null;
    }

    private TenantContext RequireTenant() =>
        tenantAccessor.Current ?? throw new InvalidOperationException("Belge serisi tenant bağlamı olmadan yönetilemez.");

    // Firmanın ayar satırı; yoksa açılır (sürüm 0). Gönderilen sürüm satırınkiyle aynı olmalı.
    private async Task<NumberSeriesSetting> LoadAsync(string seriesKey, long expectedVersion, TenantContext tenant, CancellationToken cancellationToken)
    {
        NumberSeriesSetting? setting = await db.NumberSeriesSettings.SingleOrDefaultAsync(s => s.SeriesKey == seriesKey, cancellationToken);
        if ((setting?.Version ?? 0) != expectedVersion)
        {
            throw new NumberSeriesConflictException("Seri başkası tarafından değiştirildi; sayfayı yenileyip tekrar deneyin.");
        }

        if (setting is null)
        {
            setting = NumberSeriesSetting.Create(seriesKey, tenant.UserId, time.GetUtcNow());
            db.NumberSeriesSettings.Add(setting);
        }

        return setting;
    }

    private async Task<NumberSeriesView> ViewAsync(string moduleKey, NumberSeriesDefinition declared, NumberSeriesSetting setting, CancellationToken cancellationToken)
    {
        int year = (await clock.GetAsync(cancellationToken)).Today.Year;
        List<NumberCounter> counters = await db.NumberCounters.AsNoTracking().Where(c => c.SeriesKey == declared.Key).ToListAsync(cancellationToken);
        return View(moduleKey, declared, setting, counters, year);
    }

    private static NumberSeriesView View(string moduleKey, NumberSeriesDefinition declared, NumberSeriesSetting? setting, List<NumberCounter> counters, int year)
    {
        NumberSeriesDefinition effective = setting?.Apply(declared) ?? declared;
        int period = effective.PeriodOf(year);
        long last = counters.FirstOrDefault(c => c.SeriesKey == declared.Key && c.Period == period)?.LastValue ?? 0;
        return new NumberSeriesView(moduleKey, declared, effective, setting?.IsCustomized ?? false, year, last, effective.Format(last + 1, year), setting?.Version ?? 0);
    }

    private static JsonObject Snapshot(NumberSeriesDefinition declared, NumberSeriesSetting setting)
    {
        NumberSeriesDefinition effective = setting.Apply(declared);
        return new JsonObject
        {
            ["series"] = declared.DisplayName,
            ["prefix"] = effective.Prefix,
            ["digits"] = effective.Digits,
            ["reset"] = effective.Reset.ToString(),
        };
    }

    private void AddAudit(TenantContext tenant, NumberSeriesSetting setting, JsonObject before, JsonObject after)
    {
        JsonObject changes = RecordDiff.Compute(before, after);
        if (changes.Count == 0)
        {
            return;
        }

        audit.Add(new AuditEntry(
            tenant.TenantId, tenant.UserId, CoreModule.AuditedEntityKeys.NumberSeries, setting.Id, AuditAction.Updated, changes,
            ExecutionSource.Api, Activity.Current?.TraceId.ToString()));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Sürüm belirteci ya da aynı anda açılan ilk ayar satırı (seri başına tekil dizin).
            throw new NumberSeriesConflictException("Seri başkası tarafından değiştirildi; sayfayı yenileyip tekrar deneyin.");
        }
    }
}
