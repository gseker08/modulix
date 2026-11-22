using System.Net;
using System.Net.Http.Json;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Support;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Müşteri zaman çizelgesinin kaynak süzgeci (ADR-0053, dilim 3): <c>?sources=</c> yalnızca istenen kaynakları okur; süzgeç yetki, veri
/// kapsamı ve tenant sınırını genişletmez.
/// </summary>
public class CustomerTimelineFilterTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string OpportunitiesSource = "crm.opportunities";
    private const string TicketsSource = "support.tickets";

    [Fact]
    public async Task Filter_returns_only_the_chosen_sources_and_pages_within_them()
    {
        var (_, admin) = await TenantWithAdminAsync();
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Süzgeç A.Ş.");
        await CreateOpportunityAsync(client, company.Id, "Fırsat 1");
        await CreateTicketAsync(client, company.PartyId!.Value, "Talep 1");
        await CreateOpportunityAsync(client, company.Id, "Fırsat 2");
        await CreateTicketAsync(client, company.PartyId!.Value, "Talep 2");

        var all = await TimelineAsync(client, company.Id, "");
        Assert.Equal(4, all.Items.Count);

        var tickets = await TimelineAsync(client, company.Id, $"?sources={TicketsSource}");
        Assert.All(tickets.Items, i => Assert.Equal(SupportAccess.TicketEntityKey, i.EntityKey));
        Assert.Equal(["Talep 2", "Talep 1"], tickets.Items.Select(i => i.Title));
        Assert.Equal([TicketsSource], tickets.Sources);

        // Birden çok kaynak birleşir; sayfalama süzülmüş akışta ilerler.
        var both = $"sources={OpportunitiesSource},{TicketsSource}";
        var first = await TimelineAsync(client, company.Id, $"?{both}&take=3");
        Assert.Equal(3, first.Items.Count);
        var before = Uri.EscapeDataString(first.NextBefore!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var second = await TimelineAsync(client, company.Id, $"?{both}&take=3&before={before}");
        Assert.Single(second.Items);
        Assert.Null(second.NextBefore);
    }

    [Fact]
    public async Task An_unknown_source_is_rejected_and_an_empty_filter_means_all()
    {
        var (_, admin) = await TenantWithAdminAsync();
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Doğrulama A.Ş.");
        await CreateOpportunityAsync(client, company.Id, "Fırsat");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/crm/companies/{company.Id}/timeline?sources=yok.boyle")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/crm/companies/{company.Id}/timeline?sources={TicketsSource},yok.boyle")).StatusCode);
        Assert.Single((await TimelineAsync(client, company.Id, "?sources=")).Items);
    }

    [Fact]
    public async Task Filter_never_crosses_tenants()
    {
        var (_, adminA) = await TenantWithAdminAsync();
        var (_, adminB) = await TenantWithAdminAsync();
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var companyA = await CreateCompanyAsync(clientA, "A Tenant A.Ş.");
        await CreateTicketAsync(clientA, companyA.PartyId!.Value, "A'nın talebi");
        var companyB = await CreateCompanyAsync(clientB, "B Tenant A.Ş.");

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/crm/companies/{companyA.Id}/timeline?sources={TicketsSource}")).StatusCode);
        var own = await TimelineAsync(clientB, companyB.Id, $"?sources={TicketsSource}");
        Assert.Empty(own.Items);
    }

    [Fact]
    public async Task Filter_does_not_widen_what_the_caller_may_read()
    {
        var (tenant, admin) = await TenantWithAdminAsync();
        using var adminClient = await LoginAsync(admin);
        var company = await CreateCompanyAsync(adminClient, "Yetki A.Ş.");
        await CreateTicketAsync(adminClient, company.PartyId!.Value, "Gizli talep");

        // Talep okuma izni yok: kaynak istense de ne kaynak listede ne satır var ("kayıt yok" gibi de gösterilmez).
        var crmOnly = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead], Scope.All, crmOnly);
        using var crmOnlyClient = await LoginAsync(crmOnly);
        var asked = await TimelineAsync(crmOnlyClient, company.Id, $"?sources={TicketsSource}");
        Assert.Empty(asked.Sources);
        Assert.Empty(asked.Items);

        // Firma okuma izni olmayan kullanıcı süzgeçle de çizelgeyi açamaz.
        var nobody = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [SupportAccess.TicketsRead], Scope.All, nobody);
        using var nobodyClient = await LoginAsync(nobody);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobodyClient.GetAsync($"/api/crm/companies/{company.Id}/timeline?sources={TicketsSource}")).StatusCode);
    }

    private async Task<(Core.Platform.Tenant Tenant, ModulixUser Admin)> TenantWithAdminAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        await factory.SetModuleAsync(tenant.Id, "support", enabled: true);
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

    private static async Task<CustomerTimelineDto> TimelineAsync(HttpClient client, Guid companyId, string query) =>
        (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{companyId}/timeline{query}"))!;

    private static async Task<CompanyDto> CreateCompanyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/crm/companies", new SaveCompanyRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
    }

    private static async Task CreateOpportunityAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync("/api/crm/opportunities", new SaveOpportunityRequest(name, companyId, Stage: "Lead", Amount: 100m, Currency: "TRY"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task CreateTicketAsync(HttpClient client, Guid partyId, string subject)
    {
        var response = await client.PostAsJsonAsync("/api/support/tickets", new SaveTicketRequest(Subject: subject, PartyId: partyId));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }
}
