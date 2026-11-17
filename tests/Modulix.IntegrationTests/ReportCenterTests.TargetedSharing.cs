using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Modules;
using Modulix.Core.Organization;
using Modulix.Core.Teams;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Reporting;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0054 dilim 4a: hedefli paylaşım. Rapor ve pano rol, ekip ya da iş birimine (alt birimler dahil) paylaşılır; erişim her okumada
/// üyelikten hesaplanır, paylaşım yalnız "görme" verir. Mutlu yol, hata/doğrulama, tenant izolasyonu ve yetki.
/// </summary>
public partial class ReportCenterTests
{
    private const string ShareGroups = "/api/reports/share-groups";

    private static readonly string[] ViewerPermissions = [.. DataRead, .. CoreRead];

    private static readonly ReportSpecDto CountByStage = new() { Dataset = Opportunities, Groups = [new() { Field = "stage" }], Measures = [new() { Aggregation = "count" }] };

    /// <summary>Paylaşım hedefleri olarak kullanılan rol, ekip ve iş birimi ağacı (merkez → bölge → bayi) ile her hedefin tek üyesi.</summary>
    private sealed record Audience(
        Guid RoleId, Guid TeamId, Guid RegionId, Guid DealerId,
        HttpClient InRole, HttpClient InTeam, HttpClient InRegion, HttpClient InDealer, HttpClient Outsider);

    private async Task<Audience> BuildAudienceAsync(Setup s)
    {
        // Rol üyesi: yalnız kendi rolü vardır; ekip ve birim üyeliği başka kullanıcılardadır.
        var roleUser = await factory.CreateUserAsync(s.Tenant);
        var role = await factory.CreateRoleAsync(s.TenantId, ViewerPermissions, Scope.All, roleUser);
        var teamUser = await factory.CreateUserAsync(s.Tenant);
        await factory.CreateRoleAsync(s.TenantId, ViewerPermissions, Scope.All, teamUser);
        var regionUser = await factory.CreateUserAsync(s.Tenant);
        await factory.CreateRoleAsync(s.TenantId, ViewerPermissions, Scope.All, regionUser);
        var dealerUser = await factory.CreateUserAsync(s.Tenant);
        await factory.CreateRoleAsync(s.TenantId, ViewerPermissions, Scope.All, dealerUser);
        var outsider = await factory.CreateUserAsync(s.Tenant);
        await factory.CreateRoleAsync(s.TenantId, ViewerPermissions, Scope.All, outsider);

        Guid teamId;
        Guid regionId;
        Guid dealerId;
        using (var scope = factory.OpenScope(s.TenantId))
        {
            var teams = scope.ServiceProvider.GetRequiredService<ITeamService>();
            var team = await teams.CreateAsync($"Paylaşım ekibi {Guid.NewGuid():N}"[..24], null, CancellationToken.None);
            teamId = team.Id;
            Assert.NotNull(await teams.SetMembersAsync(team.Id, [await factory.GetMembershipIdAsync(teamUser.Id, s.TenantId)], CancellationToken.None));

            var units = scope.ServiceProvider.GetRequiredService<IBusinessUnitService>();
            var root = (await units.ListAsync(CancellationToken.None)).Single(u => u.ParentId is null);
            var region = await units.CreateAsync(root.Id, "Marmara Bölge", null, BusinessUnitKinds.Other, CancellationToken.None);
            var dealer = await units.CreateAsync(region!.Unit.Id, "Gebze Bayi", null, BusinessUnitKinds.Dealer, CancellationToken.None);
            regionId = region.Unit.Id;
            dealerId = dealer!.Unit.Id;
            Assert.True(await units.SetMembershipBusinessUnitAsync(await factory.GetMembershipIdAsync(regionUser.Id, s.TenantId), regionId, CancellationToken.None));
            Assert.True(await units.SetMembershipBusinessUnitAsync(await factory.GetMembershipIdAsync(dealerUser.Id, s.TenantId), dealerId, CancellationToken.None));
        }

        return new Audience(
            role.Id, teamId, regionId, dealerId,
            await LoginAsync(roleUser), await LoginAsync(teamUser), await LoginAsync(regionUser), await LoginAsync(dealerUser), await LoginAsync(outsider));
    }

