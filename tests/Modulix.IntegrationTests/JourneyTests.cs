using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Authorization;
using Modulix.Infrastructure.Identity;
using Modulix.Modules.Crm;
using Modulix.Modules.Marketing;
using Modulix.Shared.Auth;
using Modulix.Shared.Marketing;
using Modulix.Shared.Records;
using static Modulix.Modules.Marketing.MarketingModule.Permissions;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0056 dilim 1a ve 1b: müşteri yolculuğu kaydı, yolculuk kayıtları ve durum geçişleri (yayın, duraklatma, sürdürme, arşiv). Dört test türü: mutlu yol (oluşturma, listeleme, güncelleme, silme),
/// hata/doğrulama (kod, hedef türü, giriş segmenti, çıkış koşulu, elle kayıt), tenant izolasyonu ve yetki.
/// </summary>
public class JourneyTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly string[] CrmPermissions =
    [
        CrmModule.Permissions.CompaniesRead, CrmModule.Permissions.CompaniesWrite,
        CrmModule.Permissions.ContactsRead, CrmModule.Permissions.ContactsWrite,
    ];

    private static readonly string[] AllMarketing = [SegmentsRead, SegmentsWrite, SegmentsPublish, RecipientsRead, JourneysRead, JourneysWrite, JourneysPublish];

    // ---- Arayüz sözleşmesi ----

    [Fact]
    public void Journey_permission_keys_match_the_shared_access_keys()
    {
        Assert.Equal(MarketingAccess.JourneysRead, JourneysRead);
        Assert.Equal(MarketingAccess.JourneysWrite, JourneysWrite);
        Assert.Equal(MarketingAccess.JourneysPublish, JourneysPublish);
    }

    // ---- Mutlu yol ----

    [Fact]
    public async Task A_journey_is_created_in_draft_listed_updated_and_a_draft_without_enrollments_is_deleted()
    {
        var (client, _) = await ManagerAsync();
        var segment = await ActiveSegmentAsync(client, "bursa-firmalari");

        var created = await CreateJourneyAsync(client, "hosgeldin", segment.Id);
        Assert.Equal("Draft", created.Status);
        Assert.Equal("Never", created.ReentryPolicy);
        Assert.Equal("Company", created.Target);
        Assert.Equal(segment.Id, created.SegmentId);

        var list = (await client.GetFromJsonAsync<PagedResult<JourneyDto>>("/api/marketing/journeys"))!.Items;
        Assert.Contains(list, j => j.Id == created.Id);
        Assert.Empty((await client.GetFromJsonAsync<PagedResult<JourneyEnrollmentDto>>($"/api/marketing/journey-enrollments?journeyId={created.Id}"))!.Items);

        // Genel istek durumu yazamaz (yok sayılır); ad ve yeniden giriş politikası güncellenir.
        var update = await client.PutAsJsonAsync($"/api/marketing/journeys/{created.Id}",
            new SaveJourneyRequest(created.Version, created.Code, "Yeni ad", created.Description, created.Target, created.SegmentId, "AfterExit", null, created.OwnerId));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<JourneyDto>())!;
        Assert.Equal("Yeni ad", updated.Name);
        Assert.Equal("AfterExit", updated.ReentryPolicy);
        Assert.Equal("Draft", updated.Status);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/marketing/journeys/{created.Id}?version={updated.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/marketing/journeys/{created.Id}")).StatusCode);
    }

    // Arayüzün çıkış koşulu düzenleyicisi (ADR-0056 yolculuk ekranları): koşul kaydın tamamıyla birlikte yazılır ve null ile kaldırılır.
    [Fact]
    public async Task The_exit_condition_is_saved_with_the_whole_record_removed_with_null_and_a_partial_save_is_refused()
    {
        var (client, _) = await ManagerAsync();
        var segment = await ActiveSegmentAsync(client, "cikis-kosulu");
        var created = await CreateJourneyAsync(client, "cikis-kosulu-yolculugu", segment.Id);

        var condition = new JsonObject { ["op"] = "and", ["items"] = new JsonArray(Leaf("crm.company.industry", "eq", "Gıda ve içecek")) }.ToJsonString();
        var saved = await client.PutAsJsonAsync($"/api/marketing/journeys/{created.Id}",
            new SaveJourneyRequest(created.Version, created.Code, created.Name, created.Description, created.Target, created.SegmentId, created.ReentryPolicy, condition, created.OwnerId));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var withCondition = (await saved.Content.ReadFromJsonAsync<JourneyDto>())!;
        Assert.Equal(condition, withCondition.ExitCondition);

        // Biçimi bozuk koşul alan hatasıyla reddedilir ve kayıtlı koşul değişmez.
        var broken = await client.PutAsJsonAsync($"/api/marketing/journeys/{created.Id}",
            new SaveJourneyRequest(withCondition.Version, withCondition.Code, withCondition.Name, withCondition.Description, withCondition.Target, withCondition.SegmentId, withCondition.ReentryPolicy, "{bozuk", withCondition.OwnerId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, broken.StatusCode);
        Assert.Equal(condition, (await client.GetFromJsonAsync<JourneyDto>($"/api/marketing/journeys/{created.Id}"))!.ExitCondition);

        // Eksik gönderilen kayıt (zorunlu alanlar null) reddedilir: PUT kaydın tamamını yazar.
        var partial = await client.PutAsJsonAsync($"/api/marketing/journeys/{created.Id}", new SaveJourneyRequest(withCondition.Version, Name: "Yeni ad", ReentryPolicy: "Never"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, partial.StatusCode);

        // Koşul null gönderilince kaldırılır.
        var removed = await client.PutAsJsonAsync($"/api/marketing/journeys/{created.Id}",
            new SaveJourneyRequest(withCondition.Version, withCondition.Code, withCondition.Name, withCondition.Description, withCondition.Target, withCondition.SegmentId, withCondition.ReentryPolicy, null, withCondition.OwnerId));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Null((await removed.Content.ReadFromJsonAsync<JourneyDto>())!.ExitCondition);
    }

    // ---- Hata ve doğrulama ----

    [Fact]
    public async Task Invalid_journeys_are_rejected_with_field_errors_and_manual_enrollments_are_refused()
    {
        var (client, _) = await ManagerAsync();
        var active = await ActiveSegmentAsync(client, "etkin-segment");
        var draftSegment = await CreateSegmentAsync(client, "taslak-segment", Leaf("crm.company.city", "eq", "Izmir"), "Company");
        var personSegment = await ActiveSegmentAsync(client, "kisi-segmenti", "Person", Leaf("crm.contact.title", "eq", "Mudur"));

        // Kod biçimi, giriş segmenti (taslak segment, hedef türü uyuşmazlığı) ve çıkış koşulu alan hatası verir.
        Assert.Contains("code", await ErrorsAsync(client, new SaveJourneyRequest(Code: "Gecersiz Kod!", Name: "X", Target: "Company", SegmentId: active.Id)));
        Assert.Contains("segmentId", await ErrorsAsync(client, new SaveJourneyRequest(Code: "j-taslak", Name: "X", Target: "Company", SegmentId: draftSegment.Id)));
        Assert.Contains("segmentId", await ErrorsAsync(client, new SaveJourneyRequest(Code: "j-uyumsuz", Name: "X", Target: "Company", SegmentId: personSegment.Id)));
        Assert.Contains("segmentId", await ErrorsAsync(client, new SaveJourneyRequest(Code: "j-yok", Name: "X", Target: "Company", SegmentId: Guid.NewGuid())));
        Assert.Contains("exitCondition", await ErrorsAsync(client, new SaveJourneyRequest(Code: "j-cikis", Name: "X", Target: "Company", SegmentId: active.Id, ExitCondition: "[1,2]")));
        Assert.Contains("name", await ErrorsAsync(client, new SaveJourneyRequest(Code: "j-adsiz", Target: "Company", SegmentId: active.Id)));

        // Aynı kod ikinci kez 409; kod ve hedef türü oluşturulduktan sonra değişmez.
        var journey = await CreateJourneyAsync(client, "tekrar", active.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/marketing/journeys",
            new SaveJourneyRequest(Code: "tekrar", Name: "Y", Target: "Company", SegmentId: active.Id))).StatusCode);
        Assert.Contains("code", await UpdateErrorsAsync(client, journey, journey with { Code = "baska-kod" }));
        Assert.Contains("target", await UpdateErrorsAsync(client, journey, journey with { Target = "Person" }));

        // Yolculuk kaydı elle oluşturulamaz, değiştirilemez ya da silinemez (yalnızca yolculuk girişiyle oluşur).
        var manual = await client.PostAsJsonAsync("/api/marketing/journey-enrollments", new SaveJourneyEnrollmentRequest());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, manual.StatusCode);
    }

    // ---- Tenant izolasyonu ----

    [Fact]
    public async Task Another_tenant_sees_none_of_the_journeys_and_cannot_use_a_foreign_segment()
    {
        var (clientA, _) = await ManagerAsync();
        var (clientB, _) = await ManagerAsync();
        var segmentA = await ActiveSegmentAsync(clientA, "ortak-segment");
        var journeyA = await CreateJourneyAsync(clientA, "ortak-yolculuk", segmentA.Id);

        Assert.Empty((await clientB.GetFromJsonAsync<PagedResult<JourneyDto>>("/api/marketing/journeys"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/marketing/journeys/{journeyA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.DeleteAsync($"/api/marketing/journeys/{journeyA.Id}?version={journeyA.Version}")).StatusCode);
        Assert.Empty((await clientB.GetFromJsonAsync<PagedResult<JourneyEnrollmentDto>>($"/api/marketing/journey-enrollments?journeyId={journeyA.Id}"))!.Items);

        // B, A'nın segmentini giriş segmenti yapamaz; aynı kod B'de serbesttir (benzersizlik tenant ile birlikte).
        Assert.Contains("segmentId", await ErrorsAsync(clientB, new SaveJourneyRequest(Code: "b-yolculuk", Name: "B", Target: "Company", SegmentId: segmentA.Id)));
        var segmentB = await ActiveSegmentAsync(clientB, "b-segment");
        var journeyB = await CreateJourneyAsync(clientB, "ortak-yolculuk", segmentB.Id);
        Assert.NotEqual(journeyA.Id, journeyB.Id);
    }

    // ---- Yetki ----

    [Fact]
    public async Task Journeys_need_their_own_read_and_write_permissions_and_record_scope_applies()
    {
        var tenant = await TenantAsync();
        var manager = await UserClientAsync(tenant, [.. AllMarketing, .. CrmPermissions]);
        var segment = await ActiveSegmentAsync(manager, "yetki-segment");
        var journey = await CreateJourneyAsync(manager, "yetki-yolculuk", segment.Id);

        var viewer = await UserClientAsync(tenant, [JourneysRead]);
        Assert.Contains((await viewer.GetFromJsonAsync<PagedResult<JourneyDto>>("/api/marketing/journeys"))!.Items, j => j.Id == journey.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/marketing/journeys",
            new SaveJourneyRequest(Code: "izleyici", Name: "X", Target: "Company", SegmentId: segment.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/marketing/journeys/{journey.Id}?version={journey.Version}")).StatusCode);

        var nobody = await UserClientAsync(tenant, [SegmentsRead]);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/marketing/journeys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/marketing/journey-enrollments")).StatusCode);

        // Kapsamı "kendi" olan kullanıcı başkasının yolculuğunu görmez.
        var own = await UserClientAsync(tenant, [.. AllMarketing, .. CrmPermissions], RecordScope.Own);
        Assert.DoesNotContain((await own.GetFromJsonAsync<PagedResult<JourneyDto>>("/api/marketing/journeys"))!.Items, j => j.Id == journey.Id);
    }

    // ---- Dilim 1b: durum geçişleri ----

    [Fact]
    public async Task A_draft_journey_is_published_paused_resumed_and_archived()
    {
        var (client, _) = await ManagerAsync();
        var segment = await ActiveSegmentAsync(client, "gecis-segmenti");
        var journey = await CreateJourneyAsync(client, "gecis", segment.Id);

        var published = await TransitionAsync(client, journey, "publish");
        Assert.Equal("Active", published.Status);
        Assert.True(published.Version > journey.Version);

        var paused = await TransitionAsync(client, published, "pause");
        Assert.Equal("Paused", paused.Status);

        var resumed = await TransitionAsync(client, paused, "resume");
        Assert.Equal("Active", resumed.Status);

        var archived = await TransitionAsync(client, resumed, "archive");
        Assert.Equal("Archived", archived.Status);

        // Arşivlenmiş yolculuk silinemez (taslak değil) ve yeniden etkinleştirilemez.
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/marketing/journeys/{archived.Id}?version={archived.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostActionAsync(client, archived, "resume")).StatusCode);
    }

    [Fact]
    public async Task Invalid_transitions_and_an_inactive_entry_segment_are_refused_with_conflict()
    {
        var (client, _) = await ManagerAsync();
        var segment = await ActiveSegmentAsync(client, "gecis-hata-segmenti");
        var journey = await CreateJourneyAsync(client, "gecis-hata", segment.Id);

        // Taslak duraklatılamaz ve sürdürülemez; etkin yolculuk yeniden yayımlanamaz.
        Assert.Equal(HttpStatusCode.Conflict, (await PostActionAsync(client, journey, "pause")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostActionAsync(client, journey, "resume")).StatusCode);
        var published = await TransitionAsync(client, journey, "publish");
        Assert.Equal(HttpStatusCode.Conflict, (await PostActionAsync(client, published, "publish")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostActionAsync(client, published, "resume")).StatusCode);

        // Eski sürümle geçiş eşzamanlılık çatışmasıdır (durum değişmez).
        var stale = await client.PostAsJsonAsync($"/api/marketing/journeys/{published.Id}/pause", new JourneyActionRequest(published.Version - 1));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Active", (await client.GetFromJsonAsync<JourneyDto>($"/api/marketing/journeys/{published.Id}"))!.Status);

        // Giriş segmenti etkin değilse (arşivlendi) taslak yolculuk yayımlanamaz, duraklatılmış yolculuk sürdürülemez.
        var otherSegment = await ActiveSegmentAsync(client, "gecis-arsiv-segmenti");
        var draft = await CreateJourneyAsync(client, "gecis-arsiv", otherSegment.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/marketing/segments/{otherSegment.Id}/archive", new SegmentActionRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostActionAsync(client, draft, "publish")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/marketing/journeys/{Guid.NewGuid()}/publish", new JourneyActionRequest())).StatusCode);
    }

    [Fact]
    public async Task Another_tenant_cannot_change_the_status_of_a_foreign_journey()
    {
        var (clientA, _) = await ManagerAsync();
        var (clientB, _) = await ManagerAsync();
        var journeyA = await CreateJourneyAsync(clientA, "yabanci-gecis", (await ActiveSegmentAsync(clientA, "yabanci-segment")).Id);

        foreach (var action in new[] { "publish", "pause", "resume", "archive" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await PostActionAsync(clientB, journeyA, action)).StatusCode);
        }

        Assert.Equal("Draft", (await clientA.GetFromJsonAsync<JourneyDto>($"/api/marketing/journeys/{journeyA.Id}"))!.Status);
    }

    [Fact]
    public async Task Status_transitions_need_the_publish_permission_and_record_scope_applies()
    {
        var tenant = await TenantAsync();
        var manager = await UserClientAsync(tenant, [.. AllMarketing, .. CrmPermissions]);
        var journey = await CreateJourneyAsync(manager, "yetki-gecis", (await ActiveSegmentAsync(manager, "yetki-gecis-segment")).Id);

        // Yazma izni yayın izni değildir; izleyici de geçiş yapamaz.
        var writer = await UserClientAsync(tenant, [SegmentsRead, JourneysRead, JourneysWrite]);
        var viewer = await UserClientAsync(tenant, [JourneysRead]);
        foreach (var client in new[] { writer, viewer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await PostActionAsync(client, journey, "publish")).StatusCode);
        }

        Assert.Equal("Draft", (await manager.GetFromJsonAsync<JourneyDto>($"/api/marketing/journeys/{journey.Id}"))!.Status);

        // Kapsamı "kendi" olan yayıncı başkasının yolculuğunu göremez, geçiş yapamaz.
        var own = await UserClientAsync(tenant, [.. AllMarketing, .. CrmPermissions], RecordScope.Own);
        Assert.Equal(HttpStatusCode.NotFound, (await PostActionAsync(own, journey, "publish")).StatusCode);
    }

    private static Task<HttpResponseMessage> PostActionAsync(HttpClient client, JourneyDto journey, string action) =>
        client.PostAsJsonAsync($"/api/marketing/journeys/{journey.Id}/{action}", new JourneyActionRequest(journey.Version));

    private static async Task<JourneyDto> TransitionAsync(HttpClient client, JourneyDto journey, string action)
    {
        var response = await PostActionAsync(client, journey, action);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JourneyDto>())!;
    }

    // ---- Yardımcılar ----

    private static JsonObject Leaf(string field, string op, params string[] values) => new()
    {
        ["field"] = field,
        ["operator"] = op,
        ["values"] = new JsonArray([.. values.Select(v => (JsonNode?)JsonValue.Create(v))]),
    };

    private static async Task<SegmentDto> CreateSegmentAsync(HttpClient client, string code, JsonObject draft, string target)
    {
        var response = await client.PostAsJsonAsync("/api/marketing/segments",
            new SaveSegmentRequest(Code: code, Name: $"Segment {code}", Target: target, Kind: "Dynamic", Purpose: "Test", Draft: draft.ToJsonString()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SegmentDto>())!;
    }

    private static async Task<SegmentDto> ActiveSegmentAsync(HttpClient client, string code, string target = "Company", JsonObject? draft = null)
    {
        var segment = await CreateSegmentAsync(client, code, draft ?? Leaf("crm.company.city", "eq", "Bursa"), target);
        var publish = await client.PostAsJsonAsync($"/api/marketing/segments/{segment.Id}/publish", new SegmentActionRequest());
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        return (await publish.Content.ReadFromJsonAsync<SegmentDto>())!;
    }

    private static async Task<JourneyDto> CreateJourneyAsync(HttpClient client, string code, Guid segmentId)
    {
        var response = await client.PostAsJsonAsync("/api/marketing/journeys",
            new SaveJourneyRequest(Code: code, Name: $"Yolculuk {code}", Target: "Company", SegmentId: segmentId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JourneyDto>())!;
    }

    private static async Task<IEnumerable<string>> ErrorsAsync(HttpClient client, SaveJourneyRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/marketing/journeys", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsObject().Select(e => e.Key).ToList();
    }

    private static async Task<IEnumerable<string>> UpdateErrorsAsync(HttpClient client, JourneyDto current, JourneyDto changed)
    {
        var response = await client.PutAsJsonAsync($"/api/marketing/journeys/{current.Id}",
            new SaveJourneyRequest(current.Version, changed.Code, changed.Name, changed.Description, changed.Target, changed.SegmentId, changed.ReentryPolicy, changed.ExitCondition, changed.OwnerId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsObject().Select(e => e.Key).ToList();
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

    private async Task<HttpClient> UserClientAsync(Modulix.Core.Platform.Tenant tenant, string[] permissions, RecordScope scope = RecordScope.All)
    {
        var user = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, permissions, scope, user);
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
