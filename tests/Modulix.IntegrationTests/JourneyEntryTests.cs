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
/// ADR-0056 dilim 1b-2, 2c-1, 2c-2 ve 2c-3: yolculuk giriş işi, elle çıkarma, yeniden giriş ve çıkış koşulu. Dört test türü: mutlu yol (segmentin yeni üyeleri kayıt olur, olay yayınlanır, iş tekrarında yeni kayıt açmaz,
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
        Assert.Equivalent(new[] { bursa1.PartyId!.Value, bursa2.PartyId!.Value }, enrollments.Select(e => e.TargetId));
        Assert.DoesNotContain(enrollments, e => e.TargetId == izmir.PartyId!.Value);
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
        Assert.Contains(await EnrollmentsAsync(client, journey.Id), e => e.TargetId == late.PartyId!.Value);
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
        Assert.Equal([companyA.PartyId!.Value], (await EnrollmentsAsync(clientA, journeyA.Id)).Select(e => e.TargetId));
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

    // ---- Dilim 2c-1: elle çıkarma ----

    [Fact]
    public async Task A_manual_exit_ends_an_active_enrollment_with_a_reason_and_it_does_not_enter_again()
    {
        var (client, tenantId) = await ManagerAsync();
        var company = await CompanyAsync(client, "Cikis Firma", "Bursa");
        var journey = await PublishedJourneyAsync(client, "cikis", "bursa-cikis");
        Assert.Equal(1, (await RunAsync(tenantId)).Sum(o => o.Entered));
        var enrollment = Assert.Single(await EnrollmentsAsync(client, journey.Id));

        // Gerekçe zorunlu ve sınırlıdır (alan hatası); kayıt etkin kalır.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ExitAsync(client, enrollment, null)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ExitAsync(client, enrollment, new string('x', 201))).StatusCode);
        Assert.Equal("Active", Assert.Single(await EnrollmentsAsync(client, journey.Id)).Status);

        var exit = await ExitAsync(client, enrollment, "Müşteri aradı, yolculuk istemiyor");
        Assert.Equal(HttpStatusCode.OK, exit.StatusCode);
        var exited = (await exit.Content.ReadFromJsonAsync<JourneyEnrollmentDto>())!;
        Assert.Equal("Exited", exited.Status);
        Assert.NotNull(exited.ExitedAt);
        Assert.Equal("Müşteri aradı, yolculuk istemiyor", exited.ExitReason);

        // Sona eren kayıt yeniden etkinleşmez ve ikinci çıkarma çakışmadır; hedef (yeniden giriş yok) aynı yolculuğa tekrar girmez.
        Assert.Equal(HttpStatusCode.Conflict, (await ExitAsync(client, exited, "Tekrar")).StatusCode);
        Assert.Equal(0, (await RunAsync(tenantId)).Sum(o => o.Entered));
        Assert.Single(await EnrollmentsAsync(client, journey.Id));
        Assert.Equal(company.PartyId, exited.TargetId);
    }

    [Fact]
    public async Task Another_tenant_cannot_exit_a_foreign_enrollment()
    {
        var (clientA, tenantA) = await ManagerAsync();
        var (clientB, _) = await ManagerAsync();
        await CompanyAsync(clientA, "A Cikis", "Bursa");
        var journey = await PublishedJourneyAsync(clientA, "ortak-cikis", "bursa-ortak");
        await RunAsync(tenantA);
        var enrollment = Assert.Single(await EnrollmentsAsync(clientA, journey.Id));

        Assert.Equal(HttpStatusCode.NotFound, (await ExitAsync(clientB, enrollment, "Yabancı")).StatusCode);
        Assert.Equal("Active", Assert.Single(await EnrollmentsAsync(clientA, journey.Id)).Status);
    }

    [Fact]
    public async Task Exiting_an_enrollment_needs_the_journey_write_permission_and_record_scope_applies()
    {
        var tenant = await TenantAsync();
        var manager = await UserClientAsync(tenant, [.. AllMarketing, .. CrmPermissions]);
        await CompanyAsync(manager, "Yetki Cikis", "Bursa");
        var journey = await PublishedJourneyAsync(manager, "yetki-cikis", "bursa-yetki");
        await RunAsync(tenant.Id);
        var enrollment = Assert.Single(await EnrollmentsAsync(manager, journey.Id));

        var viewer = await UserClientAsync(tenant, [JourneysRead]);
        Assert.Equal(HttpStatusCode.Forbidden, (await ExitAsync(viewer, enrollment, "İzleyici")).StatusCode);

        // Kapsamı "kendi" olan kullanıcı başkasının yolculuk kaydını göremez, çıkaramaz.
        var own = await UserClientAsync(tenant, [.. AllMarketing, .. CrmPermissions], RecordScope.Own);
        Assert.Equal(HttpStatusCode.NotFound, (await ExitAsync(own, enrollment, "Başkası")).StatusCode);
        Assert.Equal("Active", Assert.Single(await EnrollmentsAsync(manager, journey.Id)).Status);
    }

    private static Task<HttpResponseMessage> ExitAsync(HttpClient client, JourneyEnrollmentDto enrollment, string? reason) =>
        client.PostAsJsonAsync($"/api/marketing/journey-enrollments/{enrollment.Id}/exit", new ExitJourneyEnrollmentRequest(enrollment.Version, reason));

    // ---- Dilim 2c-2: yeniden giriş ----

    [Fact]
    public async Task After_an_exit_a_target_enters_again_only_after_it_left_the_segment_and_returned()
    {
        var (client, tenantId) = await ManagerAsync();
        var company = await CompanyAsync(client, "Donen Firma", "Bursa");
        var segment = await ActiveSegmentAsync(client, "donen-segment", "Bursa");
        var journey = await PublishedWithPolicyAsync(client, "donen", segment.Id, "AfterExit");
        Assert.Equal(1, (await RunAsync(tenantId)).Sum(o => o.Entered));
        var first = Assert.Single(await EnrollmentsAsync(client, journey.Id));
        Assert.Equal(HttpStatusCode.OK, (await ExitAsync(client, first, "Pause")).StatusCode);

        // Hedef segmentte kalıyor: kayıt sona erse de yeniden girmez (sürekli döngü yok); ayrılma işareti yok.
        Assert.Equal(0, (await RunAsync(tenantId)).Sum(o => o.Entered));
        var stayed = Assert.Single(await EnrollmentsAsync(client, journey.Id));
        Assert.Equal(("Exited", null), (stayed.Status, stayed.SegmentLeftAt));

        // Segmentten ayrılır: yeni kayıt açılmaz, ancak ayrılma işaretlenir.
        company = await SetCityAsync(client, company, "Izmir");
        Assert.Equal(0, (await RunAsync(tenantId)).Sum(o => o.Entered));
        var left = Assert.Single(await EnrollmentsAsync(client, journey.Id));
        Assert.NotNull(left.SegmentLeftAt);

        // Segmente döner: eski kayıt (Exited) korunur, yeni etkin kayıt açılır ve olay yayınlanır.
        await SetCityAsync(client, company, "Bursa");
        Assert.Equal(1, (await RunAsync(tenantId)).Sum(o => o.Entered));
        var all = await EnrollmentsAsync(client, journey.Id);
        Assert.Equal(2, all.Count);
        Assert.Equal(["Active", "Exited"], all.Select(e => e.Status).Order().ToArray());
        using var scope = factory.OpenScope(tenantId);
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().OutboxMessages.AsNoTracking()
            .CountAsync(o => o.EventType == MarketingModule.Events.JourneyEntered));
    }

    [Fact]
    public async Task Never_and_after_completion_policies_do_not_re_enter_after_a_manual_exit_even_when_the_target_returns()
    {
        var (client, tenantId) = await ManagerAsync();
        var company = await CompanyAsync(client, "Politika Firma", "Bursa");
        var segment = await ActiveSegmentAsync(client, "politika-segment", "Bursa");
        var never = await PublishedWithPolicyAsync(client, "hic", segment.Id, "Never");
        var afterCompletion = await PublishedWithPolicyAsync(client, "tamamlaninca", segment.Id, "AfterCompletion");
        Assert.Equal(2, (await RunAsync(tenantId)).Sum(o => o.Entered));
        foreach (var journey in new[] { never, afterCompletion })
        {
            Assert.Equal(HttpStatusCode.OK, (await ExitAsync(client, Assert.Single(await EnrollmentsAsync(client, journey.Id)), "Pause")).StatusCode);
        }

        // Ayrılıp dönmek, çıkışla biten kaydın hedefini bu iki politikada yeniden sokmaz (biri hiç, biri yalnızca tamamlanınca).
        company = await SetCityAsync(client, company, "Izmir");
        await RunAsync(tenantId);
        await SetCityAsync(client, company, "Bursa");
        Assert.Equal(0, (await RunAsync(tenantId)).Sum(o => o.Entered));
        Assert.Single(await EnrollmentsAsync(client, never.Id));
        Assert.Single(await EnrollmentsAsync(client, afterCompletion.Id));
    }

    [Fact]
    public async Task A_run_for_another_tenant_never_marks_this_tenants_enrollments_as_left()
    {
        var (clientA, tenantA) = await ManagerAsync();
        var (_, tenantB) = await ManagerAsync();
        var company = await CompanyAsync(clientA, "A Ayrilan", "Bursa");
        var segment = await ActiveSegmentAsync(clientA, "a-ayrilan", "Bursa");
        var journey = await PublishedWithPolicyAsync(clientA, "a-ayril", segment.Id, "AfterExit");
        await RunAsync(tenantA);
        Assert.Equal(HttpStatusCode.OK, (await ExitAsync(clientA, Assert.Single(await EnrollmentsAsync(clientA, journey.Id)), "Pause")).StatusCode);
        await SetCityAsync(clientA, company, "Izmir");

        // B'nin turu A'nın kaydına dokunmaz; A'nın turu ayrılmayı işaretler.
        await RunAsync(tenantB);
        Assert.Null(Assert.Single(await EnrollmentsAsync(clientA, journey.Id)).SegmentLeftAt);
        await RunAsync(tenantA);
        Assert.NotNull(Assert.Single(await EnrollmentsAsync(clientA, journey.Id)).SegmentLeftAt);
    }

    private static async Task<JourneyDto> PublishedWithPolicyAsync(HttpClient client, string code, Guid segmentId, string policy)
    {
        var created = await client.PostAsJsonAsync("/api/marketing/journeys",
            new SaveJourneyRequest(Code: code, Name: $"Yolculuk {code}", Target: "Company", SegmentId: segmentId, ReentryPolicy: policy));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await Transition(client, (await created.Content.ReadFromJsonAsync<JourneyDto>())!, "publish");
    }

    private static async Task<CompanyDto> SetCityAsync(HttpClient client, CompanyDto company, string city)
    {
        var response = await client.PutAsJsonAsync($"/api/crm/companies/{company.Id}", new SaveCompanyRequest(company.Name, City: city, Version: company.Version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
    }

    // ---- Dilim 2c-3: çıkış koşulu ----

    [Fact]
    public async Task A_target_that_meets_the_exit_condition_leaves_the_journey_and_one_that_already_meets_it_never_enters()
    {
        var (client, tenantId) = await ManagerAsync();
        var a = await CompanyAsync(client, "Kosul A", "Bursa");
        await CompanyAsync(client, "Kosul B", "Bursa");
        var segment = await ActiveSegmentAsync(client, "kosul-segment", "Bursa");
        var journey = await PublishedWithExitAsync(client, "kosullu", segment.Id, ExitWhenIndustry("Lojistik"));
        Assert.Equal(2, (await RunAsync(tenantId)).Sum(o => o.Entered));

        // A "Lojistik" olunca bir sonraki turda çıkar (neden: exit-condition); B etkin kalır.
        await SetIndustryAsync(client, a, "Lojistik");
        await RunAsync(tenantId);
        var enrollments = await EnrollmentsAsync(client, journey.Id);
        var exited = Assert.Single(enrollments, e => e.Status == "Exited");
        Assert.Equal(("exit-condition", a.PartyId), (exited.ExitReason, exited.TargetId));
        Assert.Single(enrollments, e => e.Status == "Active");

        // Koşulu zaten sağlayan yeni hedef yolculuğa hiç girmez.
        await SetIndustryAsync(client, await CompanyAsync(client, "Kosul C", "Bursa"), "Lojistik");
        Assert.Equal(0, (await RunAsync(tenantId)).Sum(o => o.Entered));
        Assert.Equal(2, (await EnrollmentsAsync(client, journey.Id)).Count);
    }

    [Fact]
    public async Task An_exit_condition_with_an_unknown_field_is_saved_but_cannot_be_published_until_fixed()
    {
        var (client, _) = await ManagerAsync();
        var segment = await ActiveSegmentAsync(client, "kosul-hata-segment", "Bursa");
        var created = await client.PostAsJsonAsync("/api/marketing/journeys",
            new SaveJourneyRequest(Code: "kosul-hata", Name: "Koşul hatası", Target: "Company", SegmentId: segment.Id, ExitCondition: Condition("crm.company.yok", "x")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var journey = (await created.Content.ReadFromJsonAsync<JourneyDto>())!;

        // Biçim doğru olduğundan kaydedilir; alan kataloğunda olmadığından yayında alan hatasıyla reddedilir.
        var publish = await client.PostAsJsonAsync($"/api/marketing/journeys/{journey.Id}/publish", new JourneyActionRequest(journey.Version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, publish.StatusCode);
        Assert.Contains("exitCondition", (await publish.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsObject().Select(e => e.Key));
        Assert.Equal("Draft", (await client.GetFromJsonAsync<JourneyDto>($"/api/marketing/journeys/{journey.Id}"))!.Status);

        var fixedUp = await client.PutAsJsonAsync($"/api/marketing/journeys/{journey.Id}",
            new SaveJourneyRequest(journey.Version, journey.Code, journey.Name, journey.Description, journey.Target, journey.SegmentId, journey.ReentryPolicy, ExitWhenIndustry("Lojistik"), journey.OwnerId));
        Assert.Equal(HttpStatusCode.OK, fixedUp.StatusCode);
        journey = (await fixedUp.Content.ReadFromJsonAsync<JourneyDto>())!;
        Assert.Equal("Active", (await Transition(client, journey, "publish")).Status);
    }

    [Fact]
    public async Task Another_tenants_run_does_not_exit_this_tenants_enrollments()
    {
        var (clientA, tenantA) = await ManagerAsync();
        var (_, tenantB) = await ManagerAsync();
        var company = await CompanyAsync(clientA, "A Kosul", "Bursa");
        var segment = await ActiveSegmentAsync(clientA, "a-kosul-segment", "Bursa");
        var journey = await PublishedWithExitAsync(clientA, "a-kosul", segment.Id, ExitWhenIndustry("Lojistik"));
        await RunAsync(tenantA);
        await SetIndustryAsync(clientA, company, "Lojistik");

        await RunAsync(tenantB);
        Assert.Equal("Active", Assert.Single(await EnrollmentsAsync(clientA, journey.Id)).Status);
        await RunAsync(tenantA);
        Assert.Equal("Exited", Assert.Single(await EnrollmentsAsync(clientA, journey.Id)).Status);
    }

    [Fact]
    public async Task A_publisher_who_cannot_read_the_exit_field_cannot_publish_the_journey()
    {
        var tenant = await TenantAsync();
        var manager = await UserClientAsync(tenant, [.. AllMarketing, .. CrmPermissions]);
        var segment = await ActiveSegmentAsync(manager, "kosul-yetki-segment", "Bursa");
        var created = await manager.PostAsJsonAsync("/api/marketing/journeys",
            new SaveJourneyRequest(Code: "kosul-yetki", Name: "Koşul yetki", Target: "Company", SegmentId: segment.Id, ExitCondition: ExitWhenIndustry("Lojistik")));
        var journey = (await created.Content.ReadFromJsonAsync<JourneyDto>())!;

        // CRM firma alanını okuyamayan yayıncı koşulu doğrulayamaz: alan onun için "yok" sayılır (varlığı sızdırılmaz).
        var marketingOnly = await UserClientAsync(tenant, AllMarketing);
        var refused = await marketingOnly.PostAsJsonAsync($"/api/marketing/journeys/{journey.Id}/publish", new JourneyActionRequest(journey.Version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("Active", (await Transition(manager, journey, "publish")).Status);
    }

    private static string Condition(string field, string value) =>
        new JsonObject { ["field"] = field, ["operator"] = "eq", ["values"] = new JsonArray(JsonValue.Create(value)) }.ToJsonString();

    private static string ExitWhenIndustry(string industry) => Condition("crm.company.industry", industry);

    private static async Task<JourneyDto> PublishedWithExitAsync(HttpClient client, string code, Guid segmentId, string exitCondition)
    {
        var created = await client.PostAsJsonAsync("/api/marketing/journeys",
            new SaveJourneyRequest(Code: code, Name: $"Yolculuk {code}", Target: "Company", SegmentId: segmentId, ExitCondition: exitCondition));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await Transition(client, (await created.Content.ReadFromJsonAsync<JourneyDto>())!, "publish");
    }

    private static async Task<CompanyDto> SetIndustryAsync(HttpClient client, CompanyDto company, string industry)
    {
        var response = await client.PutAsJsonAsync($"/api/crm/companies/{company.Id}", new SaveCompanyRequest(company.Name, City: company.City, Industry: industry, Version: company.Version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
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
