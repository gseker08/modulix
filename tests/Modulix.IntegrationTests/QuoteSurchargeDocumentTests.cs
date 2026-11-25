using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Authorization;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Müşteri belgesinde ek ücret gösterimi (ADR-0018 devamı, Dilim 2c): kuralın sunum seçimi müşteriye giden veride uygulanır. Gizlenen ve gruba dahil edilen
/// ek ücret satırı herkese açık veride hiç yoktur (yalnızca arayüzde saklanmış olmaz), tutarlar toplamda ve vergide kalır, hesabın iç ayrıntısı verilmez.
/// </summary>
public class QuoteSurchargeDocumentTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Rules = "/api/crm/surcharge-rules";

    [Fact]
    public async Task The_customer_view_follows_each_rules_presentation_and_never_exposes_hidden_charges()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Ayrı ücret", "Fixed", "EUR", Priority: 10, Amount: 10m, Group: "ardiye-bedeli", Presentation: "SeparateLine"));
        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Dahil ücret", "Fixed", "EUR", Priority: 20, Amount: 20m, Group: "ardiye-bedeli", Presentation: "IncludedInGroup"));
        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("GIZLI-DOKUM-UCRETI", "Fixed", "EUR", Priority: 30, Amount: 30m, Group: "ardiye-bedeli", Presentation: "BreakdownOnly"));

        var quote = await PostAsync<QuoteDto>(admin, "/api/crm/quotes", new SaveQuoteRequest("Ardiye teklifi", s.CompanyId, Currency: "EUR", ValidUntil: DateTimeOffset.UtcNow.AddDays(30)));
        await PostAsync<QuoteLineDto>(admin, "/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, "Kalem", "Free", null, 1m, 100m, TaxRatePercent: 20m, GroupName: "Ardiye Bedeli"));

        // İç görünümde dört satırın hepsi vardır.
        Assert.Equal(4, (await admin.GetFromJsonAsync<CrmPage<QuoteLineDto>>($"/api/crm/quote-lines?quoteId={quote.Id}"))!.Items.Count);

        var present = await admin.PostAsJsonAsync($"/api/crm/quotes/{quote.Id}/present", new QuoteTransitionRequest((await admin.GetFromJsonAsync<QuoteDto>($"/api/crm/quotes/{quote.Id}"))!.Version));
        Assert.Equal(HttpStatusCode.OK, present.StatusCode);
        var link = await PostAsync<QuoteShareLinkCreatedDto>(admin, $"/api/crm/quotes/{quote.Id}/share-links", new CreateQuoteShareLinkRequest());
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync($"/api/public/quotes/{s.Slug}/view", new PublicQuoteTokenRequest(link.Token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var content = System.Text.Json.JsonSerializer.Deserialize<PublicQuoteView>(raw, System.Text.Json.JsonSerializerOptions.Web)!.Content!;

        // Satırlar: elle kalem ve ayrı gösterilen ek ücret; gruba dahil ve yalnız döküm satırları müşteri verisinde yoktur.
        Assert.Equal(["Kalem", "Ayrı ücret"], content.Lines.Select(l => l.Name));
        Assert.DoesNotContain("GIZLI-DOKUM-UCRETI", raw);
        Assert.DoesNotContain("Dahil ücret", raw);
        Assert.Null(content.Lines.Single(l => l.Name == "Ayrı ücret").Description); // hesabın iç ayrıntısı müşteriye verilmez

        // Alt toplam gruba dahil edilen ek ücreti kapsar (100 + 10 + 20); yalnız döküm tutarı alt toplamda da yoktur, bayrak yanar.
        var subtotal = Assert.Single(content.GroupSubtotals!);
        Assert.Equal(("Ardiye Bedeli", 130m), (subtotal.GroupName, subtotal.Net));
        Assert.True(content.IncludesUnlistedCharges);

        // Toplamlar ve vergi görünmeyen ek ücretleri de içerir: 100 + 10 + 20 + 30 = 160, %20 vergi 32.
        Assert.Equal((160m, 32m, 192m), (content.NetTotal, content.TaxTotal, content.GrossTotal));
        var tax = Assert.Single(content.TaxGroups);
        Assert.Equal((20m, 160m, 32m), (tax.RatePercent, tax.Net, tax.Tax));
    }

    [Fact]
    public async Task A_quote_without_hidden_charges_has_no_note_and_no_subtotals()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Ayrı ücret", "Fixed", "EUR", Amount: 10m, Group: "ardiye-bedeli"));
        var quote = await PostAsync<QuoteDto>(admin, "/api/crm/quotes", new SaveQuoteRequest("Teklif", s.CompanyId, Currency: "EUR", ValidUntil: DateTimeOffset.UtcNow.AddDays(30)));
        await PostAsync<QuoteLineDto>(admin, "/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, "Kalem", "Free", null, 1m, 100m, GroupName: "Ardiye Bedeli"));
        await admin.PostAsJsonAsync($"/api/crm/quotes/{quote.Id}/present", new QuoteTransitionRequest((await admin.GetFromJsonAsync<QuoteDto>($"/api/crm/quotes/{quote.Id}"))!.Version));

        var link = await PostAsync<QuoteShareLinkCreatedDto>(admin, $"/api/crm/quotes/{quote.Id}/share-links", new CreateQuoteShareLinkRequest());
        using var anonymous = factory.CreateClient();
        var view = (await (await anonymous.PostAsJsonAsync($"/api/public/quotes/{s.Slug}/view", new PublicQuoteTokenRequest(link.Token))).Content.ReadFromJsonAsync<PublicQuoteView>())!;

        Assert.Equal(["Kalem", "Ayrı ücret"], view.Content!.Lines.Select(l => l.Name));
        Assert.False(view.Content.IncludesUnlistedCharges);
        Assert.True(view.Content.GroupSubtotals is null or { Count: 0 });
    }

    private static async Task<SurchargeRuleDto> PostRuleAsync(HttpClient client, SaveSurchargeRuleRequest body) =>
        await PostAsync<SurchargeRuleDto>(client, Rules, body with { TaxRatePercent = body.TaxRatePercent ?? 20m });

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<(ModulixUser Admin, Guid CompanyId, string Slug)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        using var client = await LoginAsync(admin);
        var company = await PostAsync<CompanyDto>(client, "/api/crm/companies", new SaveCompanyRequest("Anka Ltd."));
        return (admin, company.Id, tenant.Slug);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