    private static ShareGroupDto Group(string kind, Guid id) => new(kind, id);

    private static Task<HttpResponseMessage> ShareReportToAsync(HttpClient client, Guid id, params ShareGroupDto[] groups) =>
        client.PutAsJsonAsync($"{Reports}/{id}/sharing", new ShareReportRequest("users", null, null, groups));

    private static Task<HttpResponseMessage> ShareDashboardToAsync(HttpClient client, Guid id, params ShareGroupDto[] groups) =>
        client.PutAsJsonAsync($"{Dashboards}/{id}/sharing", new ShareDashboardRequest("users", null, null, groups));

    private static async Task<bool> CanSeeAsync(HttpClient client, string url) => (await client.GetAsync(url)).StatusCode == HttpStatusCode.OK;

    [Fact]
    public async Task A_report_shared_to_a_role_a_team_or_a_unit_reaches_exactly_those_members_and_sub_units_are_included()
    {
        var s = await SetUpAsync();
        var a = await BuildAudienceAsync(s);
        var report = await SaveAsync(s.Admin, "Hedefli rapor", CountByStage);
        var url = $"{Reports}/{report.Summary.Id}";

        // Paylaşılmadan kimse görmez.
        foreach (var client in new[] { a.InRole, a.InTeam, a.InRegion, a.InDealer, a.Outsider })
        {
            Assert.False(await CanSeeAsync(client, url));
        }

        // Rol: yalnız rol üyesi.
        var byRole = await ReadAsync<ReportDetailDto>(await ShareReportToAsync(s.Admin, report.Summary.Id, Group("role", a.RoleId)));
        Assert.Equal(Group("role", a.RoleId), Assert.Single(byRole.SharedWithGroups!));
        Assert.Empty(byRole.SharedWith);
        Assert.True(await CanSeeAsync(a.InRole, url));
        Assert.False(await CanSeeAsync(a.InTeam, url));
        Assert.False(await CanSeeAsync(a.Outsider, url));

        // Ekip: yalnız ekip üyesi (rol paylaşımı değiştirilince kalkar).
        await ReadAsync<ReportDetailDto>(await ShareReportToAsync(s.Admin, report.Summary.Id, Group("team", a.TeamId)));
        Assert.True(await CanSeeAsync(a.InTeam, url));
        Assert.False(await CanSeeAsync(a.InRole, url));

        // İş birimi: birim üyesi ve alt birimdeki (bayi) üye görür; üst birimdekiler ve dışarıdakiler görmez.
        await ReadAsync<ReportDetailDto>(await ShareReportToAsync(s.Admin, report.Summary.Id, Group("businessUnit", a.RegionId)));
        Assert.True(await CanSeeAsync(a.InRegion, url));
        Assert.True(await CanSeeAsync(a.InDealer, url));
        Assert.False(await CanSeeAsync(a.InTeam, url));
        Assert.False(await CanSeeAsync(a.Outsider, url));

        // Yalnız bayi birimine paylaşılırsa bölge üyesi (üst birim) görmez.
        await ReadAsync<ReportDetailDto>(await ShareReportToAsync(s.Admin, report.Summary.Id, Group("businessUnit", a.DealerId)));
        Assert.True(await CanSeeAsync(a.InDealer, url));
        Assert.False(await CanSeeAsync(a.InRegion, url));

        // Birden çok grup birlikte; kullanıcı listesi ve grup listesi sahibine döner, başkasına dönmez.
        var many = await ReadAsync<ReportDetailDto>(await ShareReportToAsync(
            s.Admin, report.Summary.Id, Group("role", a.RoleId), Group("team", a.TeamId), Group("role", a.RoleId)));
        Assert.Equal(2, many.SharedWithGroups!.Count);
        Assert.True(await CanSeeAsync(a.InRole, url) && await CanSeeAsync(a.InTeam, url));
        var seenByRoleMember = await ReadAsync<ReportDetailDto>(await a.InRole.GetAsync(url));
        Assert.Empty(seenByRoleMember.SharedWithGroups ?? []);

        // Geri al: erişim hemen biter.
        await ReadAsync<ReportDetailDto>(await s.Admin.PutAsJsonAsync($"{Reports}/{report.Summary.Id}/sharing", new ShareReportRequest("private", null)));
        Assert.False(await CanSeeAsync(a.InRole, url));
        Assert.False(await CanSeeAsync(a.InTeam, url));
    }

