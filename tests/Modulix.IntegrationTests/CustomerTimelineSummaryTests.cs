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
/// Müşteri zaman çizelgesinin özet şeridi (ADR-0053, dilim 3): son hareket ve kaynak başına açık iş sayısı. Her kaynak çağıranın izni, veri
/// kapsamı ve lisansıyla sorulur; okunamayan kaynak şeritte yoktur (sıfır gibi gösterilmez).
/// </summary>
public class CustomerTimelineSummaryTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Activities = "crm.activities";
    private const string Opportunities = "crm.opportunities";
    private const string Tickets = "support.tickets";

    [Fact]
    public async Task Summary_shows_the_last_past_entry_and_open_counts_per_readable_source()
    {
        var (_, admin) = await TenantWithAdminAsync();
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Özet A.Ş.");
        var before = DateTimeOffset.UtcNow;

        // İki açık aktivite (biri ileri tarihli plan), bir tamamlanmış aktivite; bir açık fırsat; iki açık talep.
        await CreateActivityAsync(client, company.Id, "Geçmiş görev", due: DateTimeOffset.UtcNow.AddDays(-3));
        await CreateActivityAsync(client, company.Id, "Planlı görüşme", due: DateTimeOffset.UtcNow.AddDays(5));
        await CreateActivityAsync(client, company.Id, "Biten görev", due: DateTimeOffset.UtcNow.AddDays(-2), completed: DateTimeOffset.UtcNow.AddDays(-1));
        await CreateOpportunityAsync(client, company.Id, "Yıllık bakım");
        await CreateTicketAsync(client, company.PartyId!.Value, "Talep 1");
        await CreateTicketAsync(client, company.PartyId!.Value, "Talep 2");

        var summary = await SummaryAsync(client, company.Id);

        // Sipariş modülü kapalı: şeritte yoktur. Açık sayılar kaynak anahtarına göre sıralıdır.
        Assert.Equal([(Activities, 2), (Opportunities, 1), (Tickets, 2)], summary.Open.Select(o => (o.Source, o.Count)));

        // Son hareket "şimdi"den önceki en yeni kayıttır: ileri tarihli planlı görüşme sayılmaz, bu testte açılan talep en yenidir.
        Assert.Equal(SupportAccess.TicketEntityKey, summary.LastEntryEntityKey);
        Assert.InRange(summary.LastEntryAt!.Value, before.AddSeconds(-5), DateTimeOffset.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task A_customer_without_records_has_zero_counts_and_no_last_entry_and_an_unknown_company_is_404()
    {
        var (_, admin) = await TenantWithAdminAsync();
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Boş A.Ş.");

        var summary = await SummaryAsync(client, company.Id);

        // Okunabilir ama kayıt yok: sayılar sıfır, son hareket yok.
        Assert.Null(summary.LastEntryAt);
        Assert.Null(summary.LastEntryEntityKey);
        Assert.Equal([(Activities, 0), (Opportunities, 0), (Tickets, 0)], summary.Open.Select(o => (o.Source, o.Count)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/crm/companies/{Guid.NewGuid()}/timeline/summary")).StatusCode);
    }

    [Fact]
    public async Task Summary_never_counts_another_tenants_records()
    {
        var (_, adminA) = await TenantWithAdminAsync();
        var (_, adminB) = await TenantWithAdminAsync();
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var companyA = await CreateCompanyAsync(clientA, "A Tenant A.Ş.");
        await CreateTicketAsync(clientA, companyA.PartyId!.Value, "A'nın talebi");
        await CreateOpportunityAsync(clientA, companyA.Id, "A'nın fırsatı");
        var companyB = await CreateCompanyAsync(clientB, "B Tenant A.Ş.");

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/crm/companies/{companyA.Id}/timeline/summary")).StatusCode);
        var own = await SummaryAsync(clientB, companyB.Id);
        Assert.Null(own.LastEntryAt);
        Assert.All(own.Open, o => Assert.Equal(0, o.Count));
    }

    [Fact]
    public async Task Summary_only_includes_sources_the_caller_may_read()
    {
        var (tenant, admin) = await TenantWithAdminAsync();
        using var adminClient = await LoginAsync(admin);
        var company = await CreateCompanyAsync(adminClient, "Yetki A.Ş.");
        await CreateOpportunityAsync(adminClient, company.Id, "Gizli fırsat");
        await CreateTicketAsync(adminClient, company.PartyId!.Value, "Gizli talep");

        // Yalnızca firma okuma izni: hiçbir kaynak okunamaz; şerit boştur ("0" gibi gösterilmez).
        var crmOnly = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead], Scope.All, crmOnly);
        using var crmOnlyClient = await LoginAsync(crmOnly);
        var none = await SummaryAsync(crmOnlyClient, company.Id);
        Assert.Null(none.LastEntryAt);
        Assert.Empty(none.Open);

        // Talep okuma izni: yalnızca talep kaynağı şeritte, fırsat ve aktivite yok.
        var tickets = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, SupportAccess.TicketsRead], Scope.All, tickets);
        using var ticketsClient = await LoginAsync(tickets);
        var some = await SummaryAsync(ticketsClient, company.Id);
        Assert.Equal([(Tickets, 1)], some.Open.Select(o => (o.Source, o.Count)));
        Assert.Equal(SupportAccess.TicketEntityKey, some.LastEntryEntityKey);

        // Firma okuma izni olmayan kullanıcı şeridi de açamaz.
        var nobody = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [SupportAccess.TicketsRead], Scope.All, nobody);
        using var nobodyClient = await LoginAsync(nobody);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobodyClient.GetAsync($"/api/crm/companies/{company.Id}/timeline/summary")).StatusCode);
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

    private static async Task<CustomerTimelineSummaryDto> SummaryAsync(HttpClient client, Guid companyId) =>
        (await client.GetFromJsonAsync<CustomerTimelineSummaryDto>($"/api/crm/companies/{companyId}/timeline/summary"))!;

    private static async Task<CompanyDto> CreateCompanyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/crm/companies", new SaveCompanyRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
    }

    private static async Task CreateActivityAsync(HttpClient client, Guid companyId, string subject, DateTimeOffset due, DateTimeOffset? completed = null)
    {
        var response = await client.PostAsJsonAsync("/api/crm/activities", new SaveActivityRequest(subject, "Task", due, completed, CompanyId: companyId));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
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
