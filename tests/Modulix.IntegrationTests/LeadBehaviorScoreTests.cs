using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Jobs;
using Modulix.Api.Tenancy;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Records;
using Modulix.Infrastructure.Automation;
using Modulix.Infrastructure.Events;
using Modulix.Infrastructure.Persistence;
using Modulix.Modules.Crm;
using Modulix.Modules.Crm.Automation;
using Modulix.Modules.Crm.Leads;
using Modulix.Shared.Auth;
using Modulix.Shared.Automation;
using Modulix.Shared.Crm;
using Modulix.Shared.Records;
using static Modulix.Modules.Crm.CrmModule.Permissions;

namespace Modulix.IntegrationTests;

/// <summary>
/// Davranış puanı (ADR-0050, dilim 2; <c>crm.lead-behavior-score</c>): adaya bağlı tamamlanmış gerçek temaslar ve toplantılar "en az N" kurallarıyla
/// puanlanır; aktivite eklenince puan olayla hemen güncellenir, gizli aktivite sayılmaz, olay tekrar teslim edilse ikinci yazım olmaz. Olay işleyicisi
/// tüm veritabanındaki olayları işlediği için bu sınıf diğer olay testleriyle aynı koleksiyonda sırayla çalışır.
/// </summary>
[Collection(EventTestCollection.Name)]
public sealed class LeadBehaviorScoreTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string RulesUrl = "/api/records/" + LeadScoreCatalog.EntityKey;

    [Fact]
    public async Task Completed_contacts_and_meetings_raise_the_score_through_the_event_and_replays_write_nothing()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, CrmModule.ModuleKey, enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, admin);
        using var client = factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(admin.Email!, ModulixApiFactory.Password))).StatusCode);

        await CreateRuleAsync(client, "İki temas", "contacts", "2", 20);
        await CreateRuleAsync(client, "Toplantı yapıldı", "meetings", "1", 30);
        var lead = (await (await client.PostAsJsonAsync("/api/crm/leads", new SaveLeadRequest("Davranış", LastName: "Kaya"))).Content.ReadFromJsonAsync<LeadDto>())!;
        Assert.Equal(0, lead.Score);
        var done = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Bir temas eşiğe yetmez; gizli toplantı ve tamamlanmamış toplantı sayılmaz.
        await PostActivityAsync(client, new SaveActivityRequest("Görüşüldü", ActivityType.Call, done, done, LeadId: lead.Id, Outcome: ActivityOutcome.Reached));
        await PostActivityAsync(client, new SaveActivityRequest("Gizli toplantı", ActivityType.Meeting, done, done, LeadId: lead.Id, IsPrivate: true));
        await PostActivityAsync(client, new SaveActivityRequest("Planlı toplantı", ActivityType.Meeting, DateTimeOffset.UtcNow.AddDays(1), LeadId: lead.Id));
        await DrainAsync(tenant.Id);
        Assert.Equal(0, (await GetLeadAsync(client, lead.Id)).Score);

        // Yapılan toplantı hem ikinci temas hem toplantıdır: 20 + 30.
        await PostActivityAsync(client, new SaveActivityRequest("Sunum", ActivityType.Meeting, done, done, LeadId: lead.Id));
        await DrainAsync(tenant.Id);
        var scored = await GetLeadAsync(client, lead.Id);
        Assert.Equal(50, scored.Score);

        await ReplayAllDeliveriesAsync(tenant.Id);
        await DrainAsync(tenant.Id);
        Assert.Equal(scored.Version, (await GetLeadAsync(client, lead.Id)).Version);

        using var scope = factory.OpenScope(tenant.Id);
        var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
        var change = Assert.Single(await db.AuditLog.AsNoTracking()
            .Where(a => a.RecordId == lead.Id && a.Action == AuditAction.Updated && a.Subscription == "crm.lead-behavior-score").ToListAsync());
        Assert.Equal(50, JsonNode.Parse(change.Changes)!["score"]!["new"]!.GetValue<int>());
    }

    [Fact]
    public async Task Inactive_days_decay_the_score_through_the_job_and_a_new_contact_restores_it()
    {
        var (tenant, client) = await SetUpAsync();
        await CreateRuleAsync(client, "Türkiye", "country", null, 50, LeadScoreCatalog.EqualsTo, "TR");
        await CreateRuleAsync(client, "Bir aydır sessiz", "inactiveDays", "30", -20);
        var lead = await CreateLeadAsync(client, "Sessiz aday");
        Assert.Equal(50, lead.Score);

        // 40 gün önce gelmiş, hiç temas yok: toplu iş azaltır; ikinci tur bir şey yazmaz.
        await AgeLeadAsync(tenant.Id, lead.Id, days: 40);
        Assert.Equal(new LeadScoreJobRun(1, 1, 0), await RunJobAsync(tenant.Id));
        Assert.Equal(30, (await GetLeadAsync(client, lead.Id)).Score);
        Assert.Equal(new LeadScoreJobRun(0, 0, 0), await RunJobAsync(tenant.Id));

        // Bugün gerçekleşen temas hareketsizliği sıfırlar; puan olayla hemen geri gelir.
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        await PostActivityAsync(client, new SaveActivityRequest("Arandı", ActivityType.Call, now, now, LeadId: lead.Id, Outcome: ActivityOutcome.Reached));
        await DrainAsync(tenant.Id);
        Assert.Equal(50, (await GetLeadAsync(client, lead.Id)).Score);

        // Başka tenant'ın aynı kuralları yoksa onun eski adayına dokunulmaz.
        var (other, otherClient) = await SetUpAsync();
        var foreign = await CreateLeadAsync(otherClient, "Öteki");
        await AgeLeadAsync(other.Id, foreign.Id, days: 40);
        Assert.Equal(new LeadScoreJobRun(0, 0, 0), await RunJobAsync(other.Id));
        Assert.Equal(0, (await GetLeadAsync(otherClient, foreign.Id)).Score);
    }

    [Fact]
    public async Task The_score_threshold_template_opens_one_follow_up_when_the_score_crosses_the_threshold()
    {
        var (tenant, client) = await SetUpAsync();
        var use = await client.PostAsync($"/api/automation/templates/{CrmAutomationTemplates.LeadScoreThreshold}/use", null);
        Assert.Equal(HttpStatusCode.Created, use.StatusCode);
        var rule = (await use.Content.ReadFromJsonAsync<AutomationRuleDetailDto>())!;
        var publish = await client.PostAsJsonAsync($"/api/automation/rules/{rule.Rule.Id}/publish", new PublishAutomationRuleRequest(true, null));
        Assert.True(publish.StatusCode == HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());

        await CreateRuleAsync(client, "Türkiye", "country", null, 50, LeadScoreCatalog.EqualsTo, "TR");
        await CreateRuleAsync(client, "Toplantı yapıldı", "meetings", "1", 30);
        await CreateRuleAsync(client, "İki temas", "contacts", "2", 10);
        var lead = await CreateLeadAsync(client, "Eşik adayı");
        Assert.Equal(50, lead.Score);
        var done = DateTimeOffset.UtcNow.AddMinutes(-5);

        // 50 → 80: eşik aşıldı, sorumluya takip açılır.
        await PostActivityAsync(client, new SaveActivityRequest("Sunum", ActivityType.Meeting, done, done, LeadId: lead.Id));
        // Olay kuyruğu paralel testlerle ortaktır: puan olayı ve onun açtığı kural çalışması birkaç turda işlenebilir.
        for (var i = 0; i < 5 && (await FollowUpsAsync(tenant.Id)).Count == 0; i++)
        {
            await DeliverAndRunAsync(tenant.Id);
        }

        Assert.Equal(80, (await GetLeadAsync(client, lead.Id)).Score);
        var followUp = Assert.Single(await FollowUpsAsync(tenant.Id));
        Assert.Equal("Sıcak müşteri adayı: Eşik adayı", followUp.Subject);
        Assert.Equal(lead.OwnerId, followUp.OwnerId);

        // 80 → 90: eşiğin üstünde kalan değişiklik yeni takip açmaz.
        await PostActivityAsync(client, new SaveActivityRequest("İkinci sunum", ActivityType.Meeting, done, done, LeadId: lead.Id));
        await DeliverAndRunAsync(tenant.Id);
        Assert.Equal(90, (await GetLeadAsync(client, lead.Id)).Score);
        Assert.Single(await FollowUpsAsync(tenant.Id));
    }

    [Fact]
    public void The_subscriber_can_only_read_and_update_leads()
    {
        var subscription = ((IModule)new CrmModule()).EventSubscriptions.Single(s => s.Key == "crm.lead-behavior-score");
        Assert.Equal([LeadsRead, LeadsUpdate, LeadsAssign], subscription.Permissions);
        Assert.Equal([CrmModule.Entities.Activity], subscription.EntityKeys);
    }

    private static async Task CreateRuleAsync(
        HttpClient client, string name, string field, string? count, int points, string op = LeadScoreCatalog.AtLeast, string? value = null)
    {
        var response = await client.PostAsJsonAsync(RulesUrl, new RecordWriteRequest(new JsonObject
        {
            ["name"] = name, ["field"] = field, ["operator"] = op, ["value"] = value ?? count, ["points"] = points, ["isActive"] = true,
        }));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<(Modulix.Core.Platform.Tenant Tenant, HttpClient Client)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, CrmModule.ModuleKey, enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, admin);
        var client = factory.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(admin.Email!, ModulixApiFactory.Password))).StatusCode);
        return (tenant, client);
    }

    private static async Task<LeadDto> CreateLeadAsync(HttpClient client, string subject)
    {
        var response = await client.PostAsJsonAsync("/api/crm/leads", new SaveLeadRequest(subject, LastName: "Kaya", Country: "TR"));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LeadDto>())!;
    }

    /// <summary>Adayı geçmişte gelmiş gibi gösterir (hareketsiz gün sayısı oluşturulmadan sayılır).</summary>
    private async Task AgeLeadAsync(Guid tenantId, Guid leadId, int days)
    {
        using var scope = factory.OpenScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
        await db.Database.ExecuteSqlRawAsync("UPDATE crm.Leads SET CreatedAt = DATEADD(day, -{1}, CreatedAt) WHERE Id = {0}", leadId, days);
    }

    private async Task<LeadScoreJobRun?> RunJobAsync(Guid tenantId)
    {
        var run = await factory.Services.GetRequiredService<ModuleJobRunner>().RunTenantAsync("crm.lead-scoring", tenantId, CancellationToken.None);
        Assert.Equal(ModuleJobRunStatus.Completed, run.Status);
        return run.Result?.Details as LeadScoreJobRun;
    }

    /// <summary>Olayları dağıtır (olay tetikleyicili kurallar çalışma açar), sonra kural turu bekleyen çalışmaları yürütür.</summary>
    private async Task DeliverAndRunAsync(Guid tenantId)
    {
        await DrainAsync(tenantId);
        var (_, skipped) = await factory.Services.GetRequiredService<AutomationRunner>().RunTenantAsync(tenantId, CancellationToken.None);
        Assert.False(skipped);
        await DrainAsync(tenantId);
    }

    private async Task<List<Modulix.Modules.Crm.Domain.Activity>> FollowUpsAsync(Guid tenantId)
    {
        using var scope = factory.OpenScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().Set<Modulix.Modules.Crm.Domain.Activity>().AsNoTracking()
            .Where(a => a.ExternalSource == "automation").ToListAsync();
    }

    private static async Task PostActivityAsync(HttpClient client, SaveActivityRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/crm/activities", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<LeadDto> GetLeadAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<LeadDto>($"/api/crm/leads/{id}"))!;

    private async Task DrainAsync(Guid tenantId)
    {
        for (var i = 0; i < 20; i++)
        {
            using var scope = TenantScope(tenantId);
            var round = await scope.ServiceProvider.GetRequiredService<EventDispatcher>().RunOnceAsync(CancellationToken.None);
            if (round.Dispatched == 0 && round.Delivered == 0)
            {
                return;
            }
        }
    }

    /// <summary>Tüm teslimleri yeniden "bekliyor" yapar: en az bir kez teslim garantisinin tekrarını taklit eder.</summary>
    private async Task ReplayAllDeliveriesAsync(Guid tenantId)
    {
        using var scope = TenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM platform.EventDeliveries; UPDATE platform.OutboxMessages SET DispatchedAt = NULL");
    }

    private IServiceScope TenantScope(Guid tenantId)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpTenantContextAccessor>().Current = new TenantContext(tenantId, Guid.Empty, Guid.Empty);
        return scope;
    }
}
