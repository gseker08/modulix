using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Modules;
using Modulix.Core.Records;
using Modulix.Core.Reporting;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Reporting;

/// <summary>
/// Kullanıcı panoları (CRM epik E2, dilim 1). Pano ve bileşenleri tenant verisidir (query filter + RLS) ve yalnızca sahibine görünür.
/// Bileşen bir kayıtlı rapora bağlanır; raporun görünürlüğü ve verisi her seferinde <see cref="IReportCenter"/> üzerinden, çağıranın
/// yetkisiyle sorulur: pano veri erişimi vermez. Her yazma <c>core.dashboard</c> kaydı olarak denetime yazılır.
/// </summary>
internal sealed class Dashboards(
    ModulixDbContext db,
    ITenantContextAccessor tenantAccessor,
    IAuditWriter audit,
    IReportCenter reports,
    TimeProvider time) : IDashboards
{
    public async Task<IReadOnlyList<DashboardSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var me = RequireTenant().UserId;
        return await db.Dashboards.AsNoTracking()
            .Where(d => d.OwnerUserId == me)
            .OrderBy(d => d.Name).ThenBy(d => d.Id)
            .Select(d => new DashboardSummary(d.Id, d.Name, db.DashboardWidgets.Count(w => w.DashboardId == d.Id), d.UpdatedAt, d.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task<DashboardDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var me = RequireTenant().UserId;
        var dashboard = await db.Dashboards.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.OwnerUserId == me, cancellationToken);
        return dashboard is null ? null : await DetailAsync(dashboard, cancellationToken);
    }

    public async Task<DashboardDetail> CreateAsync(DashboardInput input, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        var name = NormalizeName(input.Name);
        var widgets = await ValidatedAsync(input.Widgets, cancellationToken);

        if (await db.Dashboards.CountAsync(d => d.OwnerUserId == tenant.UserId, cancellationToken) >= Dashboard.MaxPerUser)
        {
            throw new DashboardException($"En fazla {Dashboard.MaxPerUser} pano oluşturabilirsiniz.");
        }

        await EnsureNameFreeAsync(tenant.UserId, name, null, cancellationToken);
        var dashboard = Dashboard.Create(tenant.UserId, name, time.GetUtcNow());
        db.Dashboards.Add(dashboard);
        db.DashboardWidgets.AddRange(widgets.Select((w, index) => new DashboardWidget(dashboard.Id, w.Input.ReportId, w.Input.Kind, w.Input.Title, index)));
        Audit(tenant, dashboard.Id, AuditAction.Created, null, Snapshot(name, widgets));
        await SaveAsync(cancellationToken);
        return await DetailAsync(dashboard, cancellationToken);
    }

    public async Task<DashboardDetail?> UpdateAsync(Guid id, DashboardInput input, long? expectedVersion, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        var dashboard = await db.Dashboards.SingleOrDefaultAsync(d => d.Id == id && d.OwnerUserId == tenant.UserId, cancellationToken);
        if (dashboard is null)
        {
            return null;
        }

        if (expectedVersion is { } expected && expected != dashboard.Version)
        {
            throw new DashboardConflictException("Pano başka bir sekmede değiştirildi; sayfayı yenileyip tekrar deneyin.");
        }

        var name = NormalizeName(input.Name);
        var widgets = await ValidatedAsync(input.Widgets, cancellationToken);
        await EnsureNameFreeAsync(tenant.UserId, name, dashboard.Id, cancellationToken);

        var current = await db.DashboardWidgets.Where(w => w.DashboardId == dashboard.Id).OrderBy(w => w.Position).ToListAsync(cancellationToken);
        var before = Snapshot(dashboard.Name, await DescribeAsync(current, cancellationToken));

        // Bileşen listesi bütün olarak değişir: sıra listedeki sıradır, bileşen kimlikleri yenilenir.
        db.DashboardWidgets.RemoveRange(current);
        db.DashboardWidgets.AddRange(widgets.Select((w, index) => new DashboardWidget(dashboard.Id, w.Input.ReportId, w.Input.Kind, w.Input.Title, index)));
        dashboard.Update(name, time.GetUtcNow());
        Audit(tenant, dashboard.Id, AuditAction.Updated, before, Snapshot(name, widgets));
        await SaveAsync(cancellationToken);
        return await DetailAsync(dashboard, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        var dashboard = await db.Dashboards.SingleOrDefaultAsync(d => d.Id == id && d.OwnerUserId == tenant.UserId, cancellationToken);
        if (dashboard is null)
        {
            return false;
        }

        var current = await db.DashboardWidgets.Where(w => w.DashboardId == dashboard.Id).OrderBy(w => w.Position).ToListAsync(cancellationToken);
        Audit(tenant, dashboard.Id, AuditAction.Deleted, Snapshot(dashboard.Name, await DescribeAsync(current, cancellationToken)), null);
        db.Dashboards.Remove(dashboard);
        await SaveAsync(cancellationToken);
        return true;
    }

    public async Task<ReportResult?> RunWidgetAsync(Guid id, Guid widgetId, CancellationToken cancellationToken)
    {
        var me = RequireTenant().UserId;
        var widget = await (
            from w in db.DashboardWidgets.AsNoTracking()
            join d in db.Dashboards.AsNoTracking() on w.DashboardId equals d.Id
            where w.Id == widgetId && d.Id == id && d.OwnerUserId == me
            select w).SingleOrDefaultAsync(cancellationToken);
        if (widget is null)
        {
            return null;
        }

        // Özet raporun bütün grupları tek sayfada; tablo raporunda yalnızca sayı gerekir.
        return await reports.RunAsync(widget.ReportId, null, 1, ReportLimits.MaxPageSize, cancellationToken);
    }

    // ---- Doğrulama

    private static string NormalizeName(string? name)
    {
        try
        {
            return Dashboard.NormalizeName(name);
        }
        catch (ArgumentException ex)
        {
            throw new DashboardException(Clean(ex));
        }
    }

    private async Task<List<(DashboardWidgetInput Input, string? ReportName)>> ValidatedAsync(
        IReadOnlyList<DashboardWidgetInput>? widgets, CancellationToken cancellationToken)
    {
        widgets ??= [];
        if (widgets.Count > Dashboard.MaxWidgets)
        {
            throw new DashboardException($"Bir panoda en fazla {Dashboard.MaxWidgets} bileşen olabilir.", "widgets");
        }

        var result = new List<(DashboardWidgetInput Input, string? ReportName)>();
        var known = new Dictionary<Guid, ReportDetail?>();
        foreach (var widget in widgets)
        {
            if (!Enum.IsDefined(widget.Kind))
            {
                throw new DashboardException("Bileşen türü geçersiz.", "widgets");
            }

            string? title;
            try
            {
                title = DashboardWidget.NormalizeTitle(widget.Title);
            }
            catch (ArgumentException ex)
            {
                throw new DashboardException(Clean(ex), "widgets");
            }

            // Rapor çağıranın görebildiği bir rapor olmalı; göremediği rapor "yok" sayılır.
            if (!known.TryGetValue(widget.ReportId, out var report))
            {
                known[widget.ReportId] = report = await reports.GetAsync(widget.ReportId, cancellationToken);
            }

            if (report is null)
            {
                throw new DashboardException("Bileşenin raporu bulunamadı.", "widgets");
            }

            if (widget.Kind == DashboardWidgetKind.Bar && report.Spec.Groups.Count == 0)
            {
                throw new DashboardException($"'{report.Summary.Name}' gruplanmamış bir rapor; çubuk grafik için gruplu bir rapor seçin.", "widgets");
            }

            result.Add((widget with { Title = title }, report.Summary.Name));
        }

        return result;
    }

    private async Task EnsureNameFreeAsync(Guid owner, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await db.Dashboards.AnyAsync(d => d.OwnerUserId == owner && d.Name == name && d.Id != exceptId, cancellationToken))
        {
            throw new DashboardException("Bu adla bir panonuz zaten var.");
        }
    }

    private static string Clean(ArgumentException ex) => ex.Message.Split(" (Parameter", StringSplitOptions.None)[0];

    // ---- Okuma

    private async Task<DashboardDetail> DetailAsync(Dashboard dashboard, CancellationToken cancellationToken)
    {
        var widgets = await db.DashboardWidgets.AsNoTracking()
            .Where(w => w.DashboardId == dashboard.Id)
            .OrderBy(w => w.Position).ThenBy(w => w.Id)
            .ToListAsync(cancellationToken);
        var described = await DescribeAsync(widgets, cancellationToken);
        return new DashboardDetail(
            new DashboardSummary(dashboard.Id, dashboard.Name, widgets.Count, dashboard.UpdatedAt, dashboard.Version),
            [.. widgets.Select((w, index) => new DashboardWidgetView(w.Id, w.ReportId, described[index].ReportName, w.Kind, w.Title, w.Position))]);
    }

    /// <summary>Bileşenlerin rapor adları; çağıranın artık göremediği raporun adı boş gelir.</summary>
    private async Task<List<(DashboardWidgetInput Input, string? ReportName)>> DescribeAsync(
        IReadOnlyList<DashboardWidget> widgets, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var reportId in widgets.Select(w => w.ReportId).Distinct())
        {
            names[reportId] = (await reports.GetAsync(reportId, cancellationToken))?.Summary.Name;
        }

        return [.. widgets.Select(w => (new DashboardWidgetInput(w.ReportId, w.Kind, w.Title), names[w.ReportId]))];
    }

    // ---- Denetim ve kayıt

    private static JsonObject Snapshot(string name, IEnumerable<(DashboardWidgetInput Input, string? ReportName)> widgets) =>
        new()
        {
            ["name"] = name,
            ["widgets"] = string.Join("; ", widgets.Select(w =>
                $"{w.Input.Title ?? w.ReportName ?? "(rapor görünmüyor)"} ({(w.Input.Kind == DashboardWidgetKind.Bar ? "çubuk" : "sayı")})")),
        };

    private void Audit(TenantContext tenant, Guid dashboardId, AuditAction action, JsonObject? before, JsonObject? after)
    {
        var changes = RecordDiff.Compute(before, after);
        if (action == AuditAction.Updated && changes.Count == 0)
        {
            return;
        }

        audit.Add(new AuditEntry(tenant.TenantId, tenant.UserId, CoreModule.AuditedEntityKeys.Dashboard, dashboardId, action, changes, ExecutionSource.Api, null));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DashboardConflictException("Pano başka bir sekmede değiştirildi; sayfayı yenileyip tekrar deneyin.");
        }
        catch (DbUpdateException)
        {
            // En olası neden eşzamanlı aynı ad ya da bu sırada silinen rapor.
            throw new DashboardConflictException("Pano kaydedilemedi: aynı ad ya da silinen bir rapor başka bir işlemle çakıştı. Tekrar deneyin.");
        }
    }

    private TenantContext RequireTenant() =>
        tenantAccessor.Current ?? throw new InvalidOperationException("Aktif tenant olmadan panolar yönetilemez.");
}
