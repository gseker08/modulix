using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Müşteri zaman çizelgesi (CRM eksik süreçler E9, dilim 1): <c>GET /api/crm/companies/{id}/timeline</c> firmanın aktivite, fırsat ve tekliflerini
/// tek akışta, en yeniden eskiye verir. Satırlar kaynağın kendi izni ve veri kapsamıyla süzülür; okunamayan kaynak yanıtta yoktur.
/// </summary>
public class CustomerTimelineTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly string[] SalesAccess =
    [
        CompaniesRead, CompaniesWrite, OpportunitiesRead, OpportunitiesWrite, OpportunityAmountsRead, ActivitiesRead, ActivitiesWrite,
    ];

    [Fact]
    public async Task Timeline_merges_activities_opportunities_and_quotes_newest_first_and_pages_by_cursor()
    {
        var (_, admin) = await TenantWithAdminAsync();
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Çizelge A.Ş.");

        // Aktivitenin anı son tarihidir (üç gün önce): bugün açılan fırsat ve tekliften eskidir.
        var activity = await client.PostAsJsonAsync("/api/crm/activities",
            new SaveActivityRequest("Tanışma araması", "Task", DateTimeOffset.UtcNow.AddDays(-3), CompanyId: company.Id));
        Assert.Equal(HttpStatusCode.Created, activity.StatusCode);
        await CreateOpportunityAsync(client, company.Id, "Yıllık bakım");
        var quote = await client.PostAsJsonAsync("/api/crm/quotes", new SaveQuoteRequest("Bakım teklifi", company.Id));
        Assert.Equal(HttpStatusCode.Created, quote.StatusCode);

        // Başka firmanın kaydı çizelgeye girmez.
        var other = await CreateCompanyAsync(client, "Başka Ltd.");
        await CreateOpportunityAsync(client, other.Id, "İlgisiz fırsat");

        var timeline = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.Equal(["crm.activities", "crm.opportunities", "crm.quotes"], timeline.Sources);
        Assert.Equal([CrmEntityKeys.Quote, CrmEntityKeys.Opportunity, CrmEntityKeys.Activity], timeline.Items.Select(i => i.EntityKey));
        Assert.Equal(["Bakım teklifi", "Yıllık bakım", "Tanışma araması"], timeline.Items.Select(i => i.Title));
        Assert.Equal(timeline.Items.OrderByDescending(i => i.OccurredAt).Select(i => i.RecordId), timeline.Items.Select(i => i.RecordId));
        Assert.False(string.IsNullOrEmpty(timeline.Items[0].Reference));
        Assert.All(timeline.Items, i => Assert.Equal(admin.Id, i.OwnerId));
        Assert.Null(timeline.NextBefore);

        // İmleçli sayfalama: iki satır, sonra kalan bir satır; hiçbir satır yinelenmez ya da atlanmaz.
        var first = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=2"))!;
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(first.Items[^1].OccurredAt, first.NextBefore);
        var before = Uri.EscapeDataString(first.NextBefore!.Value.ToString("O", CultureInfo.InvariantCulture));
        var second = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=2&before={before}"))!;
        Assert.Single(second.Items);
        Assert.Null(second.NextBefore);
        Assert.Equal(timeline.Items.Select(i => i.RecordId), first.Items.Concat(second.Items).Select(i => i.RecordId));
    }

    [Fact]
    public async Task Timeline_rejects_an_invalid_page_size_and_an_unknown_company()
    {
        var (_, admin) = await TenantWithAdminAsync();
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Doğrulama A.Ş.");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/crm/companies/{company.Id}/timeline?take=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/crm/companies/{company.Id}/timeline?take={CustomerTimelineDto.MaxTake + 1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/crm/companies/{Guid.NewGuid()}/timeline")).StatusCode);

        // Kaydı olmayan firma: kaynaklar okunabilir, çizelge boştur.
        var empty = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.Empty(empty.Items);
        Assert.Equal(3, empty.Sources.Count);
        Assert.Null(empty.NextBefore);
    }

    [Fact]
    public async Task Timeline_of_another_tenants_company_is_not_found()
    {
        var (_, adminA) = await TenantWithAdminAsync();
        var (_, adminB) = await TenantWithAdminAsync();
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var company = await CreateCompanyAsync(clientA, "A Tenant A.Ş.");
        await CreateOpportunityAsync(clientA, company.Id, "A'nın fırsatı");

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/crm/companies/{company.Id}/timeline")).StatusCode);
        Assert.Single((await clientA.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!.Items);
    }

    [Fact]
    public async Task Timeline_shows_only_the_sources_and_records_the_user_may_read()
    {
        var (tenant, admin) = await TenantWithAdminAsync();
        using var adminClient = await LoginAsync(admin);
        var company = await CreateCompanyAsync(adminClient, "Yetki A.Ş.");
        await CreateOpportunityAsync(adminClient, company.Id, "Yöneticinin fırsatı");
        var activity = await adminClient.PostAsJsonAsync("/api/crm/activities",
            new SaveActivityRequest("Arama", "Task", DateTimeOffset.UtcNow.AddDays(-1), CompanyId: company.Id));
        Assert.Equal(HttpStatusCode.Created, activity.StatusCode);

        // Firma okuma izni yok: uç kapalı.
        var outsider = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ActivitiesRead], Scope.All, outsider);
        using var outsiderClient = await LoginAsync(outsider);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync($"/api/crm/companies/{company.Id}/timeline")).StatusCode);

        // Yalnızca firma izni: hiçbir kaynak okunamaz (boş çizelge, kaynak listesi de boş).
        var onlyCompany = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead], Scope.All, onlyCompany);
        using var onlyCompanyClient = await LoginAsync(onlyCompany);
        var bare = (await onlyCompanyClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.Empty(bare.Items);
        Assert.Empty(bare.Sources);

        // Fırsat izni var, aktivite ve teklif izni yok: yalnızca fırsat satırı.
        var opportunityReader = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, OpportunitiesRead], Scope.All, opportunityReader);
        using var readerClient = await LoginAsync(opportunityReader);
        var partial = (await readerClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.Equal(["crm.opportunities"], partial.Sources);
        Assert.Equal([CrmEntityKeys.Opportunity], partial.Items.Select(i => i.EntityKey));

        // Veri kapsamı "kendi": temsilci kendi firmasında yalnızca kendi fırsatını görür; başka temsilci firmayı hiç göremez.
        var repA = await factory.CreateUserAsync(tenant);
        var repB = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, SalesAccess, Scope.Own, repA, repB);
        using var clientA = await LoginAsync(repA);
        using var clientB = await LoginAsync(repB);
        var own = await CreateCompanyAsync(clientA, "A'nın firması");
        await CreateOpportunityAsync(clientA, own.Id, "A'nın fırsatı");
        await CreateOpportunityAsync(adminClient, own.Id, "Yöneticinin fırsatı", owner: admin.Id);

        Assert.Equal(["A'nın fırsatı"], (await clientA.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{own.Id}/timeline"))!.Items.Select(i => i.Title));
        Assert.Equal(2, (await adminClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{own.Id}/timeline"))!.Items.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/crm/companies/{own.Id}/timeline")).StatusCode);
    }

    private async Task<(Core.Platform.Tenant Tenant, ModulixUser Admin)> TenantWithAdminAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, admin);
        return (tenant, admin);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<CompanyDto> CreateCompanyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/crm/companies", new SaveCompanyRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
    }

    private static async Task CreateOpportunityAsync(HttpClient client, Guid companyId, string title, Guid? owner = null)
    {
        var response = await client.PostAsJsonAsync("/api/crm/opportunities",
            new SaveOpportunityRequest(title, companyId, Stage: "Lead", Amount: 100m, Currency: "TRY", OwnerId: owner));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
