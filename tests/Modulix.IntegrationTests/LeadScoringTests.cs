using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Jobs;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Records;
using Modulix.Infrastructure.Identity;
using Modulix.Infrastructure.Persistence;
using Modulix.Modules.Crm;
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

    // Zamanlanmış işin anahtarı ve sistem kimliği (ADR-0050 dilim 1b); iş sınıfı modülün içindedir.
    private const string JobKey = "crm.lead-scoring";

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
        await AssertInvalidAsync(client, "value", Rule("Sıfır temas", "contacts", LeadScoreCatalog.AtLeast, "0", 10));
        await AssertInvalidAsync(client, "value", Rule("Yazıyla", "contacts", LeadScoreCatalog.AtLeast, "iki", 10));
        await AssertInvalidAsync(client, "operator", Rule("Ülke en az", "country", LeadScoreCatalog.AtLeast, "1", 10));
        await AssertInvalidAsync(client, "operator", Rule("Temas dolu", "contacts", LeadScoreCatalog.IsSet, null, 10));
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
    public async Task The_job_rescores_open_leads_after_a_rule_change_through_the_pipeline_and_a_second_run_writes_nothing()
    {
        var (tenant, admin) = await SetUpAsync();
        using var client = await LoginAsync(admin);
        var open = await CreateLeadAsync(client, new SaveLeadRequest("Açık aday", LastName: "A", Country: "TR"));
        var unassigned = await CreateLeadAsync(client, new SaveLeadRequest("Havuzdaki aday", LastName: "H", Country: "TR"));
        await DropOwnerAsync(tenant.Id, unassigned.Id);
        var closed = await CreateLeadAsync(client, new SaveLeadRequest("Elenen aday", LastName: "E", Country: "TR"));
        var disqualify = await client.PostAsJsonAsync($"{LeadsUrl}/{closed.Id}/disqualify", new DisqualifyLeadRequest(closed.Version, "NoBudget"));
        Assert.True(disqualify.IsSuccessStatusCode, await disqualify.Content.ReadAsStringAsync());
        Assert.Equal(0, open.Score);

        // Kural sonradan eklenir: mevcut adaylar iş çalışana kadar eski puanında kalır.
        var rule = await CreateRuleAsync(client, "Türkiye", "country", LeadScoreCatalog.EqualsTo, "TR", 40);
        Assert.Equal(0, (await GetLeadAsync(client, open.Id)).Score);

        var first = await RunJobAsync(tenant.Id);
        Assert.Equal(new LeadScoreJobRun(2, 2, 0), first);
        Assert.Equal(40, (await GetLeadAsync(client, open.Id)).Score);
        Assert.Equal(40, (await GetLeadAsync(client, unassigned.Id)).Score);
        Assert.Equal(0, (await GetLeadAsync(client, closed.Id)).Score);

        // Puan değişikliği denetim kaydına sistem adına (işin kimliğiyle) girer.
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
            var audit = await db.AuditLog.AsNoTracking().Where(a => a.RecordId == open.Id && a.Action == AuditAction.Updated).ToListAsync();
            var entry = Assert.Single(audit);
            Assert.Equal(JobKey, entry.Subscription);
            Assert.Contains("\"score\"", entry.Changes);
        }

        Assert.Equal(new LeadScoreJobRun(0, 0, 0), await RunJobAsync(tenant.Id));

        // Kural silinince puan yeniden sıfıra iner.
        var current = (await client.GetFromJsonAsync<RecordResponse>($"{RulesUrl}/{rule.Id}"))!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{RulesUrl}/{rule.Id}?version={current.Version}")).StatusCode);
        Assert.Equal(new LeadScoreJobRun(2, 2, 0), await RunJobAsync(tenant.Id));
        Assert.Equal(0, (await GetLeadAsync(client, open.Id)).Score);
    }

    [Fact]
    public async Task The_job_of_one_tenant_does_not_touch_another_tenants_leads()
    {
        var (tenantA, adminA) = await SetUpAsync();
        var (tenantB, adminB) = await SetUpAsync();
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var leadB = await CreateLeadAsync(clientB, new SaveLeadRequest("B adayı", LastName: "B", Country: "TR"));
        await CreateRuleAsync(clientA, "Türkiye", "country", LeadScoreCatalog.EqualsTo, "TR", 60);
        await CreateRuleAsync(clientB, "Almanya", "country", LeadScoreCatalog.EqualsTo, "DE", 10);

        Assert.Equal(new LeadScoreJobRun(0, 0, 0), await RunJobAsync(tenantA.Id));
        Assert.Equal(0, (await GetLeadAsync(clientB, leadB.Id)).Score);
        Assert.Equal(new LeadScoreJobRun(0, 0, 0), await RunJobAsync(tenantB.Id));
    }

    [Fact]
    public async Task Repeat_inquiries_from_the_same_person_are_counted_within_the_tenant_only()
    {
        var (tenant, admin) = await SetUpAsync();
        var (_, otherAdmin) = await SetUpAsync();
        using var client = await LoginAsync(admin);
        using var other = await LoginAsync(otherAdmin);
        await CreateRuleAsync(client, "Tekrar başvuran", "repeatInquiries", LeadScoreCatalog.AtLeast, "1", 40);

        // Başka tenant'taki aynı e-posta tekrar sayılmaz.
        await CreateLeadAsync(other, new SaveLeadRequest("Öteki firma", LastName: "Ak", Email: "ali@ornek.test"));
        var first = await CreateLeadAsync(client, new SaveLeadRequest("İlk talep", LastName: "Ak", Email: "ali@ornek.test"));
        Assert.Equal(0, first.Score);

        // Aynı kişi yeniden başvurur (e-posta büyük harfle; normalleştirilmiş e-posta eşleşir): yeni aday kaydedilirken puanlanır, eski aday toplu işle güncellenir.
        var second = await CreateLeadAsync(client, new SaveLeadRequest("İkinci talep", LastName: "Ak", Email: "ALI@ornek.test"));
        Assert.Equal(40, second.Score);
        Assert.Equal(0, (await GetLeadAsync(client, first.Id)).Score);
        Assert.Equal(new LeadScoreJobRun(1, 1, 0), await RunJobAsync(tenant.Id));
        Assert.Equal(40, (await GetLeadAsync(client, first.Id)).Score);
        Assert.Equal(new LeadScoreJobRun(0, 0, 0), await RunJobAsync(tenant.Id));
    }

    [Fact]
    public void The_job_identity_can_only_read_and_update_leads()
    {
        var subscription = ((IModule)new CrmModule()).EventSubscriptions.Single(s => s.Key == JobKey);
        Assert.Equal([LeadsRead, LeadsUpdate, LeadsAssign], subscription.Permissions);
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

    // Sorumlusu boş aday (atama havuzu): elle açılan aday açana atanır, havuz durumunu doğrudan kurarız.
    private async Task DropOwnerAsync(Guid tenantId, Guid leadId)
    {
        using var scope = factory.OpenScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE crm.Leads SET OwnerId = NULL, AssignedAt = NULL, Version = Version + 1 WHERE Id = {0}", leadId);
    }

    private static async Task<LeadDto> GetLeadAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<LeadDto>($"{LeadsUrl}/{id}"))!;

    private async Task<LeadScoreJobRun?> RunJobAsync(Guid tenantId)
    {
        var run = await factory.Services.GetRequiredService<ModuleJobRunner>().RunTenantAsync(JobKey, tenantId, CancellationToken.None);
        Assert.Equal(ModuleJobRunStatus.Completed, run.Status);
        return run.Result?.Details as LeadScoreJobRun;
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
