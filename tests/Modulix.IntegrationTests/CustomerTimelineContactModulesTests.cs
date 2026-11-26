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
/// Kişi çizelgesinde CRM dışı kaynaklar (ADR-0059, dilim 2): destek talebi kişiye ortak kişi kimliğiyle bağlanır (<c>personId</c>); kaynak kendini
/// <c>SupportsPerson</c> ile bildirdiği için kişi çizelgesine kendiliğinden girer. Sipariş, sözleşme ve pazarlama kaynakları aynı bildirimi kullanır.
/// </summary>
public class CustomerTimelineContactModulesTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Tickets = "support.tickets";

    [Fact]
    public async Task Contact_timeline_includes_only_that_persons_tickets_and_the_source_is_listed_by_its_own_declaration()
    {
        var (_, admin) = await TenantWithAdminAsync(support: true);
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Talep Lojistik");
        var ali = await CreateContactAsync(client, "Ali", "Veli", company.Id);
        var ayse = await CreateContactAsync(client, "Ayşe", "Kaya", company.Id);
        var aliTicket = await CreateTicketAsync(client, company.PartyId!.Value, ali.PersonId!.Value, "Ali'nin talebi");
        await CreateTicketAsync(client, company.PartyId!.Value, ayse.PersonId!.Value, "Ayşe'nin talebi");
        await CreateTicketAsync(client, company.PartyId!.Value, null, "Kişisiz talep");

        var timeline = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/contacts/{ali.Id}/timeline?take=50"))!;
        Assert.Contains(Tickets, timeline.Sources);
        var row = Assert.Single(timeline.Items, i => i.EntityKey == SupportAccess.TicketEntityKey);
        Assert.Equal(aliTicket.Id, row.RecordId);

        // Özet şeridi destek kaynağının açık iş sayısını kişiye göre verir.
        var summary = (await client.GetFromJsonAsync<CustomerTimelineSummaryDto>($"/api/crm/contacts/{ali.Id}/timeline/summary"))!;
        Assert.Equal(1, summary.Open.Single(o => o.Source == Tickets).Count);

        // Firma çizelgesi üç talebi de gösterir: kişi bağı kayıtları firmadan ayırmaz.
        var companyView = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=50"))!;
        Assert.Equal(3, companyView.Items.Count(i => i.EntityKey == SupportAccess.TicketEntityKey));
    }

    [Fact]
    public async Task Contact_timeline_has_no_ticket_source_when_the_module_is_off_or_the_permission_is_missing()
    {
        var (_, adminOff) = await TenantWithAdminAsync(support: false);
        using var offClient = await LoginAsync(adminOff);
        var offContact = await CreateContactAsync(offClient, "Modülsüz", "Kişi", null);
        var off = (await offClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/contacts/{offContact.Id}/timeline"))!;
        Assert.DoesNotContain(Tickets, off.Sources);

        var (tenant, admin) = await TenantWithAdminAsync(support: true);
        using var adminClient = await LoginAsync(admin);
        var company = await CreateCompanyAsync(adminClient, "Yetki Lojistik");
        var contact = await CreateContactAsync(adminClient, "Yetki", "Kişisi", company.Id);
        await CreateTicketAsync(adminClient, company.PartyId!.Value, contact.PersonId!.Value, "Gizli talep");

        var noTickets = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ContactsRead, ActivitiesRead], Scope.All, noTickets);
        using var noTicketsClient = await LoginAsync(noTickets);
        var hidden = (await noTicketsClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/contacts/{contact.Id}/timeline"))!;
        Assert.DoesNotContain(Tickets, hidden.Sources);
        Assert.DoesNotContain(hidden.Items, i => i.EntityKey == SupportAccess.TicketEntityKey);

        var reader = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ContactsRead, SupportAccess.TicketsRead], Scope.All, reader);
        using var readerClient = await LoginAsync(reader);
        var visible = (await readerClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/contacts/{contact.Id}/timeline"))!;
        Assert.Single(visible.Items, i => i.EntityKey == SupportAccess.TicketEntityKey);
    }

    [Fact]
    public async Task Another_tenants_ticket_never_appears_on_a_contact_timeline()
    {
        var (_, adminA) = await TenantWithAdminAsync(support: true);
        var (_, adminB) = await TenantWithAdminAsync(support: true);
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var companyA = await CreateCompanyAsync(clientA, "A Tenant A.Ş.");
        var contactA = await CreateContactAsync(clientA, "A", "Kişisi", companyA.Id);
        await CreateTicketAsync(clientA, companyA.PartyId!.Value, contactA.PersonId!.Value, "A'nın talebi");
        var contactB = await CreateContactAsync(clientB, "B", "Kişisi", null);

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/crm/contacts/{contactA.Id}/timeline")).StatusCode);
        var own = (await clientB.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/contacts/{contactB.Id}/timeline"))!;
        Assert.Contains(Tickets, own.Sources);
        Assert.Empty(own.Items);
    }

    private async Task<(Core.Platform.Tenant Tenant, ModulixUser Admin)> TenantWithAdminAsync(bool support)
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        if (support)
        {
            await factory.SetModuleAsync(tenant.Id, "support", enabled: true);
        }

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

    private static async Task<ContactDto> CreateContactAsync(HttpClient client, string first, string last, Guid? companyId)
    {
        var response = await client.PostAsJsonAsync("/api/crm/contacts", new SaveContactRequest(first, last, companyId));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    private static async Task<TicketDto> CreateTicketAsync(HttpClient client, Guid partyId, Guid? personId, string subject)
    {
        var response = await client.PostAsJsonAsync("/api/support/tickets", new SaveTicketRequest(Subject: subject, PartyId: partyId, PersonId: personId));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TicketDto>())!;
    }
}
