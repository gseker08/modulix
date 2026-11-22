using System.Net;
using System.Net.Http.Json;
using Modulix.Core.Contracts.Authorization;
using Modulix.Modules.Crm;
using Modulix.Modules.Marketing;
using Modulix.Shared.Crm;
using Modulix.Shared.Marketing;
using Modulix.Shared.Records;
using static Modulix.Modules.Marketing.MarketingModule.Permissions;

namespace Modulix.IntegrationTests;

// ADR-0053 dilim 2d: müşteri zaman çizelgesinin pazarlama kaynağı. Müşteriye giden kampanya iletileri ve gelen yanıtlar CRM kayıtlarıyla aynı akışta görünür;
// satırlar pazarlama geçmişinin izin ve kampanya veri kapsamıyla süzülür. Dört test türü; CampaignRunTests'in dünya kurulumunu paylaşır.
public partial class CampaignRunTests
{
    private const string TouchesSource = "marketing.touches";

    private static async Task<CustomerTimelineDto> TimelineAsync(HttpClient client, Guid companyId, string query = "") =>
        (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{companyId}/timeline{query}"))!;

    /// <summary>Teslimatın hedefi olan firmanın CRM kimliği (hedef kimliği ortak taraf kimliğidir).</summary>
    private static async Task<Guid> CompanyOfAsync(HttpClient client, Guid partyId)
    {
        var page = (await client.GetFromJsonAsync<PagedResult<CompanyDto>>("/api/crm/companies?pageSize=50"))!;
        return page.Items.Single(c => c.PartyId == partyId).Id;
    }

    private async Task<HttpClient> ReaderAsync(Modulix.Core.Platform.Tenant tenant, params string[] marketing) =>
        await UserClientAsync(tenant, [CrmModule.Permissions.CompaniesRead, .. marketing]);

    // ---- Mutlu yol ----

    [Fact]
    public async Task Timeline_lists_the_messages_and_responses_sent_to_the_customer_newest_first()
    {
        var s = await SentWorldAsync();
        var delivery = s.Accepted[0];
        await RegisterAsync(s.Responder, delivery.Id, "Question");
        var reader = await ReaderAsync(s.World.Tenant, Reporter);
        var companyId = await CompanyOfAsync(reader, delivery.TargetId);

        var timeline = await TimelineAsync(reader, companyId);

        Assert.Contains(TouchesSource, timeline.Sources);
        // Yanıt, yanıtladığı iletiden sonra alındı: önce o gelir. Satırın kayıt kimliği kampanyadır, adres ya da içerik taşımaz.
        Assert.Equal([MarketingTimelineKeys.Response, MarketingTimelineKeys.Delivery], timeline.Items.Select(i => i.EntityKey));
        var response = timeline.Items[0];
        Assert.Equal((s.World.Campaign.Id, s.World.Campaign.Name, "Question"), (response.RecordId, response.Title, response.Type));
        Assert.Equal("Accepted", timeline.Items[1].Status);

        // İmleç: tek satırlık sayfalarla ikisi de gezilir.
        var first = await TimelineAsync(reader, companyId, "?take=1");
        Assert.Equal(MarketingTimelineKeys.Response, Assert.Single(first.Items).EntityKey);
        var before = Uri.EscapeDataString(first.NextBefore!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var second = await TimelineAsync(reader, companyId, $"?take=1&before={before}");
        Assert.Equal(MarketingTimelineKeys.Delivery, Assert.Single(second.Items).EntityKey);
        Assert.Null(second.NextBefore);
    }

    // ---- Hata / doğrulama ----

    [Fact]
    public async Task A_customer_with_no_campaign_touches_has_the_source_but_no_rows_and_an_unknown_company_is_404()
    {
        var s = await SentWorldAsync();
        var reader = await ReaderAsync(s.World.Tenant, Reporter);
        var untouched = await CompanyAsync(s.World.Preparer, "Dokunulmamış A.Ş.");

        var timeline = await TimelineAsync(reader, untouched.Id);

        // Okunabilir ama kayıt yok: kaynak listede, satır yok.
        Assert.Contains(TouchesSource, timeline.Sources);
        Assert.DoesNotContain(timeline.Items, i => i.EntityKey is MarketingTimelineKeys.Delivery or MarketingTimelineKeys.Response);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/crm/companies/{Guid.NewGuid()}/timeline")).StatusCode);
    }

    // ---- Tenant izolasyonu ----

    [Fact]
    public async Task Timeline_never_shows_another_tenants_campaign_touches()
    {
        var a = await SentWorldAsync();
        var b = await SentWorldAsync();
        var readerA = await ReaderAsync(a.World.Tenant, Reporter);
        var readerB = await ReaderAsync(b.World.Tenant, Reporter);
        var companyA = await CompanyOfAsync(readerA, a.Accepted[0].TargetId);

        Assert.Contains((await TimelineAsync(readerA, companyA)).Items, i => i.EntityKey == MarketingTimelineKeys.Delivery);

        // B kullanıcısı A'nın firmasının çizelgesini açamaz.
        Assert.Equal(HttpStatusCode.NotFound, (await readerB.GetAsync($"/api/crm/companies/{companyA}/timeline")).StatusCode);
    }

    // ---- Yetki ----

    [Fact]
    public async Task Marketing_rows_follow_the_report_and_recipient_permissions_and_the_campaign_data_scope()
    {
        var s = await SentWorldAsync();
        var delivery = s.Accepted[0];
        var companyId = await CompanyOfAsync(s.World.Preparer, delivery.TargetId);

        // Pazarlama izni yok ya da rapor/alıcı izni eksik: kaynak de satır da yok ("kayıt yok" gibi gösterilmez).
        foreach (var permissions in new[] { Array.Empty<string>(), [CampaignsRead, ResponsesRead, RecipientsRead], [CampaignsRead, ReportsRead] })
        {
            var client = await ReaderAsync(s.World.Tenant, permissions);
            var timeline = await TimelineAsync(client, companyId);
            Assert.DoesNotContain(TouchesSource, timeline.Sources);
            Assert.DoesNotContain(timeline.Items, i => i.EntityKey is MarketingTimelineKeys.Delivery or MarketingTimelineKeys.Response);
        }

        // Dar kapsam: kampanya başkasının olduğundan kaynak okunur ama satır gelmez.
        var scoped = await factory.CreateUserAsync(s.World.Tenant);
        await factory.CreateRoleAsync(s.World.Tenant.Id, [CrmModule.Permissions.CompaniesRead, .. Reporter], RecordScope.Own, scoped);
        var scopedClient = await LoginAsync(scoped);
        var ownCompany = await CompanyAsync(scopedClient, "Temsilcinin firması");
        var scopedTimeline = await TimelineAsync(scopedClient, ownCompany.Id);
        Assert.Contains(TouchesSource, scopedTimeline.Sources);
        Assert.Empty(scopedTimeline.Items);

        // Pazarlama modülü kapalıysa kaynak yoktur.
        await factory.SetModuleAsync(s.World.Tenant.Id, MarketingModule.ModuleKey, enabled: false);
        var off = await TimelineAsync(await ReaderAsync(s.World.Tenant, Reporter), companyId);
        Assert.DoesNotContain(TouchesSource, off.Sources);
    }
}
