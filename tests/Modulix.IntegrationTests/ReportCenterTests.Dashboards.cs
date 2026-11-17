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
/// CRM epik E2: kullanıcı panoları. Pano kayıtlı raporlara bağlı bileşenlerden oluşur ve veri erişimi vermez: bileşen verisi bakan kişinin
/// yetkisiyle gelir. Dilim 2: sahibi panoyu seçtiği kullanıcılarla ya da firmayla paylaşır, kullanıcı görebildiği bir panoyu varsayılanı
/// yapar. Mutlu yol, hata/doğrulama, tenant izolasyonu ve yetki.
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

    // ---- Dilim 2: paylaşım ve varsayılan pano

    private static readonly string[] DashboardOwner =
        [.. DataRead, CoreModule.Permissions.ReadReports, CoreModule.Permissions.ManageReports, CoreModule.Permissions.ShareReports];

    private static Task<HttpResponseMessage> ShareDashboardAsync(HttpClient client, Guid id, string visibility, IReadOnlyList<Guid>? users = null, long? version = null) =>
        client.PutAsJsonAsync($"{Dashboards}/{id}/sharing", new ShareDashboardRequest(visibility, users, version));

    private static Task<HttpResponseMessage> SetDefaultAsync(HttpClient client, Guid id, bool isDefault) =>
        client.PutAsJsonAsync($"{Dashboards}/{id}/default", new DefaultDashboardRequest(isDefault));

    private static async Task<List<DashboardSummaryDto>> DashboardsAsync(HttpClient client) =>
        await ReadAsync<List<DashboardSummaryDto>>(await client.GetAsync(Dashboards));

    [Fact]
    public async Task A_shared_dashboard_is_visible_to_the_chosen_people_gives_no_data_access_and_revocation_is_immediate()
    {
        var s = await SetUpAsync();
        await OpenAsync(s.Admin, s.CompanyId, "A", 100m);
        var owner = await UserAsync(s, DashboardOwner, Scope.All);
        var userTwo = await factory.CreateUserAsync(s.Tenant);
        await factory.CreateRoleAsync(s.TenantId, [.. DataRead, .. CoreRead], Scope.All, userTwo);
        var two = await LoginAsync(userTwo);
        var three = await UserAsync(s, [.. DataRead, .. CoreRead], Scope.All);

        var countOnly = new ReportSpecDto { Dataset = Opportunities, Groups = [new() { Field = "stage" }], Measures = [new() { Aggregation = "count" }] };
        var open = await SaveAsync(owner, "Firmaya açık rapor", countOnly);
        await ReadAsync<ReportDetailDto>(await owner.PutAsJsonAsync($"{Reports}/{open.Summary.Id}/sharing", new ShareReportRequest("tenant", null)));
        var secret = await SaveAsync(owner, "Özel rapor", countOnly);
        var dashboard = await CreateDashboardAsync(owner, "Ekip panosu", Number(open), Number(secret));
        var id = dashboard.Summary.Id;
        var url = $"{Dashboards}/{id}";
        Assert.Equal(("private", true, 0), (dashboard.Summary.Visibility, dashboard.Summary.IsOwner, dashboard.Summary.SharedWithCount));

        // Özel: başkası bulamaz.
        Assert.Equal(HttpStatusCode.NotFound, (await two.GetAsync(url)).StatusCode);
        Assert.Empty(await DashboardsAsync(two));

        // Seçilen kullanıcıyla paylaş: yalnız o görür; paylaşım listesini yalnız sahibi görür.
        var shared = await ReadAsync<DashboardDetailDto>(await ShareDashboardAsync(owner, id, "users", [userTwo.Id, userTwo.Id], dashboard.Summary.Version));
        Assert.Equal(("users", 1, dashboard.Summary.Version + 1), (shared.Summary.Visibility, shared.Summary.SharedWithCount, shared.Summary.Version));
        Assert.Equal([userTwo.Id], shared.SharedWith);
        var seen = await ReadAsync<DashboardDetailDto>(await two.GetAsync(url));
        Assert.False(seen.Summary.IsOwner);
        Assert.NotNull(seen.Summary.OwnerName);
        Assert.Equal(0, seen.Summary.SharedWithCount);
        Assert.Empty(seen.SharedWith ?? []);
        Assert.Equal(id, Assert.Single(await DashboardsAsync(two)).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await three.GetAsync(url)).StatusCode);

        // Pano veri erişimi vermez: görebildiği raporun bileşeni kendi yetkisiyle çalışır, göremediği raporun bileşeni veri vermez.
        Assert.Equal("ok", (await ReadAsync<ReportResultDto>(await WidgetDataAsync(two, seen, 0))).Status);
        Assert.Null(seen.Widgets[1].ReportName);
        Assert.Equal(HttpStatusCode.NotFound, (await WidgetDataAsync(two, seen, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await two.GetAsync($"{Reports}/{secret.Summary.Id}")).StatusCode);

        // Firmaya aç: üçüncü kişi de görür.
        await ReadAsync<DashboardDetailDto>(await ShareDashboardAsync(owner, id, "tenant"));
        Assert.Equal(HttpStatusCode.OK, (await three.GetAsync(url)).StatusCode);
        Assert.Equal(id, Assert.Single(await DashboardsAsync(three)).Id);

        // Paylaşımı geri al: erişim hemen biter (pano, liste, bileşen verisi).
        var closed = await ReadAsync<DashboardDetailDto>(await ShareDashboardAsync(owner, id, "private"));
        Assert.Equal(("private", 0), (closed.Summary.Visibility, closed.Summary.SharedWithCount));
        foreach (var former in new[] { two, three })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await former.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await WidgetDataAsync(former, seen, 0)).StatusCode);
            Assert.Empty(await DashboardsAsync(former));
        }

        // Paylaşım değişiklikleri denetim kaydında: oluşturma + üç paylaşım ayarı.
        using var scope = factory.OpenScope(s.TenantId);
        var entries = await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().AuditLog
            .Where(a => a.EntityKey == CoreModule.AuditedEntityKeys.Dashboard && a.RecordId == id)
            .CountAsync();
        Assert.Equal(4, entries);
    }

    [Fact]
    public async Task Dashboard_sharing_validates_the_visibility_the_selected_users_and_the_version()
    {
        var s = await SetUpAsync();
        var dashboard = await CreateDashboardAsync(s.Admin, "Pano");
        var id = dashboard.Summary.Id;
        var member = await factory.CreateUserAsync(s.Tenant);
        await factory.CreateRoleAsync(s.TenantId, [.. CoreRead], Scope.All, member);
        var stranger = await factory.CreateUserAsync(await factory.CreateTenantAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await ShareDashboardAsync(s.Admin, id, "herkes")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ShareDashboardAsync(s.Admin, id, "7")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareDashboardAsync(s.Admin, id, "users", [])).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareDashboardAsync(s.Admin, id, "users", [s.AdminId])).StatusCode);   // yalnızca kendisi
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareDashboardAsync(s.Admin, id, "users", [stranger.Id])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ShareDashboardAsync(s.Admin, Guid.NewGuid(), "tenant")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetDefaultAsync(s.Admin, Guid.NewGuid(), true)).StatusCode);
        Assert.Equal("private", (await ReadAsync<DashboardDetailDto>(await s.Admin.GetAsync($"{Dashboards}/{id}"))).Summary.Visibility);

        Assert.Equal(HttpStatusCode.OK, (await ShareDashboardAsync(s.Admin, id, "users", [member.Id], dashboard.Summary.Version)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ShareDashboardAsync(s.Admin, id, "tenant", null, dashboard.Summary.Version)).StatusCode);
    }

    [Fact]
    public async Task Another_tenant_never_sees_a_shared_dashboard_and_cannot_share_or_mark_it()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        var dashboard = await CreateDashboardAsync(a.Admin, "A panosu");
        var id = dashboard.Summary.Id;
        await ReadAsync<DashboardDetailDto>(await ShareDashboardAsync(a.Admin, id, "tenant"));

        // "Firma" paylaşımı yalnızca kendi firmasıdır.
        Assert.Empty(await DashboardsAsync(b.Admin));
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.GetAsync($"{Dashboards}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ShareDashboardAsync(b.Admin, id, "private")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetDefaultAsync(b.Admin, id, true)).StatusCode);

        // Öteki firmanın kullanıcısıyla paylaşılamaz.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareDashboardAsync(a.Admin, id, "users", [b.AdminId])).StatusCode);
        Assert.Equal("tenant", (await ReadAsync<DashboardDetailDto>(await a.Admin.GetAsync($"{Dashboards}/{id}"))).Summary.Visibility);
    }

    [Fact]
    public async Task Only_the_owner_with_the_share_permission_shares_and_a_viewer_cannot_change_the_dashboard()
    {
        var s = await SetUpAsync();
        var owner = await UserAsync(s, DashboardOwner, Scope.All);
        var dashboard = await CreateDashboardAsync(owner, "Paylaşılan pano");
        var id = dashboard.Summary.Id;
        var url = $"{Dashboards}/{id}";

        // Paylaşım izni olmayan kendi panosunu da paylaşamaz.
        var noShare = await UserAsync(s, [.. DataRead, CoreModule.Permissions.ReadReports, CoreModule.Permissions.ManageReports], Scope.All);
        var mine = await CreateDashboardAsync(noShare, "Paylaşamam");
        Assert.Equal(HttpStatusCode.Forbidden, (await ShareDashboardAsync(noShare, mine.Summary.Id, "tenant")).StatusCode);
        Assert.Equal("private", (await ReadAsync<DashboardDetailDto>(await noShare.GetAsync($"{Dashboards}/{mine.Summary.Id}"))).Summary.Visibility);

        // Rapor izni olmayan varsayılan da seçemez.
        var noReports = await UserAsync(s, DataRead, Scope.All);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetDefaultAsync(noReports, id, true)).StatusCode);

        // Paylaşılan kişi yönetme ve paylaşma iznine sahip olsa bile panoyu değiştiremez, silemez, paylaşımını ayarlayamaz.
        await ReadAsync<DashboardDetailDto>(await ShareDashboardAsync(owner, id, "tenant"));
        var editor = await UserAsync(s, DashboardOwner, Scope.All);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PutAsJsonAsync(url, new SaveDashboardRequest("Ele geçirildi"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ShareDashboardAsync(editor, id, "private")).StatusCode);

        var after = await ReadAsync<DashboardDetailDto>(await owner.GetAsync(url));
        Assert.Equal(("Paylaşılan pano", "tenant"), (after.Summary.Name, after.Summary.Visibility));
    }

    [Fact]
    public async Task The_default_dashboard_is_personal_listed_first_and_cleared_when_access_ends()
    {
        var s = await SetUpAsync();
        var owner = await UserAsync(s, DashboardOwner, Scope.All);
        var viewerUser = await factory.CreateUserAsync(s.Tenant);
        await factory.CreateRoleAsync(s.TenantId, [.. DataRead, CoreModule.Permissions.ReadReports, CoreModule.Permissions.ManageReports], Scope.All, viewerUser);
        var viewer = await LoginAsync(viewerUser);

        var team = await CreateDashboardAsync(owner, "Z ekip panosu");
        await ReadAsync<DashboardDetailDto>(await ShareDashboardAsync(owner, team.Summary.Id, "users", [viewerUser.Id]));
        var own = await CreateDashboardAsync(viewer, "A kendi panom");

        // Varsayılan yokken ada göre; paylaşılan pano varsayılan yapılınca başa geçer.
        Assert.Equal([own.Summary.Id, team.Summary.Id], (await DashboardsAsync(viewer)).Select(d => d.Id));
        Assert.Equal(HttpStatusCode.OK, (await SetDefaultAsync(viewer, team.Summary.Id, true)).StatusCode);
        var listed = await DashboardsAsync(viewer);
        Assert.Equal([(team.Summary.Id, true), (own.Summary.Id, false)], listed.Select(d => (d.Id, d.IsDefault)));

        // Kişiye özeldir ve panoyu değiştirmez: sahibinin varsayılanı değildir, sürüm aynı kalır.
        var ownersView = Assert.Single(await DashboardsAsync(owner));
        Assert.False(ownersView.IsDefault);
        Assert.Equal(listed[0].Version, ownersView.Version);

        // Kullanıcı başına tek varsayılan: yenisi eskisinin yerine geçer; işaret kaldırılabilir.
        Assert.Equal(HttpStatusCode.OK, (await SetDefaultAsync(viewer, own.Summary.Id, true)).StatusCode);
        Assert.Equal([(own.Summary.Id, true), (team.Summary.Id, false)], (await DashboardsAsync(viewer)).Select(d => (d.Id, d.IsDefault)));
        Assert.Equal(HttpStatusCode.OK, (await SetDefaultAsync(viewer, own.Summary.Id, false)).StatusCode);
        Assert.DoesNotContain(await DashboardsAsync(viewer), d => d.IsDefault);

        // Erişim bitince varsayılan işareti de kalkar; pano yeniden paylaşılınca kendiliğinden varsayılan olmaz.
        Assert.Equal(HttpStatusCode.OK, (await SetDefaultAsync(viewer, team.Summary.Id, true)).StatusCode);
        await ReadAsync<DashboardDetailDto>(await ShareDashboardAsync(owner, team.Summary.Id, "private"));
        Assert.Equal(HttpStatusCode.NotFound, (await SetDefaultAsync(viewer, team.Summary.Id, true)).StatusCode);
        await ReadAsync<DashboardDetailDto>(await ShareDashboardAsync(owner, team.Summary.Id, "tenant"));
        Assert.Equal([(own.Summary.Id, false), (team.Summary.Id, false)], (await DashboardsAsync(viewer)).Select(d => (d.Id, d.IsDefault)));
    }
}
