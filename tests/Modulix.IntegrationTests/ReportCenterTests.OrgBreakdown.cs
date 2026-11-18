using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Organization;
using Modulix.Core.Teams;
using Modulix.Shared.Crm;
using Modulix.Shared.Reporting;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0054 dilim 4b: departman ve ekip kırılımı. Rapor, kaydın sorumlusunun bugünkü departmanına ve birincil ekibine (en önce katıldığı ekip)
/// göre gruplanır ve süzülür; her kayıt tek kovaya girer, toplamlar kırılımlarda iki kez sayılmaz. Mutlu yol, doğrulama, tenant izolasyonu ve yetki.
/// </summary>
public partial class ReportCenterTests
{
    /// <summary>Üç sorumlu: ikisi "Satış" departmanında (biri iki ekipte), biri "Destek" departmanında ve ekipsiz; her birinin açık fırsatı vardır.</summary>
    private sealed record OrgData(Guid SalesUnit, Guid SupportUnit, Guid FirstTeam, Guid SecondTeam, IReadOnlyList<Guid> Owners);

    private static ReportFilterDto OwnedBy(IEnumerable<Guid> owners) => new() { Field = "ownerId", Operator = "in", Values = [.. owners.Select(o => o.ToString())] };

    private async Task<OrgData> SeedOrgAsync(Setup s)
    {
        var first = await factory.CreateUserAsync(s.Tenant);
        var second = await factory.CreateUserAsync(s.Tenant);
        var third = await factory.CreateUserAsync(s.Tenant);
        Guid sales;
        Guid support;
        Guid alpha;
        Guid beta;
        using (var scope = factory.OpenScope(s.TenantId))
        {
            var units = scope.ServiceProvider.GetRequiredService<IBusinessUnitService>();
            var root = (await units.ListAsync(CancellationToken.None)).Single(u => u.ParentId is null);
            sales = (await units.CreateAsync(root.Id, "Satış", null, BusinessUnitKinds.Other, CancellationToken.None))!.Unit.Id;
            support = (await units.CreateAsync(root.Id, "Destek", null, BusinessUnitKinds.Other, CancellationToken.None))!.Unit.Id;
            Assert.True(await units.SetMembershipBusinessUnitAsync(await factory.GetMembershipIdAsync(first.Id, s.TenantId), sales, CancellationToken.None));
            Assert.True(await units.SetMembershipBusinessUnitAsync(await factory.GetMembershipIdAsync(second.Id, s.TenantId), sales, CancellationToken.None));
            Assert.True(await units.SetMembershipBusinessUnitAsync(await factory.GetMembershipIdAsync(third.Id, s.TenantId), support, CancellationToken.None));

            // İlk kullanıcı önce Alfa'ya, sonra Beta'ya girer: birincil ekibi Alfa'dır. İkinci kullanıcı yalnız Beta'dadır; üçüncünün ekibi yoktur.
            var teams = scope.ServiceProvider.GetRequiredService<ITeamService>();
            alpha = (await teams.CreateAsync("Alfa", null, CancellationToken.None)).Id;
            beta = (await teams.CreateAsync("Beta", null, CancellationToken.None)).Id;
            var firstMembership = await factory.GetMembershipIdAsync(first.Id, s.TenantId);
            var secondMembership = await factory.GetMembershipIdAsync(second.Id, s.TenantId);
            Assert.NotNull(await teams.SetMembersAsync(alpha, [firstMembership], CancellationToken.None));
            await Task.Delay(20);
            Assert.NotNull(await teams.SetMembersAsync(beta, [firstMembership, secondMembership], CancellationToken.None));
        }

        foreach (var (owner, title) in new[] { (first.Id, "A1"), (first.Id, "A2"), (second.Id, "B1"), (third.Id, "C1") })
        {
            await PostAsync<OpportunityDto>(s.Admin, "/api/crm/opportunities", new SaveOpportunityRequest(
                title, s.CompanyId, Amount: 1000m, Currency: "TRY", OwnerId: owner, ExpectedCloseDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20)));
        }

