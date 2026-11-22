using System.Net;
using System.Net.Http.Json;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Müşteri zaman çizelgesinin durum değişikliği satırları (ADR-0053, dilim 4): fırsatın kazanılması/kaybedilmesi, teklifin kabulü ve müşteri adayının
/// bu firmaya dönüşmesi. Satırlar kaydın kendi izni ve veri kapsamıyla gelir; görünmeyen kaydın geçmişi çizelgeye girmez.
/// </summary>
public class CustomerTimelineStatusTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Leads = "crm.leads";

    [Fact]
    public async Task Timeline_shows_won_and_lost_opportunities_accepted_quotes_and_converted_leads_next_to_the_records()
    {
        var (_, admin) = await TenantWithAdminAsync();
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Durum A.Ş.");

        var won = await CreateOpportunityAsync(client, company.Id, "Kazanılan fırsat");
        var lost = await CreateOpportunityAsync(client, company.Id, "Kaybedilen fırsat");
        var open = await CreateOpportunityAsync(client, company.Id, "Açık fırsat");
        await WinAsync(client, won);
        var loseResponse = await client.PostAsJsonAsync($"/api/crm/opportunities/{lost.Id}/lose", new LoseOpportunityRequestDto(lost.Version, "Price"));
        Assert.True(loseResponse.StatusCode == HttpStatusCode.OK, await loseResponse.Content.ReadAsStringAsync());
        var quote = await AcceptedQuoteAsync(client, company.Id);
        var lead = await ConvertedLeadAsync(client, company.Id);

        var timeline = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=50"))!;

        // Kayıt satırları yerinde; üstüne durum satırları gelir.
        Assert.Contains(timeline.Items, i => i.EntityKey == CrmEntityKeys.Opportunity && i.RecordId == open.Id);
        var closed = timeline.Items.Where(i => i.EntityKey == CrmTimelineKeys.OpportunityClosed).ToList();
        Assert.Equal([(won.Id, "Won"), (lost.Id, "Lost")], closed.OrderBy(i => i.Status == "Won" ? 0 : 1).Select(i => (i.RecordId, i.Status!)));
        Assert.All(closed, i => Assert.Equal(i.Status, i.Type));
        var accepted = Assert.Single(timeline.Items, i => i.EntityKey == CrmTimelineKeys.QuoteAccepted);
        Assert.Equal((quote.Id, "Accepted", quote.QuoteNumber), (accepted.RecordId, accepted.Status, accepted.Reference));
        var converted = Assert.Single(timeline.Items, i => i.EntityKey == CrmTimelineKeys.LeadConverted);
        Assert.Equal(lead.Id, converted.RecordId);
        Assert.Contains("Mehmet Kaya", converted.Title);
        Assert.Contains(Leads, timeline.Sources);

        // Açık fırsat kapanış satırı üretmez; satırlar en yeniden eskiye sıralıdır.
        Assert.DoesNotContain(closed, i => i.RecordId == open.Id);
        Assert.Equal(timeline.Items.OrderByDescending(i => i.OccurredAt).Select(i => i.RecordId), timeline.Items.Select(i => i.RecordId));

        // Kaynak süzgeci durum satırlarını da taşır: adaylar yalnızca dönüşüm satırıdır.
        var leadsOnly = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?sources={Leads}"))!;
        Assert.Equal([CrmTimelineKeys.LeadConverted], leadsOnly.Items.Select(i => i.EntityKey));
    }

    [Fact]
    public async Task Rows_follow_the_opportunity_history_and_other_companies_conversions_are_not_included()
    {
        var (_, admin) = await TenantWithAdminAsync();
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Hedef A.Ş.");
        var other = await CreateCompanyAsync(client, "Diğer Ltd.");
        var opp = await CreateOpportunityAsync(client, company.Id, "İptal edilen fırsat");

        // İptal ve açık fırsat durum satırı üretmez; başka firmaya dönüşen aday bu çizelgede yoktur.
        var cancel = await client.PostAsJsonAsync($"/api/crm/opportunities/{opp.Id}/cancel", new CancelOpportunityRequest(opp.Version, "Vazgeçildi"));
        Assert.True(cancel.StatusCode == HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        await ConvertedLeadAsync(client, other.Id);

        var timeline = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=50"))!;
        Assert.DoesNotContain(timeline.Items, i => i.EntityKey is CrmTimelineKeys.OpportunityClosed or CrmTimelineKeys.QuoteAccepted or CrmTimelineKeys.LeadConverted);
        Assert.Contains(timeline.Items, i => i.EntityKey == CrmEntityKeys.Opportunity && i.RecordId == opp.Id);
        Assert.Empty((await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?sources={Leads}"))!.Items);
    }

    [Fact]
    public async Task Status_rows_never_cross_tenants()
    {
        var (_, adminA) = await TenantWithAdminAsync();
        var (_, adminB) = await TenantWithAdminAsync();
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var companyA = await CreateCompanyAsync(clientA, "A Tenant A.Ş.");
        var opp = await CreateOpportunityAsync(clientA, companyA.Id, "A'nın fırsatı");
        await WinAsync(clientA, opp);
        await ConvertedLeadAsync(clientA, companyA.Id);
        var companyB = await CreateCompanyAsync(clientB, "B Tenant A.Ş.");

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/crm/companies/{companyA.Id}/timeline")).StatusCode);
        var own = (await clientB.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{companyB.Id}/timeline?take=50"))!;
        Assert.Empty(own.Items);
    }

    [Fact]
    public async Task Status_rows_follow_each_records_permission_and_data_scope()
    {
        var (tenant, admin) = await TenantWithAdminAsync();
        using var adminClient = await LoginAsync(admin);
        var company = await CreateCompanyAsync(adminClient, "Yetki A.Ş.");
        var opp = await CreateOpportunityAsync(adminClient, company.Id, "Yöneticinin fırsatı");
        await WinAsync(adminClient, opp);
        await AcceptedQuoteAsync(adminClient, company.Id);
        await ConvertedLeadAsync(adminClient, company.Id);

        // Yalnızca fırsat okuma izni: kapanış satırı gelir; teklif kabulü ve aday dönüşümü kaynakları okunamadığından yoktur.
        var oppOnly = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, OpportunitiesRead], Scope.All, oppOnly);
        using var oppOnlyClient = await LoginAsync(oppOnly);
        var view = (await oppOnlyClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=50"))!;
        Assert.Contains(view.Items, i => i.EntityKey == CrmTimelineKeys.OpportunityClosed);
        Assert.DoesNotContain(view.Items, i => i.EntityKey is CrmTimelineKeys.QuoteAccepted or CrmTimelineKeys.LeadConverted);
        Assert.DoesNotContain(Leads, view.Sources);

        // Kapsam "kendi": başkasının fırsatının kapanışı (ve kaydı) görünmez; kaynak yine okunabilir.
        var own = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, CompaniesWrite, OpportunitiesRead], Scope.Own, own);
        using var ownClient = await LoginAsync(own);
        var ownCompany = await CreateCompanyAsync(ownClient, "Temsilcinin firması");
        var foreign = await CreateOpportunityAsync(adminClient, ownCompany.Id, "Başkasının fırsatı");
        await WinAsync(adminClient, foreign);
        var scoped = (await ownClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{ownCompany.Id}/timeline?take=50"))!;
        Assert.Contains("crm.opportunities", scoped.Sources);
        Assert.Empty(scoped.Items);
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

    private static async Task<OpportunityDto> CreateOpportunityAsync(HttpClient client, Guid companyId, string title)
    {
        var response = await client.PostAsJsonAsync("/api/crm/opportunities", new SaveOpportunityRequest(title, companyId, Amount: 1_000m, Currency: "TRY"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OpportunityDto>())!;
    }

    private static async Task WinAsync(HttpClient client, OpportunityDto opportunity)
    {
        var response = await client.PostAsJsonAsync($"/api/crm/opportunities/{opportunity.Id}/win", new WinOpportunityRequestDto(opportunity.Version));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Satırlı, sunulmuş ve kabul edilmiş teklif.</summary>
    private static async Task<QuoteDto> AcceptedQuoteAsync(HttpClient client, Guid companyId)
    {
        var created = await client.PostAsJsonAsync("/api/crm/quotes", new SaveQuoteRequest("Bakım teklifi", companyId, ValidUntil: DateTimeOffset.UtcNow.AddDays(30)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var quote = (await created.Content.ReadFromJsonAsync<QuoteDto>())!;
        var line = await client.PostAsJsonAsync("/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, "Kalem", "Free", null, 1m, 100m, null, "None", 0m, 0m, null, "Standard"));
        Assert.True(line.StatusCode == HttpStatusCode.Created, await line.Content.ReadAsStringAsync());
        var presented = await TransitionAsync(client, quote.Id, "present", (await client.GetFromJsonAsync<QuoteDto>($"/api/crm/quotes/{quote.Id}"))!.Version);
        return await TransitionAsync(client, quote.Id, "record-accepted", presented.Version);
    }

    private static async Task<QuoteDto> TransitionAsync(HttpClient client, Guid id, string action, long version)
    {
        var response = await client.PostAsJsonAsync($"/api/crm/quotes/{id}/{action}", new QuoteTransitionRequest(version));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<QuoteDto>())!;
    }

    /// <summary>Nitelendirilip mevcut firmaya dönüştürülen müşteri adayı.</summary>
    private static async Task<LeadDto> ConvertedLeadAsync(HttpClient client, Guid companyId)
    {
        var created = await client.PostAsJsonAsync("/api/crm/leads",
            new SaveLeadRequest($"Talep {Guid.NewGuid():N}"[..16], FirstName: "Mehmet", LastName: "Kaya", CompanyName: "Kapsam Lojistik", Email: "kaya@kapsam.test", NeedDescription: "Yıllık bakım ihtiyacı"));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var lead = (await created.Content.ReadFromJsonAsync<LeadDto>())!;
        var qualified = await client.PostAsJsonAsync($"/api/crm/leads/{lead.Id}/qualify", new QualifyLeadRequest(lead.Version));
        Assert.True(qualified.IsSuccessStatusCode, await qualified.Content.ReadAsStringAsync());
        lead = (await qualified.Content.ReadFromJsonAsync<LeadDto>())!;
        var converted = await client.PostAsJsonAsync($"/api/crm/leads/{lead.Id}/convert", new ConvertLeadRequest(lead.Version, CompanyId: companyId));
        Assert.True(converted.StatusCode == HttpStatusCode.OK, await converted.Content.ReadAsStringAsync());
        return lead;
    }
}
