using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Jobs;
using Modulix.Api.Tenancy;
using Modulix.Core.Contracts.Authorization;
using Modulix.Core.Contracts.Automation;
using Modulix.Core.Contracts.Communication;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Modules;
using Modulix.Infrastructure.Automation;
using Modulix.Infrastructure.Events;
using Modulix.Infrastructure.Identity;
using Modulix.Infrastructure.Persistence;
using Modulix.Modules.Crm;
using Modulix.Modules.Marketing;
using Modulix.Modules.Marketing.Contracts;
using Modulix.Modules.Marketing.Domain;
using Modulix.Modules.Marketing.Journeys;
using Modulix.Shared.Auth;
using Modulix.Shared.Automation;
using Modulix.Shared.Communication;
using Modulix.Shared.Crm;
using Modulix.Shared.Marketing;
using Modulix.Shared.Records;
using Modulix.Shared.Settings;
using static Modulix.Modules.Marketing.MarketingModule.Permissions;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0056 dilim 2a ve 2b: yolculuk iletisi göndericisi ve onun kural motoruna bağlanması (<c>marketing.journey.send</c> aksiyonu, "yolculuğa girildi" olayı). Dört test türü: mutlu yol (iletinin kabulü, abonelik bağlantısı, aynı adımın ikinci kez gitmemesi, ayrı adımın
/// gitmesi), hata/doğrulama (izin yok, alıcı yok, sağlayıcı yok, etkin olmayan yolculuk: hepsi hata değil "gönderilmedi" + neden kodu), tenant izolasyonu ve yetki
/// (alıcı kaynağını okuyamayan kimlik gönderemez). Test sağlayıcısı iletiyi hiçbir yere göndermez.
/// </summary>
public class JourneyMessageTests : IClassFixture<ModulixApiFactory>, IAsyncLifetime
{
    private static readonly System.Collections.Concurrent.ConcurrentBag<MessageRequest> Sent = [];

    private static readonly string[] CrmPermissions =
    [
        CrmModule.Permissions.CompaniesRead, CrmModule.Permissions.CompaniesWrite, CrmModule.Permissions.ContactsRead, CrmModule.Permissions.ContactsWrite,
    ];

    private static readonly string[] Communication =
        [CoreModule.Permissions.ReadCommunication, CoreModule.Permissions.ManageCommunication, CoreModule.Permissions.SuppressCommunication];

    private static readonly string[] Preparer =
    [
        SegmentsRead, SegmentsWrite, SegmentsPublish, RecipientsRead, CampaignsRead, CampaignsWrite, CampaignsAudience, JourneysRead, JourneysWrite, JourneysPublish,
        CoreModule.Permissions.ManageSettings, CoreModule.Permissions.ReadAutomation, CoreModule.Permissions.ManageAutomation, CoreModule.Permissions.PublishAutomation,
        .. CrmPermissions, .. Communication,
    ];

    private readonly ModulixApiFactory factory;
    private readonly WebApplicationFactory<Program> host;

    public JourneyMessageTests(ModulixApiFactory factory)
    {
        this.factory = factory;

        // Abonelik bağlantısı genel adresle kurulur; test sağlayıcısı sarmalanır (aynı sonuçlar, ek olarak istekleri kaydeder; ileti gitmez).
        host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("App:PublicUrl", "https://app.modulix.test");
            builder.ConfigureServices(services =>
            {
                var fake = services.Single(d => d.ServiceType == typeof(IMessageProvider));
                services.Remove(fake);
                services.AddScoped<IMessageProvider>(sp => new RecordingProvider((IMessageProvider)ActivatorUtilities.CreateInstance(sp, fake.ImplementationType!), Sent));
            });
        });
    }

    private sealed class RecordingProvider(IMessageProvider inner, System.Collections.Concurrent.ConcurrentBag<MessageRequest> sent) : IMessageProvider
    {
        public string Key => inner.Key;

        public bool Supports(CommunicationChannel channel) => inner.Supports(channel);

        public Task<ProviderResult> SendAsync(MessageRequest request, CancellationToken cancellationToken)
        {
            sent.Add(request);
            return inner.SendAsync(request, cancellationToken);
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await host.DisposeAsync();

    // ---- Mutlu yol ----

    [Fact]
    public async Task An_enrolled_target_receives_the_message_once_per_step_with_an_unsubscribe_link_and_the_log_keeps_no_address()
    {
        var w = await WorldAsync("ok-ileti@a.test");

        var first = await SendAsync(w, w.Enrollment, "adim-1");
        Assert.Equal(JourneyMessageOutcomes.Accepted, first.Outcome);
        Assert.False(first.AlreadyHandled);
        var request = Assert.Single(Sent, s => s.Address == "ok-ileti@a.test");
        Assert.Equal("Merhaba", request.Subject);
        Assert.Contains("https://app.modulix.test", request.Body);
        Assert.NotNull(request.Unsubscribe);

        // Aynı adım yeniden çağrılsa ileti ikinci kez gitmez (idempotent); sonuç aynıdır.
        var again = await SendAsync(w, w.Enrollment, "adim-1");
        Assert.Equal(JourneyMessageOutcomes.Accepted, again.Outcome);
        Assert.True(again.AlreadyHandled);
        Assert.Single(Sent, s => s.Address == "ok-ileti@a.test");

        // Ayrı adım ayrı iletidir (sıklık sınırı kapalıyken).
        Assert.Equal(JourneyMessageOutcomes.Accepted, (await SendAsync(w, w.Enrollment, "adim-2")).Outcome);
        Assert.Equal(2, Sent.Count(s => s.Address == "ok-ileti@a.test"));

        var rows = await MessagesAsync(w.Tenant.Id, w.Enrollment);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, m => Assert.Equal(JourneyMessageOutcomes.Accepted, m.Outcome));
        Assert.All(rows, m => Assert.StartsWith("fake-", m.ProviderMessageId));
        Assert.DoesNotContain(rows, m => (m.Code ?? string.Empty).Contains('@'));
    }

    // ---- Hata ve doğrulama ----

    [Fact]
    public async Task A_message_that_may_not_go_is_logged_as_skipped_with_a_reason_and_never_reaches_the_provider()
    {
        var w = await WorldAsync("izinsiz@a.test", consent: false);

        // İletişim izni yok.
        var noConsent = await SendAsync(w, w.Enrollment, "izin");
        Assert.Equal(JourneyMessageOutcomes.Skipped, noConsent.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(noConsent.Code));
        Assert.DoesNotContain(Sent, s => s.Address == "izinsiz@a.test");

        // İzin verilir, sonra reddedilir: izni geri alan hedefe ileti gitmez.
        await ConsentAsync(w.Preparer, "izinsiz@a.test", "Granted");
        Assert.Equal(JourneyMessageOutcomes.Accepted, (await SendAsync(w, w.Enrollment, "izin-verildi")).Outcome);
        await ConsentAsync(w.Preparer, "izinsiz@a.test", "Denied");
        var optedOut = await SendAsync(w, w.Enrollment, "cikti");
        Assert.Equal(JourneyMessageOutcomes.Skipped, optedOut.Outcome);
        Assert.Equal(1, Sent.Count(s => s.Address == "izinsiz@a.test"));

        // İzni geri alan alıcı yolculuktan çıkar (ADR-0056 dilim 2c-1): kayıt Exited, sonraki ileti "yolculukta değil" nedeniyle atlanır.
        var exited = (await w.Preparer.GetFromJsonAsync<JourneyEnrollmentDto>($"/api/marketing/journey-enrollments/{w.Enrollment}"))!;
        Assert.Equal(("Exited", "unsubscribed"), (exited.Status, exited.ExitReason));
        Assert.Equal("journey-inactive", (await SendAsync(w, w.Enrollment, "cikti-sonrasi")).Code);

        // Alıcısı olmayan firma (irtibat kişisi yok) ve olmayan kayıt/içerik hata değil "gönderilmedi"/"başarısız" kodudur.
        var lonely = await CompanyAsync(w.Preparer, "Yalniz Tekstil");
        await RunEntryAsync(w.Tenant.Id);
        var lonelyEnrollment = await EnrollmentOfAsync(w.Preparer, w.Journey.Id, lonely.PartyId!.Value);
        var noRecipient = await SendAsync(w, lonelyEnrollment, "alici-yok");
        Assert.Equal(JourneyMessageOutcomes.Skipped, noRecipient.Outcome);
        Assert.Equal(RecipientCodes.NoMatchingContact, noRecipient.Code);
        Assert.Equal("enrollment-not-found", (await SendAsync(w, Guid.NewGuid(), "yok")).Code);
        using (var scope = OpenScope(w))
        {
            var result = await scope.ServiceProvider.GetRequiredService<IJourneySender>().SendAsync(new JourneySendRequest(lonelyEnrollment, Guid.NewGuid(), "icerik-yok"), CancellationToken.None);
            Assert.Equal((JourneyMessageOutcomes.Failed, "content-not-found"), (result.Outcome, result.Code));
        }
    }

    [Fact]
    public async Task No_provider_a_daily_limit_and_an_inactive_journey_stop_the_message_with_a_code()
    {
        var w = await WorldAsync("sinir@a.test", provider: "none");

        // Sağlayıcı seçili değil: gönderilmez.
        var noProvider = await SendAsync(w, w.Enrollment, "saglayici-yok");
        Assert.Equal((JourneyMessageOutcomes.Failed, "provider-missing"), (noProvider.Outcome, noProvider.Code));
        Assert.DoesNotContain(Sent, s => s.Address == "sinir@a.test");

        // Günlük sıklık sınırı 1: ilk ileti gider, ikincisi sınırdan elenir.
        await SetSettingAsync(w.Preparer, MarketingModule.SettingKeys.Provider, "fake");
        await SetSettingAsync(w.Preparer, MarketingModule.SettingKeys.DailyLimit, "1");
        Assert.Equal(JourneyMessageOutcomes.Accepted, (await SendAsync(w, w.Enrollment, "birinci")).Outcome);
        var limited = await SendAsync(w, w.Enrollment, "ikinci");
        Assert.Equal((JourneyMessageOutcomes.Skipped, "frequency-limit"), (limited.Outcome, limited.Code));
        Assert.Equal(1, Sent.Count(s => s.Address == "sinir@a.test"));

        // Duraklatılmış yolculukta ileti gitmez.
        await SetSettingAsync(w.Preparer, MarketingModule.SettingKeys.DailyLimit, "0");
        var journey = (await w.Preparer.GetFromJsonAsync<JourneyDto>($"/api/marketing/journeys/{w.Journey.Id}"))!;
        Assert.Equal(HttpStatusCode.OK, (await w.Preparer.PostAsJsonAsync($"/api/marketing/journeys/{journey.Id}/pause", new JourneyActionRequest(journey.Version))).StatusCode);
        var inactive = await SendAsync(w, w.Enrollment, "duraklatildi");
        Assert.Equal((JourneyMessageOutcomes.Skipped, "journey-inactive"), (inactive.Outcome, inactive.Code));
        Assert.Equal(1, Sent.Count(s => s.Address == "sinir@a.test"));
    }

    // ---- Tenant izolasyonu ----

    [Fact]
    public async Task Another_tenant_cannot_send_for_a_foreign_enrollment_and_the_same_step_is_independent_per_tenant()
    {
        var a = await WorldAsync("a-ortak@a.test");
        var b = await WorldAsync("b-ortak@b.test");

        // B'nin kapsamından A'nın yolculuk kaydı görünmez: ileti gitmez, satır yazılmaz.
        var foreign = await SendAsync(b, a.Enrollment, "adim-1");
        Assert.Equal((JourneyMessageOutcomes.Failed, "enrollment-not-found"), (foreign.Outcome, foreign.Code));
        Assert.Empty(await MessagesAsync(b.Tenant.Id, a.Enrollment));
        Assert.DoesNotContain(Sent, s => s.Address == "a-ortak@a.test");

        // Aynı adım anahtarı iki tenant'ta bağımsız gider.
        Assert.Equal(JourneyMessageOutcomes.Accepted, (await SendAsync(a, a.Enrollment, "adim-1")).Outcome);
        Assert.Equal(JourneyMessageOutcomes.Accepted, (await SendAsync(b, b.Enrollment, "adim-1")).Outcome);
        Assert.Single(await MessagesAsync(a.Tenant.Id, a.Enrollment));
        Assert.Single(await MessagesAsync(b.Tenant.Id, b.Enrollment));
        Assert.Empty(await MessagesAsync(b.Tenant.Id, a.Enrollment));
    }

    // ---- Yetki ----

    [Fact]
    public async Task An_identity_that_cannot_read_the_recipient_source_or_the_content_cannot_send()
    {
        var w = await WorldAsync("yetki@a.test");

        // CRM kişilerini okuyamayan kimlik alıcıyı çözemez: ileti gitmez.
        var noContacts = await UserAsync(w.Tenant, [CampaignsRead, JourneysRead, RecipientsRead]);
        var blocked = await SendAsync(w, w.Enrollment, "yetki-1", as_: noContacts);
        Assert.Equal((JourneyMessageOutcomes.Skipped, "no-access"), (blocked.Outcome, blocked.Code));

        // Kampanya içeriğini okuyamayan kimlik içeriği göremez.
        var noContent = await UserAsync(w.Tenant, [JourneysRead, .. CrmPermissions]);
        var hidden = await SendAsync(w, w.Enrollment, "yetki-2", as_: noContent);
        Assert.Equal((JourneyMessageOutcomes.Failed, "content-not-found"), (hidden.Outcome, hidden.Code));
        Assert.DoesNotContain(Sent, s => s.Address == "yetki@a.test");
    }

    // ---- Dilim 2b: kural motoruna bağlama ----

    [Fact]
    public async Task A_published_journey_rule_sends_each_step_once_when_the_target_enters_the_journey()
    {
        var w = await WorldAsync("kural@a.test");
        var rule = await PublishJourneyRuleAsync(w.Preparer, w.Journey.Id, w.ContentId, "ilk", "ikinci");

        await DeliverAndRunAsync(w.Tenant.Id);
        var rows = await MessagesAsync(w.Tenant.Id, w.Enrollment);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, m => Assert.Equal(JourneyMessageOutcomes.Accepted, m.Outcome));
        Assert.Equal(2, rows.Select(m => m.StepKey).Distinct().Count());
        Assert.Equal(2, Sent.Count(s => s.Address == "kural@a.test"));
        Assert.NotEqual(Guid.Empty, rule);

        // Olaylar yeniden teslim edilse de (en az bir kez teslim) ve kural turu tekrar çalışsa da ileti ikinci kez gitmez.
        await ReplayDeliveriesAsync(w.Tenant.Id);
        await DeliverAndRunAsync(w.Tenant.Id);
        Assert.Equal(2, (await MessagesAsync(w.Tenant.Id, w.Enrollment)).Count);
        Assert.Equal(2, Sent.Count(s => s.Address == "kural@a.test"));
    }

    [Fact]
    public async Task A_rule_bound_to_another_journey_does_not_run_and_a_denied_recipient_skips_the_step_without_failing_the_run()
    {
        var w = await WorldAsync("baska@a.test", consent: false);

        // Koşulu başka bir yolculuğa bağlı kural bu girişte çalışmaz.
        await PublishJourneyRuleAsync(w.Preparer, Guid.NewGuid(), w.ContentId, "yabanci");
        await DeliverAndRunAsync(w.Tenant.Id);
        Assert.Empty(await MessagesAsync(w.Tenant.Id, w.Enrollment));

        // İzni olmayan alıcıda adım atlanır (hata değil): günlükte Skipped, sağlayıcıya ileti gitmez.
        await PublishJourneyRuleAsync(w.Preparer, w.Journey.Id, w.ContentId, "izinsiz");
        await ReplayDeliveriesAsync(w.Tenant.Id);
        await DeliverAndRunAsync(w.Tenant.Id);
        var row = Assert.Single(await MessagesAsync(w.Tenant.Id, w.Enrollment));
        Assert.Equal(JourneyMessageOutcomes.Skipped, row.Outcome);
        Assert.DoesNotContain(Sent, s => s.Address == "baska@a.test");
    }

    [Fact]
    public async Task Another_tenants_rule_never_sends_for_this_tenants_enrollment()
    {
        var a = await WorldAsync("a-kural@a.test");
        var b = await WorldAsync("b-kural@b.test");
        await PublishJourneyRuleAsync(b.Preparer, a.Journey.Id, b.ContentId, "yabanci-kural");

        await DeliverAndRunAsync(a.Tenant.Id);
        await DeliverAndRunAsync(b.Tenant.Id);
        Assert.Empty(await MessagesAsync(a.Tenant.Id, a.Enrollment));
        Assert.Empty(await MessagesAsync(b.Tenant.Id, b.Enrollment));
        Assert.DoesNotContain(Sent, s => s.Address is "a-kural@a.test" or "b-kural@b.test");
    }

    [Fact]
    public async Task A_publisher_who_cannot_read_crm_contacts_cannot_publish_the_send_step()
    {
        var w = await WorldAsync("yayin@a.test");
        var publisher = await UserClientAsync(w.Tenant, [.. Preparer.Where(p => p != CrmModule.Permissions.ContactsRead)]);
        var definition = JourneyRule(w.Journey.Id, w.ContentId, "yetkisiz");
        var create = await publisher.PostAsJsonAsync("/api/automation/rules", new CreateAutomationRuleRequest(
            MarketingModule.ModuleKey, MarketingModule.Entities.JourneyEnrollment, $"Yolculuk {Guid.NewGuid():N}"[..20], null, 100, false, null, null,
            JsonSerializer.SerializeToElement(definition, AutomationJson.Options)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var rule = (await create.Content.ReadFromJsonAsync<AutomationRuleDetailDto>())!;
        var publish = await publisher.PostAsJsonAsync($"/api/automation/rules/{rule.Rule.Id}/publish", new PublishAutomationRuleRequest(true, null));
        Assert.NotEqual(HttpStatusCode.OK, publish.StatusCode);
        Assert.Contains("gereken izne", await publish.Content.ReadAsStringAsync());
    }

    // ---- Dilim 2c-1: tamamlama ve abonelikten çıkma ----

    [Fact]
    public async Task A_rule_ending_with_the_complete_step_completes_the_enrollment_once()
    {
        var w = await WorldAsync("tamam@a.test");
        await PublishRuleAsync(w.Preparer, JourneyRuleWith(w.Journey.Id, Send(w.ContentId, "ilk"), Complete("bitir")));

        await DeliverAndRunAsync(w.Tenant.Id);
        var enrollment = Assert.Single(await EnrollmentsAsync(w.Preparer, w.Journey.Id));
        Assert.Equal("Completed", enrollment.Status);
        Assert.Equal("completed", enrollment.ExitReason);
        Assert.NotNull(enrollment.ExitedAt);
        Assert.Equal(JourneyMessageOutcomes.Accepted, Assert.Single(await MessagesAsync(w.Tenant.Id, w.Enrollment)).Outcome);

        // Olay yeniden teslim edilse de kayıt ikinci kez tamamlanmaz, ileti tekrar gitmez.
        await ReplayDeliveriesAsync(w.Tenant.Id);
        await DeliverAndRunAsync(w.Tenant.Id);
        Assert.Equal("Completed", Assert.Single(await EnrollmentsAsync(w.Preparer, w.Journey.Id)).Status);
        Assert.Equal(1, Sent.Count(s => s.Address == "tamam@a.test"));
    }

    [Fact]
    public async Task A_recipient_who_withdrew_consent_leaves_the_journey_and_the_remaining_steps_are_skipped()
    {
        var w = await WorldAsync("cikan@a.test");
        await ConsentAsync(w.Preparer, "cikan@a.test", "Denied");
        await PublishRuleAsync(w.Preparer, JourneyRuleWith(w.Journey.Id, Send(w.ContentId, "ilk"), Send(w.ContentId, "ikinci"), Complete("bitir")));

        await DeliverAndRunAsync(w.Tenant.Id);

        // Çıkış koşulsuzdur: kayıt abonelikten çıkma nedeniyle sona erer, ikinci ileti ve "tamamla" adımı çalışmaz.
        var enrollment = Assert.Single(await EnrollmentsAsync(w.Preparer, w.Journey.Id));
        Assert.Equal("Exited", enrollment.Status);
        Assert.Equal("unsubscribed", enrollment.ExitReason);
        var row = Assert.Single(await MessagesAsync(w.Tenant.Id, w.Enrollment));
        Assert.Equal(JourneyMessageOutcomes.Skipped, row.Outcome);
        Assert.DoesNotContain(Sent, s => s.Address == "cikan@a.test");
    }

    [Fact]
    public async Task Completing_an_enrollment_of_another_tenant_is_impossible_and_a_publisher_without_journey_write_cannot_publish_the_steps()
    {
        var a = await WorldAsync("a-tamam@a.test");
        var b = await WorldAsync("b-tamam@b.test");

        // B'nin kuralı A'nın yolculuğuna bağlansa bile A'nın kaydını tamamlayamaz.
        await PublishRuleAsync(b.Preparer, JourneyRuleWith(a.Journey.Id, Complete("bitir")));
        await DeliverAndRunAsync(a.Tenant.Id);
        await DeliverAndRunAsync(b.Tenant.Id);
        Assert.Equal("Active", Assert.Single(await EnrollmentsAsync(a.Preparer, a.Journey.Id)).Status);

        // Yazma iznine sahip olmayan yayınlayan tamamla adımını yayınlayamaz.
        var readOnly = await UserClientAsync(a.Tenant, [.. Preparer.Where(p => p != JourneysWrite)]);
        var create = await readOnly.PostAsJsonAsync("/api/automation/rules", new CreateAutomationRuleRequest(
            MarketingModule.ModuleKey, MarketingModule.Entities.JourneyEnrollment, $"Yolculuk {Guid.NewGuid():N}"[..20], null, 100, false, null, null,
            JsonSerializer.SerializeToElement(JourneyRuleWith(a.Journey.Id, Complete("bitir")), AutomationJson.Options)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var rule = (await create.Content.ReadFromJsonAsync<AutomationRuleDetailDto>())!;
        Assert.NotEqual(HttpStatusCode.OK, (await readOnly.PostAsJsonAsync($"/api/automation/rules/{rule.Rule.Id}/publish", new PublishAutomationRuleRequest(true, null))).StatusCode);
    }

    private static AutomationActionStep Send(Guid contentId, string id) =>
        new(id, JourneyAutomation.SendAction, new JsonObject { ["contentId"] = contentId.ToString() });

    private static AutomationActionStep Complete(string id) => new(id, JourneyAutomation.CompleteAction, new JsonObject());

    private static AutomationDefinition JourneyRuleWith(Guid journeyId, params AutomationActionStep[] steps) => new(
        new DomainEventTrigger(MarketingModule.Events.JourneyEntered),
        new ConditionLeaf("marketing.journey-enrollment.journeyId", AutomationOperator.Eq, [journeyId.ToString()]),
        steps);

    private static async Task<Guid> PublishRuleAsync(HttpClient client, AutomationDefinition definition)
    {
        var create = await client.PostAsJsonAsync("/api/automation/rules", new CreateAutomationRuleRequest(
            MarketingModule.ModuleKey, MarketingModule.Entities.JourneyEnrollment, $"Yolculuk {Guid.NewGuid():N}"[..20], null, 100, false, null, null,
            JsonSerializer.SerializeToElement(definition, AutomationJson.Options)));
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var rule = (await create.Content.ReadFromJsonAsync<AutomationRuleDetailDto>())!;
        var publish = await client.PostAsJsonAsync($"/api/automation/rules/{rule.Rule.Id}/publish", new PublishAutomationRuleRequest(true, null));
        Assert.True(publish.StatusCode == HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());
        return rule.Rule.Id;
    }

    private static async Task<List<JourneyEnrollmentDto>> EnrollmentsAsync(HttpClient client, Guid journeyId) =>
        [.. (await client.GetFromJsonAsync<PagedResult<JourneyEnrollmentDto>>($"/api/marketing/journey-enrollments?journeyId={journeyId}&pageSize=100"))!.Items];

    private static AutomationDefinition JourneyRule(Guid journeyId, Guid contentId, params string[] steps) => new(
        new DomainEventTrigger(MarketingModule.Events.JourneyEntered),
        new ConditionLeaf("marketing.journey-enrollment.journeyId", AutomationOperator.Eq, [journeyId.ToString()]),
        [.. steps.Select(id => new AutomationActionStep(id, JourneyAutomation.SendAction, new JsonObject { ["contentId"] = contentId.ToString() }))]);

    private static async Task<Guid> PublishJourneyRuleAsync(HttpClient client, Guid journeyId, Guid contentId, params string[] steps)
    {
        var create = await client.PostAsJsonAsync("/api/automation/rules", new CreateAutomationRuleRequest(
            MarketingModule.ModuleKey, MarketingModule.Entities.JourneyEnrollment, $"Yolculuk {Guid.NewGuid():N}"[..20], null, 100, false, null, null,
            JsonSerializer.SerializeToElement(JourneyRule(journeyId, contentId, steps), AutomationJson.Options)));
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var rule = (await create.Content.ReadFromJsonAsync<AutomationRuleDetailDto>())!;
        var publish = await client.PostAsJsonAsync($"/api/automation/rules/{rule.Rule.Id}/publish", new PublishAutomationRuleRequest(true, null));
        Assert.True(publish.StatusCode == HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());
        return rule.Rule.Id;
    }

    /// <summary>Olayları dağıtır (olay tetikleyicili kurallar çalışma açar), sonra kural turu bekleyen çalışmaları yürütür.</summary>
    private async Task DeliverAndRunAsync(Guid tenantId)
    {
        await DrainAsync(tenantId);
        var (_, skipped) = await host.Services.GetRequiredService<AutomationRunner>().RunTenantAsync(tenantId, CancellationToken.None);
        Assert.False(skipped);
        await DrainAsync(tenantId);
    }

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
    private async Task ReplayDeliveriesAsync(Guid tenantId)
    {
        using var scope = TenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().Database
            .ExecuteSqlRawAsync("DELETE FROM platform.EventDeliveries; UPDATE platform.OutboxMessages SET DispatchedAt = NULL");
    }

    private IServiceScope TenantScope(Guid tenantId)
    {
        var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpTenantContextAccessor>().Current = new TenantContext(tenantId, Guid.Empty, Guid.Empty);
        return scope;
    }

    private async Task<HttpClient> UserClientAsync(Modulix.Core.Platform.Tenant tenant, string[] permissions)
    {
        var user = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, permissions, RecordScope.All, user);
        return await LoginAsync(user);
    }

    // ---- Yardımcılar ----

    private sealed record World(Modulix.Core.Platform.Tenant Tenant, HttpClient Preparer, ModulixUser PreparerUser, JourneyDto Journey, Guid Enrollment, Guid ContentId);

    private async Task<World> WorldAsync(string email, bool consent = true, string provider = "fake")
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, CrmModule.ModuleKey, enabled: true);
        await factory.SetModuleAsync(tenant.Id, MarketingModule.ModuleKey, enabled: true);
        var user = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, Preparer, RecordScope.All, user);
        var client = await LoginAsync(user);
        await SetSettingAsync(client, MarketingModule.SettingKeys.Provider, provider);
        await SetSettingAsync(client, MarketingModule.SettingKeys.DailyLimit, "0");

        var company = await CompanyAsync(client, "Anka Tekstil");
        await ContactAsync(client, company.Id, "Anka", "Kişi", email);
        if (consent)
        {
            await ConsentAsync(client, email, "Granted");
        }

        var segment = await PublishedSegmentAsync(client);
        var campaign = await CampaignAsync(client, "yolculuk-ilet");
        var content = await ContentAsync(client, campaign.Id);
        var journeyResponse = await client.PostAsJsonAsync("/api/marketing/journeys", new SaveJourneyRequest(Code: "ileti-yolculugu", Name: "Yolculuk", Target: "Company", SegmentId: segment.Id));
        Assert.Equal(HttpStatusCode.Created, journeyResponse.StatusCode);
        var journey = (await journeyResponse.Content.ReadFromJsonAsync<JourneyDto>())!;
        var published = await client.PostAsJsonAsync($"/api/marketing/journeys/{journey.Id}/publish", new JourneyActionRequest(journey.Version));
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        journey = (await published.Content.ReadFromJsonAsync<JourneyDto>())!;

        await RunEntryAsync(tenant.Id);
        var enrollment = await EnrollmentOfAsync(client, journey.Id, company.PartyId!.Value);
        return new World(tenant, client, user, journey, enrollment, content.Id);
    }

    private async Task RunEntryAsync(Guid tenantId)
    {
        var run = await host.Services.GetRequiredService<ModuleJobRunner>().RunTenantAsync("marketing.journey-entry", tenantId, CancellationToken.None);
        Assert.Equal(ModuleJobRunStatus.Completed, run.Status);
    }

    private static async Task<Guid> EnrollmentOfAsync(HttpClient client, Guid journeyId, Guid targetId)
    {
        var items = (await client.GetFromJsonAsync<PagedResult<JourneyEnrollmentDto>>($"/api/marketing/journey-enrollments?journeyId={journeyId}&pageSize=100"))!.Items;
        return items.Single(e => e.TargetId == targetId).Id;
    }

    private async Task<JourneySendResult> SendAsync(World w, Guid enrollmentId, string step, (Guid UserId, Guid MembershipId)? as_ = null)
    {
        using var scope = OpenScope(w, as_);
        return await scope.ServiceProvider.GetRequiredService<IJourneySender>().SendAsync(new JourneySendRequest(enrollmentId, w.ContentId, step), CancellationToken.None);
    }

    private IServiceScope OpenScope(World w, (Guid UserId, Guid MembershipId)? actor = null)
    {
        var scope = host.Services.CreateScope();
        var (userId, membershipId) = actor ?? (w.PreparerUser.Id, MembershipOf(w.PreparerUser.Id, w.Tenant.Id));
        scope.ServiceProvider.GetRequiredService<HttpTenantContextAccessor>().Current = new TenantContext(w.Tenant.Id, userId, membershipId);
        return scope;
    }

    private Guid MembershipOf(Guid userId, Guid tenantId) => factory.GetMembershipIdAsync(userId, tenantId).GetAwaiter().GetResult();

    private async Task<(Guid UserId, Guid MembershipId)> UserAsync(Modulix.Core.Platform.Tenant tenant, string[] permissions)
    {
        var user = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, permissions, RecordScope.All, user);
        return (user.Id, await factory.GetMembershipIdAsync(user.Id, tenant.Id));
    }

    private async Task<List<JourneyMessage>> MessagesAsync(Guid tenantId, Guid enrollmentId)
    {
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpTenantContextAccessor>().Current = new TenantContext(tenantId, Guid.NewGuid(), Guid.NewGuid());
        return await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().Set<JourneyMessage>().AsNoTracking()
            .Where(m => m.EnrollmentId == enrollmentId).OrderBy(m => m.CreatedAt).ToListAsync();
    }

    private static async Task<SegmentDto> PublishedSegmentAsync(HttpClient client)
    {
        var condition = new JsonObject { ["field"] = "crm.company.name", ["operator"] = "contains", ["values"] = new JsonArray("Tekstil") };
        var created = await client.PostAsJsonAsync("/api/marketing/segments", new SaveSegmentRequest(Code: "tekstil", Name: "Tekstil", Target: "Company", Kind: "Dynamic", Draft: condition.ToJsonString()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var segment = (await created.Content.ReadFromJsonAsync<SegmentDto>())!;
        var published = await client.PostAsJsonAsync($"/api/marketing/segments/{segment.Id}/publish", new SegmentActionRequest());
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return (await published.Content.ReadFromJsonAsync<SegmentDto>())!;
    }

    private static async Task<CampaignDto> CampaignAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/marketing/campaigns",
            new SaveCampaignRequest(Code: code, Name: $"Kampanya {code}", Target: "Company", Channel: "Email", RecipientRule: "PrimaryContact"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CampaignDto>())!;
    }

    private static async Task<CampaignContentDto> ContentAsync(HttpClient client, Guid campaignId)
    {
        var response = await client.PostAsJsonAsync("/api/marketing/campaign-contents",
            new SaveCampaignContentRequest(CampaignId: campaignId, Subject: "Merhaba", Body: "Antrepo hizmetimiz hakkında bilgi: https://example.test/antrepo"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CampaignContentDto>())!;
    }

    private static async Task<CompanyDto> CompanyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/crm/companies", new SaveCompanyRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
    }

    private static async Task ContactAsync(HttpClient client, Guid companyId, string first, string last, string email)
    {
        var response = await client.PostAsJsonAsync("/api/crm/contacts", new SaveContactRequest(first, last));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var contact = (await response.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.True((await client.PostAsJsonAsync("/api/crm/company-contacts", new SaveCompanyContactRequest(companyId, contact.Id, IsPrimary: true))).IsSuccessStatusCode);
        Assert.True((await client.PostAsJsonAsync("/api/crm/person-contact-points", new SavePersonContactPointRequest(contact.Id, email, "Email"))).IsSuccessStatusCode);
    }

    private static async Task ConsentAsync(HttpClient client, string address, string status)
    {
        var request = new SaveConsentRequest("Email", address, "Marketing", status, "manual", "test-kaniti", null, null, null);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/communication/preferences", request)).StatusCode);
    }

    private static async Task SetSettingAsync(HttpClient client, string key, string value)
    {
        var current = (await client.GetFromJsonAsync<SettingsResponse>("/api/settings"))!.Settings.First(s => s.Key == key);
        var response = await client.PutAsJsonAsync("/api/settings", new SaveSettingsRequest(new Dictionary<string, SettingWriteDto> { [key] = new(value, current.Version) }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
