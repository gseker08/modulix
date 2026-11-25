using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Authorization;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Customization;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Teklifin ek ücret hesap dökümü (<c>GET /api/crm/quotes/{id}/surcharges</c>; ADR-0018 devamı, Dilim 2c ilk aşama): etkin kurallar teklifin özel
/// alanları ve kalemleriyle çalışır, uygulanmayan kural nedeniyle listelenir, salt okunurdur; yetki ve tenant izolasyonu.
/// </summary>
public class QuoteSurchargePreviewTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Rules = "/api/crm/surcharge-rules";
    private static readonly string[] ReaderPermissions = [QuotesRead, CompaniesRead];
    private static readonly string[] NoQuotePermissions = [ProductsRead];

    [Fact]
    public async Task Active_rules_run_against_the_quote_and_unapplied_rules_are_listed_with_their_reason()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var cargo = await CreateFieldAsync(admin, new SaveCustomFieldRequest("crm.quote", "Yük özelliği", CustomFieldType.Choice, Options: [new(null, "ADR"), new(null, "Standart")]));
        var days = await CreateFieldAsync(admin, new SaveCustomFieldRequest("crm.quote", "Süre gün", CustomFieldType.Number));
        var declarations = await CreateFieldAsync(admin, new SaveCustomFieldRequest("crm.quote", "Beyanname adedi", CustomFieldType.Number));
        var adr = cargo.Options.Single(o => o.Label == "ADR").Key;

        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("ADR ek ücreti", "Fixed", "EUR", Priority: 20, Amount: 100m, Group: "ardiye-bedeli",
            QuantityKeys: new JsonArray(declarations.Key), Conditions: new JsonArray(Condition(cargo.Key, "Equals", adr))));
        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Demuraj", "Duration", "EUR", Priority: 40, Percent: 10m, BaseKey: "quote.subtotal", DurationKey: days.Key,
            FreeUnits: 7m, Group: "ardiye-bedeli", QuantityKeys: new JsonArray(declarations.Key)));
        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Asgari ardiye", "Minimum", "EUR", Priority: 90, Amount: 125m, Group: "ardiye-bedeli"));
        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Dolar kuralı", "Fixed", "USD", Amount: 5m)); // başka para birimi: hiç değerlendirilmez
        var passive = await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Pasif kural", "Fixed", "EUR", Amount: 1m, IsActive: false));

        var quote = await NewQuoteAsync(admin, s.CompanyId, new JsonObject { [cargo.Key] = adr, [days.Key] = 10, [declarations.Key] = 1 });
        await AddLineAsync(admin, quote.Id, "Parsiyel ardiye", 150m, "Ardiye Bedeli");

        var breakdown = await GetBreakdownAsync(admin, quote.Id);

        Assert.Equal("EUR", breakdown.Currency);
        Assert.Equal(["ADR ek ücreti", "Demuraj"], breakdown.Applied.Select(a => a.Name));
        Assert.Equal([100m, 45m], breakdown.Applied.Select(a => a.Amount)); // 100 × 1 beyan; %10 × 150 × (10 − 7) gün × 1 beyan
        Assert.Equal(145m, breakdown.Total);
        Assert.Equal(295m, breakdown.GroupTotals["ardiye-bedeli"]); // 150 + 100 + 45
        var skipped = Assert.Single(breakdown.Skipped);
        Assert.Equal("Asgari ardiye", skipped.Name);
        Assert.Contains("aşıldı", skipped.Reason);
        Assert.DoesNotContain(breakdown.Applied, a => a.Name is "Dolar kuralı" or "Pasif kural");
        Assert.DoesNotContain(breakdown.Skipped, x => x.Name is "Dolar kuralı" or "Pasif kural");
        Assert.NotEqual(Guid.Empty, passive.Id);

        // Salt okunurdur: teklifin toplamları değişmez.
        var after = (await admin.GetFromJsonAsync<QuoteDto>($"/api/crm/quotes/{quote.Id}"))!;
        Assert.Equal(quote.NetTotal + 150m, after.NetTotal);

        // Değeri girilmemiş teklifte kurallar nedeniyle uygulanmaz; fiyat uydurulmaz.
        var empty = await NewQuoteAsync(admin, s.CompanyId, null);
        await AddLineAsync(admin, empty.Id, "Parsiyel ardiye", 100m, "Ardiye Bedeli");
        var bare = await GetBreakdownAsync(admin, empty.Id);
        Assert.Contains(bare.Skipped, x => x.Name == "Demuraj" && x.Reason.Contains("Eksik değer"));
        Assert.Contains(bare.Skipped, x => x.Name == "ADR ek ücreti");
        Assert.Equal(25m, Assert.Single(bare.Applied).Amount); // asgari 125 − 100: yalnızca fark
    }

    [Fact]
    public async Task The_breakdown_needs_quote_read_and_never_crosses_tenants()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        using var reader = await LoginAsync(a.Reader);
        using var noQuote = await LoginAsync(a.NoQuote);

        await PostRuleAsync(adminA, new SaveSurchargeRuleRequest("Sabit", "Fixed", "TRY", Amount: 10m));
        var quote = await NewQuoteAsync(adminA, a.CompanyId, null, currency: "TRY");
        await AddLineAsync(adminA, quote.Id, "Kalem", 100m, null);

        // Teklif okuma izni yeter (kural yönetimi izni gerekmez); teklif izni olmayan göremez.
        var ok = await reader.GetAsync($"/api/crm/quotes/{quote.Id}/surcharges");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(10m, (await ok.Content.ReadFromJsonAsync<QuoteSurchargeBreakdownDto>())!.Total);
        Assert.Equal(HttpStatusCode.Forbidden, (await noQuote.GetAsync($"/api/crm/quotes/{quote.Id}/surcharges")).StatusCode);

        // Tenant izolasyonu: B firması A'nın teklifini bulamaz, A'nın kuralı B'nin hesabına karışmaz.
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync($"/api/crm/quotes/{quote.Id}/surcharges")).StatusCode);
        var own = await NewQuoteAsync(adminB, b.CompanyId, null, currency: "TRY");
        await AddLineAsync(adminB, own.Id, "Kalem", 100m, null);
        var bBreakdown = await GetBreakdownAsync(adminB, own.Id);
        Assert.Empty(bBreakdown.Applied);
        Assert.Empty(bBreakdown.Skipped);
    }

    private static JsonObject Condition(string key, string op, string value) => new() { ["key"] = key, ["operator"] = op, ["value"] = value };

    private static async Task<QuoteSurchargeBreakdownDto> GetBreakdownAsync(HttpClient client, Guid quoteId)
    {
        var response = await client.GetAsync($"/api/crm/quotes/{quoteId}/surcharges");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<QuoteSurchargeBreakdownDto>())!;
    }

    private static async Task<QuoteDto> NewQuoteAsync(HttpClient client, Guid companyId, JsonObject? custom, string currency = "EUR") =>
        await PostAsync<QuoteDto>(client, "/api/crm/quotes", new SaveQuoteRequest("Ardiye teklifi", companyId, Currency: currency, Custom: custom));

    private static async Task AddLineAsync(HttpClient client, Guid quoteId, string name, decimal price, string? group) =>
        await PostAsync<QuoteLineDto>(client, "/api/crm/quote-lines", new SaveQuoteLineRequest(quoteId, name, "Free", null, 1m, price, GroupName: group));

    private static async Task<SurchargeRuleDto> PostRuleAsync(HttpClient client, SaveSurchargeRuleRequest body) =>
        await PostAsync<SurchargeRuleDto>(client, Rules, body);

    private static async Task<CustomFieldDto> CreateFieldAsync(HttpClient client, SaveCustomFieldRequest request) =>
        await PostAsync<CustomFieldDto>(client, "/api/customization/fields", request);

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<(ModulixUser Admin, ModulixUser Reader, ModulixUser NoQuote, Guid CompanyId)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        var noQuote = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, ReaderPermissions, Scope.All, reader);
        await factory.CreateRoleAsync(tenant.Id, NoQuotePermissions, Scope.All, noQuote);
        using var client = await LoginAsync(admin);
        var company = await PostAsync<CompanyDto>(client, "/api/crm/companies", new SaveCompanyRequest("Anka Ltd."));
        return (admin, reader, noQuote, company.Id);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
