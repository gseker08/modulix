using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Modulix.Infrastructure.Identity;
using Modulix.Modules.Crm.Leads;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Records;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Müşteri adayı puanı (ADR-0050, dilim 1): kurallar genel kayıt uçlarında, puan aday kaydedilirken hesaplanır; istemci puan yazamaz;
/// puana göre sıralama ve "en az puan" süzgeci; kural doğrulaması; tenant izolasyonu; kural yönetimi için ayrı izin.
/// </summary>
public class LeadScoringTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string RulesUrl = "/api/records/" + LeadScoreCatalog.EntityKey;
    private const string LeadsUrl = "/api/crm/leads";

    [Fact]
    public async Task A_lead_is_scored_on_save_and_rescored_when_it_changes_and_the_client_cannot_write_the_score()
    {
        var (_, admin) = await SetUpAsync();
        using var client = await LoginAsync(admin);
        await CreateRuleAsync(client, "Türkiye", "country", LeadScoreCatalog.EqualsTo, "tr", 30);
        await CreateRuleAsync(client, "E-postası var", "email", LeadScoreCatalog.IsSet, null, 20);
        await CreateRuleAsync(client, "Yönetici", "jobTitle", LeadScoreCatalog.Contains, "müdür", 25);

        var lead = await CreateLeadAsync(client, new SaveLeadRequest("Depo talebi", LastName: "Kaya", Email: "kaya@ornek.com", Country: "TR", JobTitle: "Satın Alma Müdürü"));
        Assert.Equal(75, lead.Score);

        // E-posta silinince puan düşer; istemciden gelen puan yok sayılır.
        var update = await client.PutAsJsonAsync($"{LeadsUrl}/{lead.Id}", new SaveLeadRequest(
            "Depo talebi", LastName: "Kaya", Country: "TR", JobTitle: "Satın Alma Müdürü", Version: lead.Version));
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
        Assert.Equal(55, (await update.Content.ReadFromJsonAsync<LeadDto>())!.Score);

        var forged = await client.PostAsJsonAsync($"/api/records/crm.lead", new RecordWriteRequest(new JsonObject
        {
            ["subject"] = "Sahte puan", ["lastName"] = "Ak", ["score"] = 99,
        }));
        Assert.Equal(HttpStatusCode.Created, forged.StatusCode);
        Assert.Equal(0, (await forged.Content.ReadFromJsonAsync<RecordResponse>())!.Values["score"]!.GetValue<int>());
    }

    [Fact]
    public async Task Leads_can_be_sorted_by_score_and_filtered_by_a_minimum_score()
    {
        var (_, admin) = await SetUpAsync();
        using var client = await LoginAsync(admin);
        await CreateRuleAsync(client, "Türkiye", "country", LeadScoreCatalog.EqualsTo, "TR", 40);
        await CreateRuleAsync(client, "Telefon", "phone", LeadScoreCatalog.IsSet, null, 30);
        await CreateLeadAsync(client, new SaveLeadRequest("Sıfır", LastName: "A", Country: "DE"));
        await CreateLeadAsync(client, new SaveLeadRequest("Kırk", LastName: "B", Country: "TR"));
        await CreateLeadAsync(client, new SaveLeadRequest("Yetmiş", LastName: "C", Country: "TR", Phone: "+90 532 000 00 00"));

        Assert.Equal(["Yetmiş", "Kırk", "Sıfır"], await SubjectsAsync(client, "&sort=-score"));
        Assert.Equal(["Sıfır", "Kırk", "Yetmiş"], await SubjectsAsync(client, "&sort=score"));
        Assert.Equal(["Yetmiş", "Kırk"], await SubjectsAsync(client, "&sort=-score&minScore=40"));
    }

    [Fact]
    public async Task Invalid_rules_are_rejected_with_field_errors()
    {
        var (_, admin) = await SetUpAsync();
        using var client = await LoginAsync(admin);

        await AssertInvalidAsync(client, "points", Rule("Sıfır puan", "country", LeadScoreCatalog.EqualsTo, "TR", 0));
        await AssertInvalidAsync(client, "points", Rule("Çok puan", "country", LeadScoreCatalog.EqualsTo, "TR", 150));
        await AssertInvalidAsync(client, "field", Rule("Bütçe", "estimatedBudget", LeadScoreCatalog.IsSet, null, 10));
        await AssertInvalidAsync(client, "operator", Rule("E-posta içerir", "email", LeadScoreCatalog.Contains, "ornek", 10));
        await AssertInvalidAsync(client, "value", Rule("Boş değer", "country", LeadScoreCatalog.EqualsTo, " ", 10));
    }

    [Fact]
    public async Task Another_tenant_neither_sees_the_rules_nor_is_scored_by_them()
    {
        var (_, adminA) = await SetUpAsync();
        var (_, adminB) = await SetUpAsync();
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var rule = await CreateRuleAsync(clientA, "Türkiye", "country", LeadScoreCatalog.EqualsTo, "TR", 50);

        var rulesB = (await clientB.GetFromJsonAsync<RecordPageResponse>(RulesUrl))!;
        Assert.Empty(rulesB.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"{RulesUrl}/{rule.Id}")).StatusCode);

        Assert.Equal(0, (await CreateLeadAsync(clientB, new SaveLeadRequest("B adayı", LastName: "B", Country: "TR"))).Score);
        Assert.Equal(50, (await CreateLeadAsync(clientA, new SaveLeadRequest("A adayı", LastName: "A", Country: "TR"))).Score);
    }

    [Fact]
    public async Task Managing_rules_needs_its_own_permission_but_every_lead_is_still_scored()
    {
        var (tenant, admin) = await SetUpAsync();
        using var adminClient = await LoginAsync(admin);
        await CreateRuleAsync(adminClient, "Türkiye", "country", LeadScoreCatalog.EqualsTo, "TR", 35);

        var rep = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [LeadsRead, LeadsCreate, LeadsUpdate], Scope.Own, rep);
        using var repClient = await LoginAsync(rep);

        Assert.Equal(HttpStatusCode.Forbidden, (await repClient.GetAsync(RulesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await repClient.PostAsJsonAsync(RulesUrl, Rule("Kaçak", "country", LeadScoreCatalog.EqualsTo, "TR", 90))).StatusCode);
        Assert.Equal(35, (await CreateLeadAsync(repClient, new SaveLeadRequest("Temsilci adayı", LastName: "T", Country: "TR"))).Score);
    }

    [Fact]
    public void The_screen_catalog_matches_the_server_white_list()
    {
        Assert.Equal(LeadScoring.Fields.Select(f => f.Key), LeadScoreCatalog.Fields.Select(f => f.Key));
        foreach (var field in LeadScoring.Fields)
        {
            Assert.Equal(LeadScoring.OperatorsFor(field.Kind), LeadScoreCatalog.Find(field.Key)!.Operators);
            Assert.Equal(field.Label, LeadScoreCatalog.Find(field.Key)!.Label);
        }
    }

    private static RecordWriteRequest Rule(string name, string field, string op, string? value, int points) => new(new JsonObject
    {
        ["name"] = name, ["field"] = field, ["operator"] = op, ["value"] = value, ["points"] = points, ["isActive"] = true,
    });

    private static async Task<RecordResponse> CreateRuleAsync(HttpClient client, string name, string field, string op, string? value, int points)
    {
        var response = await client.PostAsJsonAsync(RulesUrl, Rule(name, field, op, value, points));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    private static async Task AssertInvalidAsync(HttpClient client, string field, RecordWriteRequest request)
    {
        var response = await client.PostAsJsonAsync(RulesUrl, request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, body);
        Assert.Contains($"\"{field}\"", body);
    }

    private static async Task<LeadDto> CreateLeadAsync(HttpClient client, SaveLeadRequest request)
    {
        var response = await client.PostAsJsonAsync(LeadsUrl, request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LeadDto>())!;
    }

    private static async Task<List<string>> SubjectsAsync(HttpClient client, string query)
    {
        var page = await client.GetFromJsonAsync<CrmPage<LeadDto>>($"{LeadsUrl}?pageSize=50{query}");
        return [.. page!.Items.Select(l => l.Subject)];
    }

    private async Task<(Core.Platform.Tenant Tenant, ModulixUser Admin)> SetUpAsync()
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
}