    [Fact]
    public async Task A_dashboard_shared_to_groups_is_visible_to_members_and_lists_the_chosen_groups_to_its_owner()
    {
        var s = await SetUpAsync();
        var a = await BuildAudienceAsync(s);
        var dashboard = await CreateDashboardAsync(s.Admin, "Hedefli pano");
        var url = $"{Dashboards}/{dashboard.Summary.Id}";

        var shared = await ReadAsync<DashboardDetailDto>(await ShareDashboardToAsync(
            s.Admin, dashboard.Summary.Id, Group("role", a.RoleId), Group("businessUnit", a.RegionId)));
        Assert.Equal(2, shared.SharedWithGroups!.Count);
        Assert.True(await CanSeeAsync(a.InRole, url));
        Assert.True(await CanSeeAsync(a.InRegion, url));
        Assert.True(await CanSeeAsync(a.InDealer, url));   // alt birim
        Assert.False(await CanSeeAsync(a.InTeam, url));
        Assert.False(await CanSeeAsync(a.Outsider, url));
        Assert.Equal(dashboard.Summary.Id, Assert.Single(await DashboardsAsync(a.InDealer)).Id);
        Assert.Empty(await DashboardsAsync(a.Outsider));

        // Rol paylaşımından çıkarılan üye pano erişimini ve varsayılan işaretini kaybeder.
        Assert.Equal(HttpStatusCode.OK, (await SetDefaultAsync(a.InRole, dashboard.Summary.Id, true)).StatusCode);
        await ReadAsync<DashboardDetailDto>(await ShareDashboardToAsync(s.Admin, dashboard.Summary.Id, Group("team", a.TeamId)));
        Assert.False(await CanSeeAsync(a.InRole, url));
        Assert.Empty(await DashboardsAsync(a.InRole));
        Assert.True(await CanSeeAsync(a.InTeam, url));
    }

