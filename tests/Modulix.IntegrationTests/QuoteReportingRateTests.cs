using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Authorization;
using Modulix.Core.Contracts.Currency;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Records;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Teklifte gösterge para birimi (ADR-0049 dilim 5d devamı): kur verilmemişse kur defterinden teklif tarihine göre otomatik alınır, kur yoksa uydurulmaz (422),
/// açık kur korunur, para birimi çifti değişince eski kur yenilenir, toplam kurdan etkilenmez; kur defteri firmaya özeldir.
/// </summary>
public class QuoteReportingRateTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly string[] RepPermissions = [QuotesRead, QuotesWrite, CompaniesRead, QuotesManualPrice];

    [Fact]
    public async Task The_rate_is_taken_from_the_rate_book_and_the_total_is_unaffected()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        await WriteRateAsync(admin, "EUR", "TRY", 40m);
        await WriteRateAsync(admin, "USD", "TRY", 36m);

        var quote = await PostAsync<QuoteDto>(admin, "/api/crm/quotes", New(s.CompanyId, currency: "EUR", reporting: "TRY"));
        Assert.Equal(("TRY", 40m), (quote.ReportingCurrency, quote.ExchangeRate));
        Assert.NotNull(quote.ExchangeRateDate);
        await PostAsync<QuoteLineDto>(admin, "/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, "Kalem", "Free", null, 1m, 100m));
        Assert.Equal(100m, (await GetAsync(admin, quote.Id)).NetTotal);

        // Para birimi değişir, istemci eski kuru olduğu gibi geri gönderir: kur yeni çifte göre yenilenir.
        var current = await GetAsync(admin, quote.Id);
        var changed = await admin.PutAsJsonAsync($"/api/crm/quotes/{quote.Id}", Save(current) with { Currency = "USD" });
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var after = (await changed.Content.ReadFromJsonAsync<QuoteDto>())!;
        Assert.Equal(36m, after.ExchangeRate);
        Assert.Equal(100m, after.NetTotal);
    }

    [Fact]
    public async Task A_missing_rate_is_never_invented_and_an_explicit_rate_is_kept()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);

        var missing = await admin.PostAsJsonAsync("/api/crm/quotes", New(s.CompanyId, currency: "EUR", reporting: "TRY"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Contains("reportingCurrency", await missing.Content.ReadAsStringAsync());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var explicitRate = await PostAsync<QuoteDto>(admin, "/api/crm/quotes",
            New(s.CompanyId, currency: "EUR", reporting: "TRY") with { ExchangeRate = 41.5m, ExchangeRateDate = today });
        Assert.Equal((41.5m, today), (explicitRate.ExchangeRate, explicitRate.ExchangeRateDate));

        // Gösterge para birimi teklif para biriminden farklı olmalıdır (var olan kural).
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsJsonAsync("/api/crm/quotes", New(s.CompanyId, currency: "EUR", reporting: "EUR"))).StatusCode);
    }

    [Fact]
    public async Task The_rate_book_is_per_tenant_and_a_rep_uses_it_without_managing_rates()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        using var repA = await LoginAsync(a.Rep);
        await WriteRateAsync(adminA, "EUR", "TRY", 40m);

        // A'nın temsilcisi kur yönetme izni olmadan gösterge kurunu alır; B'nin defterinde kur yoktur, B teklifi kur uydurmaz.
        var quote = await PostAsync<QuoteDto>(repA, "/api/crm/quotes", New(a.CompanyId, currency: "EUR", reporting: "TRY"));
        Assert.Equal(40m, quote.ExchangeRate);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await adminB.PostAsJsonAsync("/api/crm/quotes", New(b.CompanyId, currency: "EUR", reporting: "TRY"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await repA.PostAsJsonAsync("/api/records/core.exchange-rate", new RecordWriteRequest(new JsonObject
            {
                ["fromCurrency"] = "USD", ["toCurrency"] = "TRY", ["rate"] = 1m, ["validOn"] = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), ["source"] = ExchangeRateSources.Manual,
            }))).StatusCode);
    }

    private static SaveQuoteRequest New(Guid companyId, string currency, string reporting) =>
        new("Gösterge teklifi", companyId, Currency: currency, ReportingCurrency: reporting, ValidUntil: DateTimeOffset.UtcNow.AddDays(30));

    private static SaveQuoteRequest Save(QuoteDto q) => new(
        q.Title, q.CompanyId, q.Description, q.ContactId, q.OpportunityId, q.PriceListId, q.OwnerId, q.IssueDate, q.ValidUntil, q.Currency, q.ReportingCurrency,
        q.ExchangeRate, q.ExchangeRateDate, q.PriceIncludesTax, q.DocumentDiscountKind, q.DocumentDiscountValue, q.PaymentTerms, q.DeliveryTerms, q.CommercialTerms,
        q.InternalNotes, q.Version, q.Custom);

    private static async Task<QuoteDto> GetAsync(HttpClient client, Guid id) => (await client.GetFromJsonAsync<QuoteDto>($"/api/crm/quotes/{id}"))!;

    private static async Task WriteRateAsync(HttpClient admin, string from, string to, decimal rate)
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var response = await admin.PostAsJsonAsync("/api/records/core.exchange-rate", new RecordWriteRequest(new JsonObject
        {
            ["fromCurrency"] = from, ["toCurrency"] = to, ["rate"] = rate, ["validOn"] = day.ToString("yyyy-MM-dd"), ["source"] = ExchangeRateSources.Manual,
        }));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<(ModulixUser Admin, ModulixUser Rep, Guid CompanyId)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        var rep = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, RepPermissions, Scope.All, rep);
        using var client = await LoginAsync(admin);
        var company = await PostAsync<CompanyDto>(client, "/api/crm/companies", new SaveCompanyRequest("Anka Ltd."));
        return (admin, rep, company.Id);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