        return new OrgData(sales, support, alpha, beta, [first.Id, second.Id, third.Id]);
    }

    private static string? Label(ReportResultRowDto row) => row.Cells[0].ValueKind == System.Text.Json.JsonValueKind.Null ? null : row.Cells[0].GetString();

    private static ReportSpecDto CountBy(string group, IEnumerable<Guid> owners, params string[] columns) => new()
    {
        Dataset = Opportunities,
        Columns = [.. columns],
        Filters = [OwnedBy(owners)],
        Groups = [new() { Field = group }],
        Measures = [new() { Aggregation = "count" }],
    };

    [Fact]
    public async Task Records_are_grouped_and_filtered_by_the_owners_unit_and_primary_team_without_double_counting()
    {
        var s = await SetUpAsync();
        var org = await SeedOrgAsync(s);

        var byUnit = await RunAsync(s.Admin, CountBy("ownerUnit", org.Owners));
        Assert.Equal("ok", byUnit.Status);
        Assert.Equal(2, byUnit.Rows.Count);
        Assert.Equal(3m, Cell(byUnit.Rows.Single(r => Label(r)!.EndsWith("Satış", StringComparison.Ordinal)), 1));
        Assert.Equal(1m, Cell(byUnit.Rows.Single(r => Label(r)!.EndsWith("Destek", StringComparison.Ordinal)), 1));
        Assert.Equal(org.SalesUnit.ToString(), byUnit.Rows.Single(r => Label(r)!.EndsWith("Satış", StringComparison.Ordinal)).Keys[0]);

        // Çok ekipli kullanıcının kayıtları yalnız birincil (ilk katıldığı) ekipte sayılır; ekipsiz kullanıcı boş kovadadır; toplam kayıt sayısına eşittir.
        var byTeam = await RunAsync(s.Admin, CountBy("ownerTeam", org.Owners));
        Assert.Equal(3, byTeam.Rows.Count);
        Assert.Equal(2m, Cell(byTeam.Rows.Single(r => Label(r) == "Alfa"), 1));
        Assert.Equal(1m, Cell(byTeam.Rows.Single(r => Label(r) == "Beta"), 1));
        Assert.Equal(1m, Cell(byTeam.Rows.Single(r => Label(r) is null), 1));
        Assert.Equal(4m, byTeam.Rows.Sum(r => Cell(r, 1)));
        Assert.Equal(4m, byUnit.Rows.Sum(r => Cell(r, 1)));

        // Süzgeç ve ayrıntı: türetilmiş sütun tabloda ve süzgeçte de kullanılır.
        var inSupport = await RunAsync(s.Admin, new ReportSpecDto
        {
            Dataset = Opportunities,
            Columns = ["title", "ownerUnit", "ownerTeam"],
            Filters = [OwnedBy(org.Owners), new() { Field = "ownerUnit", Operator = "equals", Value = org.SupportUnit.ToString() }],
        });
        var only = Assert.Single(inSupport.Rows);
        Assert.Equal("C1", only.Cells[0].GetString());
        Assert.EndsWith("Destek", only.Cells[1].GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, only.Cells[2].ValueKind);

        var inBeta = await RunAsync(s.Admin, new ReportSpecDto
        {
            Dataset = Opportunities,
            Columns = ["title"],
            Filters = [OwnedBy(org.Owners), new() { Field = "ownerTeam", Operator = "equals", Value = org.SecondTeam.ToString() }],
        });
        Assert.Equal("B1", Assert.Single(inBeta.Rows).Cells[0].GetString());

        // Özet satırının ayrıntısı, o kovadaki kayıtların aynısıdır.
        var alfa = byTeam.Rows.Single(r => Label(r) == "Alfa");
        var drill = await RunAsync(s.Admin, CountBy("ownerTeam", org.Owners, "title"), new ReportDrillDto(alfa.Keys, alfa.Currency));
        var titleAt = drill.Columns.ToList().FindIndex(c => c.Key == "title");
        Assert.Equal(["A1", "A2"], drill.Rows.Select(r => r.Cells[titleAt].GetString()!).Order().ToArray());

        // Katalog seçenekleri departman yolunu ve ekip adını verir (süzgeç seçicisi için).
        var catalog = await ReadAsync<ReportCatalogDto>(await s.Admin.GetAsync($"{Reports}/catalog"));
        var columns = catalog.Datasets.Single(d => d.Key == Opportunities).Columns;
        Assert.Contains(columns.Single(c => c.Field == "ownerUnit").Options!, o => o.Key == org.SalesUnit.ToString() && o.Label.EndsWith("Satış", StringComparison.Ordinal));
        Assert.Contains(columns.Single(c => c.Field == "ownerTeam").Options!, o => o.Key == org.FirstTeam.ToString() && o.Label == "Alfa");
        Assert.Equal("businessUnit", columns.Single(c => c.Field == "ownerUnit").Kind);
        Assert.Equal("team", columns.Single(c => c.Field == "ownerTeam").Kind);
    }

    [Fact]
    public async Task Org_breakdown_validates_filter_values_and_ready_reports_run()
    {
        var s = await SetUpAsync();
        var org = await SeedOrgAsync(s);

        // Departman/ekip süzgeci kimlik ister; sayısal, tarih ya da serbest metin değer geçersizdir. Ölçü olamaz.
        var badValue = await RunAsync(s.Admin, new ReportSpecDto
        {
            Dataset = Opportunities, Columns = ["title"], Filters = [new() { Field = "ownerUnit", Operator = "equals", Value = "Satış" }],
        });
        Assert.Equal("invalid", badValue.Status);
        var asMeasure = await RunAsync(s.Admin, new ReportSpecDto { Dataset = Opportunities, Measures = [new() { Field = "ownerTeam", Aggregation = "sum" }] });
        Assert.Equal("invalid", asMeasure.Status);

        // Hazır raporlar katalogda ve çalışır.
        var catalog = await ReadAsync<ReportCatalogDto>(await s.Admin.GetAsync($"{Reports}/catalog"));
        var ready = catalog.Datasets.Single(d => d.Key == Opportunities).ReadyReports;
        Assert.Contains(ready, r => r.Key == "crm.opportunities.won-by-unit");
        Assert.Contains(ready, r => r.Key == "crm.opportunities.won-by-team");
        foreach (var report in ready.Where(r => r.Key is "crm.opportunities.won-by-unit" or "crm.opportunities.won-by-team"))
        {
            Assert.Equal("ok", (await RunAsync(s.Admin, report.Spec)).Status);
        }

        Assert.NotEqual(Guid.Empty, org.SalesUnit);
    }

    [Fact]
    public async Task Another_tenants_units_and_teams_never_appear_in_this_tenants_breakdown_or_options()
    {
        var s = await SetUpAsync();
        var org = await SeedOrgAsync(s);
        var other = await SetUpAsync();
        var theirs = await SeedOrgAsync(other);

        var byUnit = await RunAsync(other.Admin, CountBy("ownerUnit", org.Owners));
        Assert.Empty(byUnit.Rows);                                   // bizim sorumlular onların kayıtlarında yok
        var mine = await RunAsync(s.Admin, CountBy("ownerUnit", theirs.Owners));
        Assert.Empty(mine.Rows);

        // Kendi kovaları yalnız kendi kayıtlarını sayar; başka firmanın departman kimliğiyle süzgeç boş döner.
        Assert.Equal(4m, (await RunAsync(other.Admin, CountBy("ownerTeam", theirs.Owners))).Rows.Sum(r => Cell(r, 1)));
        var foreign = await RunAsync(other.Admin, new ReportSpecDto
        {
            Dataset = Opportunities, Columns = ["title"], Filters = [new() { Field = "ownerUnit", Operator = "equals", Value = org.SalesUnit.ToString() }],
        });
        Assert.Empty(foreign.Rows);

        var catalog = await ReadAsync<ReportCatalogDto>(await other.Admin.GetAsync($"{Reports}/catalog"));
        var options = catalog.Datasets.Single(d => d.Key == Opportunities).Columns.Where(c => c.Field is "ownerUnit" or "ownerTeam").SelectMany(c => c.Options!).Select(o => o.Key).ToList();
        Assert.DoesNotContain(org.SalesUnit.ToString(), options);
        Assert.DoesNotContain(org.FirstTeam.ToString(), options);
        Assert.Contains(theirs.SalesUnit.ToString(), options);
    }

    [Fact]
    public async Task Breakdown_follows_the_viewers_record_scope_and_permission()
    {
        var s = await SetUpAsync();
        var org = await SeedOrgAsync(s);

        // Yalnız kendi kayıtlarını görebilen kullanıcı kırılımda başkalarının kaydını göremez.
        var own = await UserAsync(s, [.. DataRead, .. CoreRead], Scope.Own);
        Assert.Empty((await RunAsync(own, CountBy("ownerUnit", org.Owners))).Rows);
        Assert.Empty((await RunAsync(own, CountBy("ownerTeam", org.Owners))).Rows);

        // Fırsat okuma izni olmayan kullanıcı için veri kümesi kapalıdır; kırılım da veri vermez.
        var noOpportunities = await UserAsync(s, [CompaniesRead, .. CoreRead], Scope.All);
        Assert.Equal("forbidden", (await RunAsync(noOpportunities, CountBy("ownerUnit", org.Owners))).Status);

        // İznin olduğu kullanıcı tam kırılımı görür.
        var all = await UserAsync(s, [.. DataRead, .. CoreRead], Scope.All);
        Assert.Equal(4m, (await RunAsync(all, CountBy("ownerTeam", org.Owners))).Rows.Sum(r => Cell(r, 1)));
    }
}