    [Fact]
    public async Task Group_sharing_validates_the_kind_the_targets_and_that_something_is_selected()
    {
        var s = await SetUpAsync();
        var a = await BuildAudienceAsync(s);
        var report = await SaveAsync(s.Admin, "Doğrulama", CountByStage);
        var dashboard = await CreateDashboardAsync(s.Admin, "Doğrulama panosu");

        // Tür tanınmıyor ya da kullanıcı: kullanıcılar `users` ile gelir.
        Assert.Equal(HttpStatusCode.BadRequest, (await ShareReportToAsync(s.Admin, report.Summary.Id, Group("departman", a.RoleId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ShareReportToAsync(s.Admin, report.Summary.Id, Group("user", s.AdminId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ShareDashboardToAsync(s.Admin, dashboard.Summary.Id, Group("7", a.RoleId))).StatusCode);

        // Olmayan hedef, yanlış türde kimlik ve boş seçim.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareReportToAsync(s.Admin, report.Summary.Id, Group("role", Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareReportToAsync(s.Admin, report.Summary.Id, Group("team", a.RoleId))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareDashboardToAsync(s.Admin, dashboard.Summary.Id, Group("businessUnit", Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareReportToAsync(s.Admin, report.Summary.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareDashboardToAsync(s.Admin, dashboard.Summary.Id)).StatusCode);

        // Kapalı iş birimi hedef olamaz.
        using (var scope = factory.OpenScope(s.TenantId))
        {
            var units = scope.ServiceProvider.GetRequiredService<IBusinessUnitService>();
            Assert.NotNull(await units.SetActiveAsync(a.DealerId, false, CancellationToken.None));
        }

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareReportToAsync(s.Admin, report.Summary.Id, Group("businessUnit", a.DealerId))).StatusCode);

        // Hiçbir geçersiz istek paylaşımı değiştirmedi.
        Assert.Equal("private", (await ReadAsync<ReportDetailDto>(await s.Admin.GetAsync($"{Reports}/{report.Summary.Id}"))).Summary.Visibility);

        // Seçenek listesi rol, ekip ve etkin birimleri adıyla verir (kapalı bayi yok).
        var options = await ReadAsync<List<ShareGroupOptionDto>>(await s.Admin.GetAsync(ShareGroups));
        Assert.Contains(options, o => o.Kind == "role" && o.Id == a.RoleId);
        Assert.Contains(options, o => o.Kind == "team" && o.Id == a.TeamId);
        Assert.Contains(options, o => o is { Kind: "businessUnit", Name: "Marmara Bölge" } && o.Id == a.RegionId);
        Assert.DoesNotContain(options, o => o.Id == a.DealerId);
    }

    [Fact]
    public async Task Another_tenant_neither_reaches_a_group_shared_report_nor_lists_or_targets_this_tenants_groups()
    {
        var s = await SetUpAsync();
        var a = await BuildAudienceAsync(s);
        var report = await SaveAsync(s.Admin, "Tenant A raporu", CountByStage);
        await ReadAsync<ReportDetailDto>(await ShareReportToAsync(s.Admin, report.Summary.Id, Group("role", a.RoleId), Group("businessUnit", a.RegionId)));

        var other = await SetUpAsync();
        var otherAudience = await BuildAudienceAsync(other);

        // Başka tenant'ın üyeleri (kendi rol ve birimlerinde olsalar da) raporu görmez.
        foreach (var client in new[] { other.Admin, otherAudience.InRole, otherAudience.InRegion, otherAudience.InDealer })
        {
            Assert.False(await CanSeeAsync(client, $"{Reports}/{report.Summary.Id}"));
        }

        // Başka tenant'ın seçenek listesi yalnız kendi gruplarını içerir; bizim grup kimlikleri hedef olamaz.
        var theirs = await ReadAsync<List<ShareGroupOptionDto>>(await other.Admin.GetAsync(ShareGroups));
        Assert.DoesNotContain(theirs, o => o.Id == a.RoleId || o.Id == a.TeamId || o.Id == a.RegionId);
        Assert.Contains(theirs, o => o.Id == otherAudience.RoleId);
        var theirReport = await SaveAsync(other.Admin, "Tenant B raporu", CountByStage);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ShareReportToAsync(other.Admin, theirReport.Summary.Id, Group("role", a.RoleId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ShareReportToAsync(other.Admin, report.Summary.Id, Group("role", otherAudience.RoleId))).StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_with_the_share_permission_shares_to_groups_and_sharing_gives_no_data_access()
    {
        var s = await SetUpAsync();
        var a = await BuildAudienceAsync(s);
        var report = await SaveAsync(s.Admin, "Yetki raporu", CountByStage);
        await ReadAsync<ReportDetailDto>(await ShareReportToAsync(s.Admin, report.Summary.Id, Group("role", a.RoleId)));

        // Paylaşım izni olmayan kullanıcı ne seçenekleri listeler ne de paylaşır; paylaşılan raporun sahibi de değildir.
        Assert.Equal(HttpStatusCode.Forbidden, (await a.InRole.GetAsync(ShareGroups)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ShareReportToAsync(a.InRole, report.Summary.Id, Group("role", a.RoleId))).StatusCode);

        // Paylaşım izni olsa bile sahibi olmayan paylaşamaz (rapor ona görünmüyorsa 404).
        var sharer = await UserAsync(s, [.. DataRead, .. CoreRead, CoreModule.Permissions.ShareReports], Scope.All);
        Assert.Equal(HttpStatusCode.OK, (await sharer.GetAsync(ShareGroups)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ShareReportToAsync(sharer, report.Summary.Id, Group("role", a.RoleId))).StatusCode);

        // Paylaşım yalnız görme verir: grup üyesi veri iznini kaybederse raporun verisini alamaz.
        var noData = await factory.CreateUserAsync(s.Tenant);
        var thin = await factory.CreateRoleAsync(s.TenantId, CoreRead, Scope.All, noData);
        await ReadAsync<ReportDetailDto>(await ShareReportToAsync(s.Admin, report.Summary.Id, Group("role", thin.Id)));
        var thinClient = await LoginAsync(noData);
        Assert.True(await CanSeeAsync(thinClient, $"{Reports}/{report.Summary.Id}"));
        var run = await ReadAsync<ReportResultDto>(await thinClient.PostAsJsonAsync($"{Reports}/{report.Summary.Id}/run", new RunReportRequest(null, null)));
        Assert.Equal("forbidden", run.Status);
        Assert.Empty(run.Rows);
    }
}
