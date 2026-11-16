using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Tenancy;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Records;
using Modulix.Infrastructure.Events;
using Modulix.Infrastructure.Persistence;
using Modulix.Modules.Crm;
using Modulix.Shared.Auth;
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
    public void The_subscriber_can_only_read_and_update_leads()
    {
        var subscription = ((IModule)new CrmModule()).EventSubscriptions.Single(s => s.Key == "crm.lead-behavior-score");
        Assert.Equal([LeadsRead, LeadsUpdate, LeadsAssign], subscription.Permissions);
        Assert.Equal([CrmModule.Entities.Activity], subscription.EntityKeys);
    }

    private static async Task CreateRuleAsync(HttpClient client, string name, string field, string count, int points)
    {
        var response = await client.PostAsJsonAsync(RulesUrl, new RecordWriteRequest(new JsonObject
        {
            ["name"] = name, ["field"] = field, ["operator"] = LeadScoreCatalog.AtLeast, ["value"] = count, ["points"] = points, ["isActive"] = true,
        }));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
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
