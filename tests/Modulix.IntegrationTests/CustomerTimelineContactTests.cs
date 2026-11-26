using System.Net;
using System.Net.Http.Json;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Kişi kartındaki zaman çizelgesi (ADR-0059, dilim 1): yalnızca kişiye bağlanabildiğini bildiren CRM kaynakları (aktivite, teklif) çağrılır.
/// Kaynak listesi koda yazılı değildir; yanıttaki <c>sources</c> kayıtlı kaynakların bildiriminden gelir.
/// </summary>
public class CustomerTimelineContactTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Activities = "crm.activities";
    private const string Quotes = "crm.quotes";

    [Fact]
    public async Task Contact_timeline_shows_only_the_contacts_activities_and_quotes_and_lists_the_sources_that_support_persons()
    {
        var (_, admin) = await TenantWithAdminAsync();
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Kişi Lojistik");
        var ali = await CreateContactAsync(client, "Ali", "Veli", company.Id);
        var ayse = await CreateContactAsync(client, "Ayşe", "Kaya", company.Id);
        var aliActivity = await CreateActivityAsync(client, "Ali ile görüşme", company.Id, ali.Id);
        var ayseActivity = await CreateActivityAsync(client, "Ayşe ile görüşme", company.Id, ayse.Id);
        var aliQuote = await CreateQuoteAsync(client, company.Id, ali.Id);
        await CreateQuoteAsync(client, company.Id, ayse.Id);
        await CreateOpportunityAsync(client, company.Id);

        var timeline = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/contacts/{ali.Id}/timeline?take=50"))!;
        Assert.Contains(timeline.Items, i => i.RecordId == aliActivity.Id);
        Assert.Contains(timeline.Items, i => i.RecordId == aliQuote.Id);
        Assert.DoesNotContain(timeline.Items, i => i.RecordId == ayseActivity.Id);
        Assert.DoesNotContain(timeline.Items, i => i.EntityKey == CrmEntityKeys.Opportunity);
        Assert.Equal([Activities, Quotes], timeline.Sources);
        Assert.Equal(timeline.Items.OrderByDescending(i => i.OccurredAt).Select(i => i.RecordId), timeline.Items.Select(i => i.RecordId));

        // Firma çizelgesi değişmez: iki kişinin kayıtları ve fırsat orada durur.
        var companyView = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=50"))!;
        Assert.Contains(companyView.Items, i => i.RecordId == ayseActivity.Id);
        Assert.Contains(companyView.Items, i => i.EntityKey == CrmEntityKeys.Opportunity);

        // Özet şeridi aynı kaynaklardan beslenir: açık iş yalnızca kişinin aktivitesini sayar.
        var summary = (await client.GetFromJsonAsync<CustomerTimelineSummaryDto>($"/api/crm/contacts/{ali.Id}/timeline/summary"))!;
        Assert.DoesNotContain(summary.Open, o => o.Source == "crm.opportunities");
        Assert.Equal(1, summary.Open.Single(o => o.Source == Activities).Count);
    }

    [Fact]
    public async Task Contact_timeline_works_for_a_contact_without_a_company_and_validates_the_query()
    {
        var (_, admin) = await TenantWithAdminAsync();
        using var client = await LoginAsync(admin);
        var loner = await CreateContactAsync(client, "Firmasız", "Kişi", null);
        var activity = await CreateActivityAsync(client, "Firmasız görüşme", null, loner.Id);

        var timeline = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/contacts/{loner.Id}/timeline"))!;
        Assert.Equal(activity.Id, Assert.Single(timeline.Items).RecordId);

        // Kişiyi desteklemeyen kaynak (fırsat) kişi çizelgesinde tanınmaz; sayı sınırı aşılamaz; olmayan kişi bulunamaz.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/crm/contacts/{loner.Id}/timeline?sources=crm.opportunities")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/crm/contacts/{loner.Id}/timeline?take=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/crm/contacts/{Guid.NewGuid()}/timeline")).StatusCode);
    }

    [Fact]
    public async Task Another_tenants_contact_is_not_found_and_does_not_leak_its_rows()
    {
        var (_, adminA) = await TenantWithAdminAsync();
        var (_, adminB) = await TenantWithAdminAsync();
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var contactA = await CreateContactAsync(clientA, "A", "Kişisi", null);
        await CreateActivityAsync(clientA, "A kaydı", null, contactA.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/crm/contacts/{contactA.Id}/timeline")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/crm/contacts/{contactA.Id}/timeline/summary")).StatusCode);
    }

    [Fact]
    public async Task Sources_the_caller_cannot_read_are_missing_and_a_user_without_contact_permission_is_refused()
    {
        var (tenant, admin) = await TenantWithAdminAsync();
        using var adminClient = await LoginAsync(admin);
        var company = await CreateCompanyAsync(adminClient, "Yetki Lojistik");
        var contact = await CreateContactAsync(adminClient, "Yetki", "Kişisi", company.Id);
        await CreateActivityAsync(adminClient, "Görüşme", company.Id, contact.Id);
        await CreateQuoteAsync(adminClient, company.Id, contact.Id);

        // Kişiyi okuyabilir ama yalnızca aktiviteleri: teklif kaynağı yanıtta yoktur.
        var activitiesOnly = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ContactsRead, ActivitiesRead, CompaniesRead], Scope.All, activitiesOnly);
        using var activitiesClient = await LoginAsync(activitiesOnly);
        var view = (await activitiesClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/contacts/{contact.Id}/timeline?take=50"))!;
        Assert.Equal([Activities], view.Sources);
        Assert.DoesNotContain(view.Items, i => i.EntityKey == CrmEntityKeys.Quote);

        // Kişi okuma izni olmayan kullanıcı uca giremez.
        var noContacts = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, ActivitiesRead], Scope.All, noContacts);
        using var noContactsClient = await LoginAsync(noContacts);
        Assert.Equal(HttpStatusCode.Forbidden, (await noContactsClient.GetAsync($"/api/crm/contacts/{contact.Id}/timeline")).StatusCode);
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

    private static async Task<ContactDto> CreateContactAsync(HttpClient client, string first, string last, Guid? companyId)
    {
        var response = await client.PostAsJsonAsync("/api/crm/contacts", new SaveContactRequest(first, last, companyId));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    private static async Task<ActivityDto> CreateActivityAsync(HttpClient client, string subject, Guid? companyId, Guid contactId)
    {
        var response = await client.PostAsJsonAsync("/api/crm/activities", new SaveActivityRequest(subject, DueAt: DateTimeOffset.UtcNow.AddDays(1), CompanyId: companyId, ContactId: contactId));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ActivityDto>())!;
    }

    private static async Task<QuoteDto> CreateQuoteAsync(HttpClient client, Guid companyId, Guid contactId)
    {
        var response = await client.PostAsJsonAsync("/api/crm/quotes", new SaveQuoteRequest("Kişi teklifi", companyId, ContactId: contactId, ValidUntil: DateTimeOffset.UtcNow.AddDays(30)));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<QuoteDto>())!;
    }

    private static async Task CreateOpportunityAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync("/api/crm/opportunities", new SaveOpportunityRequest("Firma fırsatı", companyId, Amount: 1_000m, Currency: "TRY"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
