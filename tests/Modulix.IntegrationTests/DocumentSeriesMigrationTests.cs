using System.Net;
using System.Net.Http.Json;
using Modulix.Infrastructure.Identity;
using Modulix.Modules.Crm;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Numbering;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0049 dilim 3a: teklif numarası ortak belge serisinden gelir (sipariş, teslimat, operasyon işi ve sözleşme kendi testlerinde biçimiyle
/// denetlenir). Mutlu yol (ardışık numara, revizyon ailenin numarasını taşır ve sıra harcamaz, firma yapılandırması numaraya yansır), doğrulama
/// (reddedilen istek sıra harcamaz), tenant izolasyonu (her firma kendi sırasından başlar), yetki (izinsiz kullanıcı teklif açamaz, sıra harcanmaz).
/// </summary>
public class DocumentSeriesMigrationTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Quotes = "/api/crm/quotes";
    private const string Series = "/api/numbering/series";

    [Fact]
    public async Task Quotes_take_consecutive_numbers_and_revisions_keep_the_family_number()
    {
        var s = await SetUpAsync();
        using var client = await LoginAsync(s.Admin);

        var first = await CreateQuoteAsync(client, s.CompanyId, "İlk teklif");
        var second = await CreateQuoteAsync(client, s.CompanyId, "İkinci teklif");
        Assert.Matches(@"^TK-\d{4}-00001$", first.QuoteNumber);
        Assert.Matches(@"^TK-\d{4}-00002$", second.QuoteNumber);

        // Revizyon aynı ailedir: numarayı taşır, seriden yeni sıra almaz.
        await AddLineAsync(client, first.Id);
        var presented = await TransitionAsync(client, first.Id, "present");
        var revision = await ReadAsync<QuoteDto>(await client.PostAsJsonAsync($"{Quotes}/{presented.Id}/revise", new ReviseQuoteRequest("Fiyat güncellendi")), HttpStatusCode.Created);
        Assert.Equal((first.QuoteNumber, 2), (revision.QuoteNumber, revision.RevisionNo));

        // Firma seriyi yapılandırır: sayaç kaldığı yerden sürer, yeni biçim sonraki teklife yansır.
        var series = await QuoteSeriesAsync(client);
        Assert.Equal(("TK", 2L), (series.Prefix, series.LastValue));
        await ReadAsync<NumberSeriesDto>(
            await client.PutAsJsonAsync($"{Series}/{CrmModule.QuoteNumberSeries}", new ConfigureNumberSeriesRequest("TEK", 4, "Never", series.Version)), HttpStatusCode.OK);
        Assert.Equal("TEK-0001", (await CreateQuoteAsync(client, s.CompanyId, "Üçüncü teklif")).QuoteNumber);
    }

    [Fact]
    public async Task Rejected_requests_do_not_consume_a_number()
    {
        var s = await SetUpAsync();
        using var client = await LoginAsync(s.Admin);
        await CreateQuoteAsync(client, s.CompanyId, "İlk teklif");

        // Başlıksız teklif doğrulamadan geçmez; numara doğrulamadan sonra alındığı için sıra ilerlemez.
        var invalid = await client.PostAsJsonAsync(Quotes, new SaveQuoteRequest(string.Empty, s.CompanyId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal(1, (await QuoteSeriesAsync(client)).LastValue);
        Assert.Matches(@"^TK-\d{4}-00002$", (await CreateQuoteAsync(client, s.CompanyId, "İkinci teklif")).QuoteNumber);
    }

    [Fact]
    public async Task Each_tenant_numbers_its_own_quotes()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var clientA = await LoginAsync(a.Admin);
        using var clientB = await LoginAsync(b.Admin);

        await CreateQuoteAsync(clientA, a.CompanyId, "A-1");
        await CreateQuoteAsync(clientA, a.CompanyId, "A-2");

        // B'nin sırası A'nınkinden bağımsızdır; A'nın sayacı B'nin listesinde görünmez.
        Assert.Matches(@"^TK-\d{4}-00001$", (await CreateQuoteAsync(clientB, b.CompanyId, "B-1")).QuoteNumber);
        Assert.Equal(1, (await QuoteSeriesAsync(clientB)).LastValue);
        Assert.Equal(2, (await QuoteSeriesAsync(clientA)).LastValue);
    }

    [Fact]
    public async Task Users_without_the_quote_write_permission_cannot_take_a_number()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        using var reader = await LoginAsync(s.Reader);

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Quotes, new SaveQuoteRequest("Yetkisiz", s.CompanyId))).StatusCode);
        Assert.Equal(0, (await QuoteSeriesAsync(admin)).LastValue);
    }

    private async Task<(ModulixUser Admin, ModulixUser Reader, Guid CompanyId)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, CrmModule.ModuleKey, enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, admin);
        await factory.CreateRoleAsync(tenant.Id, [CrmModule.Permissions.QuotesRead], reader);
        using var client = await LoginAsync(admin);
        var company = await ReadAsync<CompanyDto>(await client.PostAsJsonAsync("/api/crm/companies", new SaveCompanyRequest("Anka Ltd.")), HttpStatusCode.Created);
        return (admin, reader, company.Id);
    }

    private static async Task<QuoteDto> CreateQuoteAsync(HttpClient client, Guid companyId, string title) =>
        await ReadAsync<QuoteDto>(
            await client.PostAsJsonAsync(Quotes, new SaveQuoteRequest(title, companyId, ValidUntil: DateTimeOffset.UtcNow.AddDays(30))), HttpStatusCode.Created);

    private static async Task AddLineAsync(HttpClient client, Guid quoteId) =>
        await ReadAsync<object>(
            await client.PostAsJsonAsync("/api/crm/quote-lines", new SaveQuoteLineRequest(quoteId, "Kurulum", "Free", null, 1m, 1000m, null, "None", 0m, 20m, null, "Standard")),
            HttpStatusCode.Created);

    private static async Task<QuoteDto> TransitionAsync(HttpClient client, Guid id, string action)
    {
        var current = (await client.GetFromJsonAsync<QuoteDto>($"{Quotes}/{id}"))!;
        return await ReadAsync<QuoteDto>(await client.PostAsJsonAsync($"{Quotes}/{id}/{action}", new QuoteTransitionRequest(current.Version)), HttpStatusCode.OK);
    }

    private static async Task<NumberSeriesDto> QuoteSeriesAsync(HttpClient client) =>
        Assert.Single(await ReadAsync<List<NumberSeriesDto>>(await client.GetAsync(Series), HttpStatusCode.OK), s => s.Key == CrmModule.QuoteNumberSeries);

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
