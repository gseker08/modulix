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
/// Kullanıcı panoları (CRM epik E2). Pano, bileşenleri ve paylaşımları tenant verisidir (query filter + RLS). Görünürlük her okumada yeniden
/// hesaplanır (sahibi, firmaya açık, kendisiyle paylaşılan); paylaşım kaldırılınca erişim hemen biter. Yalnızca sahibi değiştirir.
/// Bileşen bir kayıtlı rapora bağlanır; raporun görünürlüğü ve verisi her seferinde <see cref="IReportCenter"/> üzerinden, çağıranın
/// yetkisiyle sorulur: pano veri erişimi vermez. Her yazma <c>core.dashboard</c> kaydı olarak denetime yazılır.
/// </summary>
internal sealed class Dashboards(
    ModulixDbContext db,
    ITenantContextAccessor tenantAccessor,
    IAuditWriter audit,
    IReportCenter reports,
    ITenantMembers members,
    TimeProvider time) : IDashboards
{
    public async Task<IReadOnlyList<DashboardSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var me = RequireTenant().UserId;
        var items = await Visible(me).AsNoTracking().OrderBy(d => d.Name).ThenBy(d => d.Id).ToListAsync(cancellationToken);
        var summaries = await SummariesAsync(items, me, cancellationToken);
        // Varsayılan pano başta; kalanlar ada göre.
        return [.. summaries.OrderByDescending(s => s.IsDefault)];
    }

    public async Task<DashboardDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var me = RequireTenant().UserId;
        var dashboard = await Visible(me).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        return dashboard is null ? null : await DetailAsync(dashboard, me, cancellationToken);
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
        Audit(tenant, dashboard.Id, AuditAction.Created, null, Snapshot(dashboard, name, widgets, []));
        await SaveAsync(cancellationToken);
        return await DetailAsync(dashboard, tenant.UserId, cancellationToken);
    }

    public async Task<DashboardDetail?> UpdateAsync(Guid id, DashboardInput input, long? expectedVersion, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        var dashboard = await Visible(tenant.UserId).SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dashboard is null)
        {
            return null;
        }

        RequireOwner(dashboard, tenant.UserId);
        CheckVersion(dashboard, expectedVersion);

        var name = NormalizeName(input.Name);
        var widgets = await ValidatedAsync(input.Widgets, cancellationToken);
        await EnsureNameFreeAsync(tenant.UserId, name, dashboard.Id, cancellationToken);

        var current = await db.DashboardWidgets.Where(w => w.DashboardId == dashboard.Id).OrderBy(w => w.Position).ToListAsync(cancellationToken);
        var shared = await SharedWithAsync(dashboard.Id, cancellationToken);
        var before = Snapshot(dashboard, dashboard.Name, await DescribeAsync(current, cancellationToken), shared);

        // Bileşen listesi bütün olarak değişir: sıra listedeki sıradır, bileşen kimlikleri yenilenir.
        db.DashboardWidgets.RemoveRange(current);
        db.DashboardWidgets.AddRange(widgets.Select((w, index) => new DashboardWidget(dashboard.Id, w.Input.ReportId, w.Input.Kind, w.Input.Title, index)));
        dashboard.Update(name, time.GetUtcNow());
        Audit(tenant, dashboard.Id, AuditAction.Updated, before, Snapshot(dashboard, name, widgets, shared));
        await SaveAsync(cancellationToken);
        return await DetailAsync(dashboard, tenant.UserId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        var dashboard = await Visible(tenant.UserId).SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dashboard is null)
        {
            return false;
        }

        RequireOwner(dashboard, tenant.UserId);
        var current = await db.DashboardWidgets.Where(w => w.DashboardId == dashboard.Id).OrderBy(w => w.Position).ToListAsync(cancellationToken);
        Audit(
            tenant, dashboard.Id, AuditAction.Deleted,
            Snapshot(dashboard, dashboard.Name, await DescribeAsync(current, cancellationToken), await SharedWithAsync(dashboard.Id, cancellationToken)), null);
        db.Dashboards.Remove(dashboard);
        await SaveAsync(cancellationToken);
        return true;
    }

    public async Task<DashboardDetail?> ShareAsync(
        Guid id, ReportVisibility visibility, IReadOnlyList<Guid> users, IReadOnlyList<ShareGroup> groups, long? expectedVersion, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        var dashboard = await Visible(tenant.UserId).SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dashboard is null)
        {
            return null;
        }

        RequireOwner(dashboard, tenant.UserId);
        CheckVersion(dashboard, expectedVersion);
        if (!Enum.IsDefined(visibility))
        {
            throw new DashboardException("Paylaşım türü geçersiz.", "visibility");
        }

        var wanted = visibility == ReportVisibility.Users ? users.Where(u => u != tenant.UserId).Distinct().ToList() : [];
        var wantedGroups = visibility == ReportVisibility.Users
            ? await ShareAudience.NormalizeAsync(db, groups, message => new DashboardException(message, "groups"), cancellationToken)
            : [];
        if (visibility == ReportVisibility.Users)
        {
            if (wanted.Count == 0 && wantedGroups.Count == 0)
            {
                throw new DashboardException("Paylaşmak için en az bir kullanıcı ya da grup seçin.", "users");
            }

            var active = (await members.ListActiveAsync(cancellationToken)).Select(m => m.UserId).ToHashSet();
            if (wanted.Any(u => !active.Contains(u)))
            {
                throw new DashboardException("Seçilen kullanıcılardan biri firmanın etkin üyesi değil.", "users");
            }
        }

        var widgets = await DescribeAsync(
            await db.DashboardWidgets.AsNoTracking().Where(w => w.DashboardId == dashboard.Id).OrderBy(w => w.Position).ToListAsync(cancellationToken),
            cancellationToken);
        var current = await db.DashboardShares.Where(s => s.DashboardId == dashboard.Id).ToListAsync(cancellationToken);
        var before = Snapshot(
            dashboard, dashboard.Name, widgets,
            [.. current.Where(s => s.TargetKind == ShareTargetKind.User).Select(s => s.UserId)],
            [.. current.Where(s => s.TargetKind != ShareTargetKind.User).Select(s => new ShareGroup(s.TargetKind, s.UserId))]);
        var now = time.GetUtcNow();
        db.DashboardShares.RemoveRange(current.Where(s => s.TargetKind == ShareTargetKind.User
            ? !wanted.Contains(s.UserId)
            : !wantedGroups.Contains(new ShareGroup(s.TargetKind, s.UserId))));
        db.DashboardShares.AddRange(wanted
            .Where(u => !current.Any(s => s.TargetKind == ShareTargetKind.User && s.UserId == u))
            .Select(u => new DashboardShare(dashboard.Id, u, tenant.UserId, now)));
        db.DashboardShares.AddRange(wantedGroups
            .Where(g => !current.Any(s => s.TargetKind == g.Kind && s.UserId == g.Id))
            .Select(g => new DashboardShare(dashboard.Id, g.Kind, g.Id, tenant.UserId, now)));

        // Erişimi biten kullanıcının varsayılan işareti kalkar: pano sonradan yeniden paylaşılırsa kendiliğinden varsayılan olmaz.
        // Grupla erişen kullanıcılar (rol, ekip, iş birimi üyeleri) erişimini koruduğu için işaretleri kalır.
        if (visibility != ReportVisibility.Tenant)
        {
            var keep = wanted.Append(tenant.UserId).Concat(await ShareAudience.UserIdsAsync(db, wantedGroups, cancellationToken)).ToList();
            db.DashboardDefaults.RemoveRange(
                await db.DashboardDefaults.Where(x => x.DashboardId == dashboard.Id && !keep.Contains(x.UserId)).ToListAsync(cancellationToken));
        }

        dashboard.SetVisibility(visibility, now);
        Audit(tenant, dashboard.Id, AuditAction.Updated, before, Snapshot(dashboard, dashboard.Name, widgets, wanted, wantedGroups));
        await SaveAsync(cancellationToken);
        return await DetailAsync(dashboard, tenant.UserId, cancellationToken);
    }

    public async Task<bool?> SetDefaultAsync(Guid id, bool isDefault, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        if (!await Visible(tenant.UserId).AnyAsync(d => d.Id == id, cancellationToken))
        {
            return null;
        }

        // Kullanıcı başına tek satır: yeni varsayılan eskisinin yerine geçer.
        var existing = await db.DashboardDefaults.SingleOrDefaultAsync(x => x.UserId == tenant.UserId, cancellationToken);
        var was = existing?.DashboardId == id;
        if (was == isDefault)
        {
            return isDefault;
        }

        if (!isDefault)
        {
            db.DashboardDefaults.Remove(existing!);
        }
        else if (existing is null)
        {
            db.DashboardDefaults.Add(new DashboardDefault(tenant.UserId, id, time.GetUtcNow()));
        }
        else
        {
            existing.MoveTo(id, time.GetUtcNow());
        }

        // Kişisel tercih; panonun kendisi (sürümü) değişmez. Kimin yaptığı denetim kaydının kullanıcısıdır.
        Audit(tenant, id, AuditAction.Updated, new JsonObject { ["isDefault"] = was }, new JsonObject { ["isDefault"] = isDefault });
        await SaveAsync(cancellationToken);
        return isDefault;
    }

    public async Task<ReportResult?> RunWidgetAsync(Guid id, Guid widgetId, CancellationToken cancellationToken)
    {
        var me = RequireTenant().UserId;
        var widget = await (
            from w in db.DashboardWidgets.AsNoTracking()
            join d in Visible(me).AsNoTracking() on w.DashboardId equals d.Id
            where w.Id == widgetId && d.Id == id
            select w).SingleOrDefaultAsync(cancellationToken);
        if (widget is null)
        {
            return null;
        }

        // Özet raporun bütün grupları tek sayfada; tablo raporunda yalnızca sayı gerekir.
        return await reports.RunAsync(widget.ReportId, null, 1, ReportLimits.MaxPageSize, cancellationToken);
    }

    // ---- Görünürlük

    /// <summary>Çağıranın görebildiği panolar: kendisinin, firmaya açık olanlar, kendisiyle ve üyesi olduğu rol, ekip ya da iş birimiyle paylaşılanlar.</summary>
    private IQueryable<Dashboard> Visible(Guid me)
    {
        // Grup paylaşımları (rol, ekip, iş birimi) çağıranın üyeliğinden her sorguda yeniden hesaplanır (ADR-0054 dilim 4a).
        var membershipId = RequireTenant().MembershipId;
        var roles = ShareAudience.RoleIds(db, membershipId);
        var teams = ShareAudience.TeamIds(db, membershipId);
        var units = ShareAudience.UnitIds(db, membershipId);
        return db.Dashboards.Where(d =>
            d.OwnerUserId == me
            || d.Visibility == ReportVisibility.Tenant
            || (d.Visibility == ReportVisibility.Users && db.DashboardShares.Any(s => s.DashboardId == d.Id
                && ((s.TargetKind == ShareTargetKind.User && s.UserId == me)
                    || (s.TargetKind == ShareTargetKind.Role && roles.Contains(s.UserId))
                    || (s.TargetKind == ShareTargetKind.Team && teams.Contains(s.UserId))
                    || (s.TargetKind == ShareTargetKind.BusinessUnit && units.Contains(s.UserId))))));
    }

    private static void RequireOwner(Dashboard dashboard, Guid me)
    {
        if (dashboard.OwnerUserId != me)
        {
            throw new DashboardForbiddenException("Panoyu yalnızca sahibi değiştirebilir.");
        }
    }

    private static void CheckVersion(Dashboard dashboard, long? expectedVersion)
    {
        if (expectedVersion is { } expected && expected != dashboard.Version)
        {
            throw new DashboardConflictException("Pano başka bir sekmede değiştirildi; sayfayı yenileyip tekrar deneyin.");
        }
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

    private async Task<List<DashboardSummary>> SummariesAsync(List<Dashboard> items, Guid me, CancellationToken cancellationToken)
    {
        var ids = items.Select(d => d.Id).ToList();
        var widgetCounts = await db.DashboardWidgets.AsNoTracking().Where(w => ids.Contains(w.DashboardId))
            .GroupBy(w => w.DashboardId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        // Paylaşım sayısı yalnızca sahibine gösterilir.
        var owned = items.Where(d => d.OwnerUserId == me).Select(d => d.Id).ToList();
        var shareCounts = await db.DashboardShares.AsNoTracking().Where(s => owned.Contains(s.DashboardId))
            .GroupBy(s => s.DashboardId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var defaultId = await db.DashboardDefaults.AsNoTracking().Where(x => x.UserId == me).Select(x => (Guid?)x.DashboardId).SingleOrDefaultAsync(cancellationToken);
        var names = (await members.ListActiveAsync(cancellationToken)).ToDictionary(m => m.UserId, m => m.DisplayName);

        return [.. items.Select(d => new DashboardSummary(
            d.Id, d.Name, widgetCounts.GetValueOrDefault(d.Id), d.UpdatedAt, d.Version, d.Visibility, d.OwnerUserId, names.GetValueOrDefault(d.OwnerUserId),
            d.OwnerUserId == me, d.Id == defaultId, shareCounts.GetValueOrDefault(d.Id)))];
    }

    private async Task<DashboardDetail> DetailAsync(Dashboard dashboard, Guid me, CancellationToken cancellationToken)
    {
        var widgets = await db.DashboardWidgets.AsNoTracking()
            .Where(w => w.DashboardId == dashboard.Id)
            .OrderBy(w => w.Position).ThenBy(w => w.Id)
            .ToListAsync(cancellationToken);
        var described = await DescribeAsync(widgets, cancellationToken);
        var summary = (await SummariesAsync([dashboard], me, cancellationToken))[0];
        var sharedWith = dashboard.OwnerUserId == me ? await SharedWithAsync(dashboard.Id, cancellationToken) : [];
        var sharedGroups = dashboard.OwnerUserId == me ? await SharedGroupsAsync(dashboard.Id, cancellationToken) : [];
        return new DashboardDetail(
            summary,
            [.. widgets.Select((w, index) => new DashboardWidgetView(w.Id, w.ReportId, described[index].ReportName, w.Kind, w.Title, w.Position))],
            sharedWith,
            sharedGroups);
    }

    private async Task<List<Guid>> SharedWithAsync(Guid dashboardId, CancellationToken cancellationToken) =>
        await db.DashboardShares.AsNoTracking().Where(s => s.DashboardId == dashboardId && s.TargetKind == ShareTargetKind.User).Select(s => s.UserId).ToListAsync(cancellationToken);

    private async Task<List<ShareGroup>> SharedGroupsAsync(Guid dashboardId, CancellationToken cancellationToken) =>
        (await db.DashboardShares.AsNoTracking().Where(s => s.DashboardId == dashboardId && s.TargetKind != ShareTargetKind.User)
            .Select(s => new { s.TargetKind, Id = s.UserId }).ToListAsync(cancellationToken))
        .Select(s => new ShareGroup(s.TargetKind, s.Id)).ToList();

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

    private static JsonObject Snapshot(
        Dashboard dashboard, string name, IEnumerable<(DashboardWidgetInput Input, string? ReportName)> widgets, IReadOnlyList<Guid> sharedWith,
        IReadOnlyList<ShareGroup>? groups = null)
    {
        var snapshot = new JsonObject
        {
            ["name"] = name,
            ["visibility"] = dashboard.Visibility.ToString(),
            ["sharedWith"] = string.Join(", ", sharedWith.OrderBy(u => u)),
            ["widgets"] = string.Join("; ", widgets.Select(w =>
                $"{w.Input.Title ?? w.ReportName ?? "(rapor görünmüyor)"} ({(w.Input.Kind == DashboardWidgetKind.Bar ? "çubuk" : "sayı")})")),
        };
        if (groups is { Count: > 0 })
        {
            snapshot["sharedWithGroups"] = string.Join(", ", groups.OrderBy(g => g.Kind).ThenBy(g => g.Id).Select(g => $"{g.Kind}:{g.Id}"));
        }

        return snapshot;
    }

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
