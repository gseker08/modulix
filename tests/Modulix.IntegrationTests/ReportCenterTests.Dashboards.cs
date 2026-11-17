using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Modules;
using Modulix.Infrastructure.Persistence;
using Modulix.Shared.Reporting;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// CRM epik E2, dilim 1: kullanıcı panoları. Pano kayıtlı raporlara bağlı bileşenlerden oluşur, yalnızca sahibine görünür ve veri erişimi
/// vermez: bileşen verisi bakan kişinin yetkisiyle gelir. Mutlu yol, hata/doğrulama, tenant izolasyonu ve yetki.
/// </summary>
public partial class ReportCenterTests
{
    private const string Dashboards = "/api/dashboards";

    private static DashboardWidgetRequest Number(ReportDetailDto report, string? title = null) => new(report.Summary.Id, "number", title);

    private static DashboardWidgetRequest Bar(ReportDetailDto report, string? title = null) => new(report.Summary.Id, "bar", title);

    private static async Task<DashboardDetailDto> CreateDashboardAsync(HttpClient client, string name, params DashboardWidgetRequest[] widgets) =>
        await ReadAsync<DashboardDetailDto>(await client.PostAsJsonAsync(Dashboards, new SaveDashboardRequest(name, widgets)), HttpStatusCode.Created);

    private static Task<HttpResponseMessage> WidgetDataAsync(HttpClient client, DashboardDetailDto dashboard, int index) =>
        client.GetAsync($"{Dashboards}/{dashboard.Summary.Id}/widgets/{dashboard.Widgets[index].Id}/data");

