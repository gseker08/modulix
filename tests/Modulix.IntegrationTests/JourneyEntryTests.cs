using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Jobs;
using Modulix.Core.Contracts.Authorization;
using Modulix.Infrastructure.Identity;
using Modulix.Infrastructure.Persistence;
using Modulix.Modules.Crm;
using Modulix.Modules.Marketing;
using Modulix.Modules.Marketing.Journeys;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Marketing;
using Modulix.Shared.Records;
using System.Text.Json.Nodes;
using static Modulix.Modules.Marketing.MarketingModule.Permissions;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0056 dilim 1b-2: yolculuk giriş işi. Dört test türü: mutlu yol (segmentin yeni üyeleri kayıt olur, olay yayınlanır, iş tekrarında yeni kayıt açmaz,
/// sonradan segmente giren de girer), hata/doğrulama (etkin olmayan yolculuk girmez; etkinleştirenin izni olmayan segment alanı okunamaz, girişi
/// uyarıyla atlanır), tenant izolasyonu (yalnızca kendi tenant'ının hedefleri) ve yetki (CRM'i okuyamayan etkinleştiren segmenti çalıştıramaz; iş,
/// yazma izni verilmiş küme olmadan çalışmaz).
/// </summary>
public class JourneyEntryTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly string[] CrmPermissions =
    [
        CrmModule.Permissions.CompaniesRead, CrmModule.Permissions.CompaniesWrite,
        CrmModule.Permissions.ContactsRead, CrmModule.Permissions.ContactsWrite,
    ];

    private static readonly string[] AllMarketing = [SegmentsRead, SegmentsWrite, SegmentsPublish, RecipientsRead, JourneysRead, JourneysWrite, JourneysPublish];

    // ---- Mutlu yol ----

    [Fact]
    public async Task Segment_members_enter_the_journey_once_with_an_event_and_later_members_enter_on_the_next_run()
    {
        var (client, tenantId) = await ManagerAsync();
        var bursa1 = await CompanyAsync(client, "Bursa Bir", "Bursa");
        var bursa2 = await CompanyAsync(client, "Bursa Iki", "Bursa");
        var izmir = await CompanyAsync(client, "Izmir Bir", "Izmir");
        var journey = await PublishedJourneyAsync(client, "hosgeldin", "bursa");

        var first = await RunAsync(tenantId);
        Assert.Equal(2, first.Sum(o => o.Entered));
        Assert.Null(first.Single().Error);

        var enrollments = await EnrollmentsAsync(client, journey.Id);
        Assert.Equal(2, enrollments.Count);
        Assert.Equivalent(new[] { bursa1.Id, bursa2.Id }, enrollments.Select(e => e.TargetId));
        Assert.DoesNotContain(enrollments, e => e.TargetId == izmir.Id);
        Assert.All(enrollments, e => Assert.Equal("Active", e.Status));

        // Her kayıt için iş olayı aynı işlemde yazılır.
        using (var scope = factory.OpenScope(tenantId))
        {
            var events = await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().OutboxMessages.AsNoTracking()
                .Where(o => o.EventType == MarketingModule.Events.JourneyEntered).ToListAsync();
            Assert.Equal(2, events.Count);
            Assert.Equivalent(enrollments.Select(e => e.Id), events.Select(e => e.RecordId));
        }

        // İş tekrar çalışınca yeni kayıt açmaz (aynı hedef bir kez girer).
        Assert.Equal(0, (await RunAsync(tenantId)).Sum(o => o.Entered));
        Assert.Equal(2, (await EnrollmentsAsync(client, journey.Id)).Count);

        // Segmente sonradan giren hedef bir sonraki turda girer.
        var late = await CompanyAsync(client, "Bursa Uc", "Bursa");
        Assert.Equal(1, (await RunAsync(tenantId)).Sum(o => o.Entered));
        Assert.Contains(await EnrollmentsAsync(client, journey.Id), e => e.TargetId == late.Id);
    }

    // ---- Hata ve doğrulama ----

    [Fact]
    public async Task Draft_paused_and_archived_journeys_do_not_enter()
    {
        var (client, tenantId) = await ManagerAsync();
        await CompanyAsync(client, "Firma A", "Bursa");
        await CompanyAsync(client, "Firma B", "Bursa");
        await CompanyAsync(client, "Firma C", "Bursa");
        var segment = await ActiveSegmentAsync(client, "limit-segment", "Bursa");
        var draft = await CreateJourneyAsync(client, "taslak-yolculuk", segment.Id);

        Assert.Empty(await RunAsync(tenantId));
        Assert.Empty(await EnrollmentsAsync(client, draft.Id));

        var active = await Transition(client, draft, "publish");
        Assert.Equal(3, (await RunAsync(tenantId)).Sum(o => o.Entered));

        var paused = await Transition(client, active, "pause");
        await CompanyAsync(client, "Firma E", "Bursa");
        Assert.Empty(await RunAsync(tenantId));
        Assert.Equal(3, (await EnrollmentsAsync(client, paused.Id)).Count);
        var resumed = await Transition(client, paused, "resume");
        Assert.Equal(1, (await RunAsync(tenantId)).Sum(o => o.Entered));
        Assert.Equal(4, (await EnrollmentsAsync(client, resumed.Id)).Count);

        var archived = await Transition(client, resumed, "archive");
        await CompanyAsync(client, "Firma D", "Bursa");
        Assert.Empty(await RunAsync(tenantId));
        Assert.Equal(4, (await EnrollmentsAsync(client, archived.Id)).Count);
    }

    [Fact]
    public async Task An_archived_entry_segment_stops_new_entries_without_removing_existing_ones()
    {
        var (client, tenantId) = await ManagerAsync();
        await CompanyAsync(client, "Segment Firma", "Bursa");
        var segment = await ActiveSegmentAsync(client, "arsiv-segment", "Bursa");
        var journey = await Transition(client, await CreateJourneyAsync(client, "arsiv-yolculuk", segment.Id), "publish");
        Assert.Equal(1, (await RunAsync(tenantId)).Sum(o => o.Entered));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/marketing/segments/{segment.Id}/archive", new SegmentActionRequest())).StatusCode);
        await CompanyAsync(client, "Segment Firma 2", "Bursa");

        // Segment etkin değil: giriş uyarıyla atlanır, hata atılmaz, mevcut kayıtlar korunur.
        var outcome = Assert.Single(await RunAsync(tenantId));
        Assert.Equal(0, outcome.Entered);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Error));
        Assert.Single(await EnrollmentsAsync(client, journey.Id));
    }

    // ---- Tenant izolasyonu ----

    [Fact]
    public async Task A_tenant_run_enrolls_only_its_own_targets_and_never_touches_another_tenants_journeys()
    {
        var (clientA, tenantA) = await ManagerAsync();
        var (clientB, tenantB) = await ManagerAsync();
        var companyA = await CompanyAsync(clientA, "A Firma", "Bursa");
        await CompanyAsync(clientB, "B Firma", "Bursa");
        var journeyA = await PublishedJourneyAsync(clientA, "ortak", "bursa-a");
        var journeyB = await PublishedJourneyAsync(clientB, "ortak", "bursa-b");

        Assert.Equal(1, (await RunAsync(tenantA)).Sum(o => o.Entered));
        Assert.Equal([companyA.Id], (await EnrollmentsAsync(clientA, journeyA.Id)).Select(e => e.TargetId));
        Assert.Empty(await EnrollmentsAsync(clientB, journeyB.Id));
        Assert.Empty(await EnrollmentsAsync(clientB, journeyA.Id));

        using var scopeB = factory.OpenScope(tenantB);
        var db = scopeB.ServiceProvider.GetRequiredService<ModulixDbContext>();
        Assert.Equal(0, await db.OutboxMessages.AsNoTracking().CountAsync(o => o.EventType == MarketingModule.Events.JourneyEntered));
    }

    // ---- Yetki ----

    [Fact]
    public async Task The_activating_users_read_permissions_bound_the_entry_and_a_granted_write_permission_is_refused()
    {
        var tenant = await TenantAsync();
        var manager = await UserClientAsync(tenant, [.. AllMarketing, .. CrmPermissions]);
        await CompanyAsync(manager, "Yetki Firma", "Bursa");
        var segment = await ActiveSegmentAsync(manager, "yetki-segment", "Bursa");
        var journey = await CreateJourneyAsync(manager, "yetki-yolculuk", segment.Id);

        // Yolculuğu CRM okuma izni olmayan bir kullanıcı etkinleştirir: giriş işi segmentin CRM alanını okuyamaz, kimse kayıt olmaz.
        var marketingOnly = await UserClientAsync(tenant, AllMarketing);
        Assert.Equal(HttpStatusCode.OK, (await marketingOnly.PostAsJsonAsync($"/api/marketing/journeys/{journey.Id}/publish", new JourneyActionRequest())).StatusCode);
        var outcome = Assert.Single(await RunAsync(tenant.Id));
        Assert.Equal(0, outcome.Entered);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Error));
        Assert.Empty(await EnrollmentsAsync(manager, journey.Id));

        // Yetkili kullanıcı duraklatıp sürdürünce izin kümesi onunkiyle yenilenir ve hedef girer.
        var paused = await Transition(manager, (await manager.GetFromJsonAsync<JourneyDto>($"/api/marketing/journeys/{journey.Id}"))!, "pause");
        await Transition(manager, paused, "resume");
        Assert.Equal(1, (await RunAsync(tenant.Id)).Sum(o => o.Entered));

        // Verilmiş izin kuralı: başka modülün yazma izni verilemez (yalnızca okuma).
        Assert.Throws<ArgumentException>(() => Modulix.Core.Events.GrantedPermissionRules.Validate([CrmModule.Permissions.CompaniesWrite], ["marketing.segments.read"]));
    }

    // ---- Yardımcılar ----

    private async Task<IReadOnlyList<JourneyEntryOutcome>> RunAsync(Guid tenantId)
    {
        var run = await factory.Services.GetRequiredService<ModuleJobRunner>().RunTenantAsync("marketing.journey-entry", tenantId, CancellationToken.None);
        Assert.Equal(ModuleJobRunStatus.Completed, run.Status);
        return run.Result?.Details as IReadOnlyList<JourneyEntryOutcome> ?? [];
    }

    private static JsonObject Leaf(string city) => new()
    {
        ["field"] = "crm.company.city",
        ["operator"] = "eq",
        ["values"] = new JsonArray(JsonValue.Create(city)),
    };

    private static async Task<SegmentDto> ActiveSegmentAsync(HttpClient client, string code, string city)
    {
        var created = await client.PostAsJsonAsync("/api/marketing/segments",
            new SaveSegmentRequest(Code: code, Name: $"Segment {code}", Target: "Company", Kind: "Dynamic", Purpose: "Test", Draft: Leaf(city).ToJsonString()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var segment = (await created.Content.ReadFromJsonAsync<SegmentDto>())!;
        var publish = await client.PostAsJsonAsync($"/api/marketing/segments/{segment.Id}/publish", new SegmentActionRequest());
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        return (await publish.Content.ReadFromJsonAsync<SegmentDto>())!;
    }

    private static async Task<JourneyDto> CreateJourneyAsync(HttpClient client, string code, Guid segmentId)
    {
        var response = await client.PostAsJsonAsync("/api/marketing/journeys", new SaveJourneyRequest(Code: code, Name: $"Yolculuk {code}", Target: "Company", SegmentId: segmentId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JourneyDto>())!;
    }

    private static async Task<JourneyDto> PublishedJourneyAsync(HttpClient client, string code, string segmentCode)
    {
        var segment = await ActiveSegmentAsync(client, segmentCode, "Bursa");
        return await Transition(client, await CreateJourneyAsync(client, code, segment.Id), "publish");
    }

    private static async Task<JourneyDto> Transition(HttpClient client, JourneyDto journey, string action)
    {
        var response = await client.PostAsJsonAsync($"/api/marketing/journeys/{journey.Id}/{action}", new JourneyActionRequest(journey.Version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JourneyDto>())!;
    }

    private static async Task<List<JourneyEnrollmentDto>> EnrollmentsAsync(HttpClient client, Guid journeyId) =>
        [.. (await client.GetFromJsonAsync<PagedResult<JourneyEnrollmentDto>>($"/api/marketing/journey-enrollments?journeyId={journeyId}&pageSize=100"))!.Items];

    private static async Task<CompanyDto> CompanyAsync(HttpClient client, string name, string city)
    {
        var response = await client.PostAsJsonAsync("/api/crm/companies", new SaveCompanyRequest(name, City: city));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
    }

    private async Task<Modulix.Core.Platform.Tenant> TenantAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, CrmModule.ModuleKey, enabled: true);
        await factory.SetModuleAsync(tenant.Id, MarketingModule.ModuleKey, enabled: true);
        return tenant;
    }

    private async Task<(HttpClient Client, Guid TenantId)> ManagerAsync()
    {
        var tenant = await TenantAsync();
        return (await UserClientAsync(tenant, [.. AllMarketing, .. CrmPermissions]), tenant.Id);
    }

    private async Task<HttpClient> UserClientAsync(Modulix.Core.Platform.Tenant tenant, string[] permissions)
    {
        var user = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, permissions, RecordScope.All, user);
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