    [Fact]
    public async Task A_dashboard_keeps_its_widgets_in_order_and_each_widget_returns_its_reports_data()
    {
        var s = await SetUpAsync();
        await OpenAsync(s.Admin, s.CompanyId, "A", 100m);
        await OpenAsync(s.Admin, s.CompanyId, "B", 200m);
        await StageAsync(s.Admin, await OpenAsync(s.Admin, s.CompanyId, "C", 300m), "Qualified");
        var byStage = await SaveAsync(s.Admin, "Aşamaya göre", OpenByStage());
        var table = await SaveAsync(s.Admin, "Fırsat listesi", new ReportSpecDto { Dataset = Opportunities, Columns = ["title", "stage"] });

        var dashboard = await CreateDashboardAsync(s.Admin, "  Satış panom  ", Bar(byStage, "Aşamalar"), Number(byStage), Number(table));

        Assert.Equal("Satış panom", dashboard.Summary.Name);
        Assert.Equal(
            [("bar", "Aşamalar", 0), ("number", null, 1), ("number", null, 2)],
            dashboard.Widgets.Select(w => (w.Kind, w.Title, w.Position)));
        Assert.All(dashboard.Widgets.Take(2), w => Assert.Equal("Aşamaya göre", w.ReportName));

        // Çubuk: raporun grupları; sayı: aynı raporun toplamı; tablo raporunda eşleşen kayıt sayısı.
        var bar = await ReadAsync<ReportResultDto>(await WidgetDataAsync(s.Admin, dashboard, 0));
        Assert.Equal("ok", bar.Status);
        Assert.Equal(2, bar.Rows.Count);
        Assert.Equal(3m, bar.Rows.Sum(r => Cell(r, 2)));
        Assert.Equal(600m, Cell((await ReadAsync<ReportResultDto>(await WidgetDataAsync(s.Admin, dashboard, 1))).Totals.Single(), 3));
        Assert.Equal(3, (await ReadAsync<ReportResultDto>(await WidgetDataAsync(s.Admin, dashboard, 2))).RowsMatched);

        // Liste ve güncelleme: bileşen listesi bütün olarak değişir, sürüm artar.
        var listed = Assert.Single(await ReadAsync<List<DashboardSummaryDto>>(await s.Admin.GetAsync(Dashboards)));
        Assert.Equal((dashboard.Summary.Id, 3), (listed.Id, listed.WidgetCount));
        var updated = await ReadAsync<DashboardDetailDto>(await s.Admin.PutAsJsonAsync(
            $"{Dashboards}/{dashboard.Summary.Id}", new SaveDashboardRequest("Satış panom 2", [Number(table, "Açık fırsat")], dashboard.Summary.Version)));
        Assert.Equal(("Satış panom 2", dashboard.Summary.Version + 1), (updated.Summary.Name, updated.Summary.Version));
        Assert.Equal("Açık fırsat", Assert.Single(updated.Widgets).Title);

        // Rapor silinince bileşeni de gider; pano kalır.
        Assert.Equal(HttpStatusCode.NoContent, (await s.Admin.DeleteAsync($"{Reports}/{table.Summary.Id}")).StatusCode);
        Assert.Empty((await ReadAsync<DashboardDetailDto>(await s.Admin.GetAsync($"{Dashboards}/{dashboard.Summary.Id}"))).Widgets);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Admin.DeleteAsync($"{Dashboards}/{dashboard.Summary.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Admin.GetAsync($"{Dashboards}/{dashboard.Summary.Id}")).StatusCode);

        // Yazmalar denetim kaydında: oluşturma, güncelleme, silme.
        using var scope = factory.OpenScope(s.TenantId);
        var entries = await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().AuditLog
            .Where(a => a.EntityKey == CoreModule.AuditedEntityKeys.Dashboard && a.RecordId == dashboard.Summary.Id)
            .CountAsync();
        Assert.Equal(3, entries);
    }

    [Fact]
    public async Task Invalid_dashboards_are_rejected_and_a_stale_version_conflicts()
    {
        var s = await SetUpAsync();
        var byStage = await SaveAsync(s.Admin, "Aşamaya göre", OpenByStage());
        var table = await SaveAsync(s.Admin, "Fırsat listesi", new ReportSpecDto { Dataset = Opportunities, Columns = ["title"] });
        var dashboard = await CreateDashboardAsync(s.Admin, "Pano 1", Number(byStage));

        async Task<HttpStatusCode> TryCreateAsync(string name, params DashboardWidgetRequest[] widgets) =>
            (await s.Admin.PostAsJsonAsync(Dashboards, new SaveDashboardRequest(name, widgets))).StatusCode;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, await TryCreateAsync("  "));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await TryCreateAsync(new string('x', 121)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await TryCreateAsync("Pano 1"));   // ad kullanıcı içinde tekil
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await TryCreateAsync("Tür", new DashboardWidgetRequest(byStage.Summary.Id, "pie")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await TryCreateAsync("Rapor yok", new DashboardWidgetRequest(Guid.NewGuid(), "number")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await TryCreateAsync("Tablo çubuk olmaz", Bar(table)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await TryCreateAsync("Çok bileşen", [.. Enumerable.Repeat(Number(byStage), 13)]));
        Assert.Single(await ReadAsync<List<DashboardSummaryDto>>(await s.Admin.GetAsync(Dashboards)));

        var first = await s.Admin.PutAsJsonAsync($"{Dashboards}/{dashboard.Summary.Id}", new SaveDashboardRequest("Pano 1b", [], dashboard.Summary.Version));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var stale = await s.Admin.PutAsJsonAsync($"{Dashboards}/{dashboard.Summary.Id}", new SaveDashboardRequest("Pano 1c", [], dashboard.Summary.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Admin.PutAsJsonAsync($"{Dashboards}/{Guid.NewGuid()}", new SaveDashboardRequest("Yok"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Admin.GetAsync($"{Dashboards}/{dashboard.Summary.Id}/widgets/{Guid.NewGuid()}/data")).StatusCode);
    }

    [Fact]
    public async Task Another_tenant_and_another_user_see_no_dashboard_and_cannot_touch_it()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        await OpenAsync(a.Admin, a.CompanyId, "A fırsatı", 1_000m);
        var report = await SaveAsync(a.Admin, "A raporu", OpenByStage());
        await ReadAsync<ReportDetailDto>(await a.Admin.PutAsJsonAsync($"{Reports}/{report.Summary.Id}/sharing", new ShareReportRequest("tenant", null)));
        var dashboard = await CreateDashboardAsync(a.Admin, "A panosu", Bar(report));
        var url = $"{Dashboards}/{dashboard.Summary.Id}";
        var colleague = await UserAsync(a, [.. DataRead, CoreModule.Permissions.ReadReports, CoreModule.Permissions.ManageReports], Scope.All);

        foreach (var outsider in new[] { b.Admin, colleague })
        {
            Assert.Empty(await ReadAsync<List<DashboardSummaryDto>>(await outsider.GetAsync(Dashboards)));
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await WidgetDataAsync(outsider, dashboard, 0)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutAsJsonAsync(url, new SaveDashboardRequest("Ele geçirildi"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteAsync(url)).StatusCode);
        }

        // Öteki firma, bu firmanın raporunu kendi panosuna bağlayamaz.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.Admin.PostAsJsonAsync(Dashboards, new SaveDashboardRequest("B panosu", [Number(report)]))).StatusCode);
        Assert.Equal("A panosu", (await ReadAsync<DashboardDetailDto>(await a.Admin.GetAsync(url))).Summary.Name);
    }

    [Fact]
    public async Task Permissions_gate_dashboards_and_a_widget_shows_only_what_the_viewer_may_see()
    {
        var s = await SetUpAsync();
        await OpenAsync(s.Admin, s.CompanyId, "Yöneticinin fırsatı", 500m);
        // Yalnızca sayan rapor: tutar hassas alandır, izni olmayana rapor "forbidden" döner (aşağıda ayrıca denenir).
        var countOnly = new ReportSpecDto { Dataset = Opportunities, Groups = [new() { Field = "stage" }], Measures = [new() { Aggregation = "count" }] };
        var shared = await SaveAsync(s.Admin, "Paylaşılan", countOnly);
        var withAmount = await SaveAsync(s.Admin, "Tutarlı", OpenByStage());
        await ReadAsync<ReportDetailDto>(await s.Admin.PutAsJsonAsync($"{Reports}/{withAmount.Summary.Id}/sharing", new ShareReportRequest("tenant", null)));
        await ReadAsync<ReportDetailDto>(await s.Admin.PutAsJsonAsync($"{Reports}/{shared.Summary.Id}/sharing", new ShareReportRequest("tenant", null)));

        // Rapor izni olmayan hiçbir pano ucuna erişemez; yalnızca okuyabilen pano kuramaz.
        var noReports = await UserAsync(s, DataRead, Scope.All);
        Assert.Equal(HttpStatusCode.Forbidden, (await noReports.GetAsync(Dashboards)).StatusCode);
        var reader = await UserAsync(s, [.. DataRead, .. CoreRead], Scope.All);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(Dashboards)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Dashboards, new SaveDashboardRequest("Okuyucunun"))).StatusCode);
        using var anonymous = factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Dashboards)).StatusCode);

        // Kapsamı "kendi kayıtları" olan kullanıcı paylaşılan raporu panosuna koyar; bileşen yöneticinin fırsatını saymaz.
        var own = await UserAsync(s, [.. DataRead, .. CoreRead, CoreModule.Permissions.ManageReports], Scope.Own);
        var dashboard = await CreateDashboardAsync(own, "Benim panom", Number(shared), Number(withAmount));
        var data = await ReadAsync<ReportResultDto>(await WidgetDataAsync(own, dashboard, 0));
        Assert.Equal("ok", data.Status);
        Assert.Equal(0, data.RowsMatched);

        // Hassas alanı toplayan rapor, o alanı göremeyen kullanıcının panosunda veri vermez.
        Assert.Equal("forbidden", (await ReadAsync<ReportResultDto>(await WidgetDataAsync(own, dashboard, 1))).Status);

        // Paylaşım geri alınınca bileşen veri vermez, pano kalır ve raporun adı görünmez.
        await ReadAsync<ReportDetailDto>(await s.Admin.PutAsJsonAsync(
            $"{Reports}/{shared.Summary.Id}/sharing", new ShareReportRequest("private", null)));
        Assert.Equal(HttpStatusCode.NotFound, (await WidgetDataAsync(own, dashboard, 0)).StatusCode);
        Assert.Null((await ReadAsync<DashboardDetailDto>(await own.GetAsync($"{Dashboards}/{dashboard.Summary.Id}"))).Widgets[0].ReportName);
    }
}
