using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Authorization;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Customization;
using Modulix.Shared.Records;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Özel alanın ticari kilidi (ADR-0019 kilit kararı, ADR-0018 dilim 2e): ticari işaretli alan ve ek ücret kuralında kullanılan alan sunulmuş teklifte
/// yerinde değişmez, işaretsiz ve kullanılmayan alan serbest kalır, taslakta hepsi yazılır; bayrak tenant'a özeldir ve alan yönetimi izni ister.
/// </summary>
public class QuoteCommercialFieldLockTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string QuoteKey = "crm.quote";

    private static readonly string[] RepPermissions = [QuotesRead, QuotesWrite, CompaniesRead];

    [Fact]
    public async Task Commercial_and_rule_used_fields_are_locked_once_presented_while_plain_fields_stay_writable()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var flagged = await CreateFieldAsync(admin, new SaveCustomFieldRequest(QuoteKey, "Teminat türü", CustomFieldType.Text, IsCommercial: true));
        var ruleUsed = await CreateFieldAsync(admin, new SaveCustomFieldRequest(QuoteKey, "Süre gün", CustomFieldType.Number));
        var plain = await CreateFieldAsync(admin, new SaveCustomFieldRequest(QuoteKey, "Operasyon notu", CustomFieldType.Text));
        Assert.True(flagged.IsCommercial);
        Assert.False(plain.IsCommercial);
        await PostAsync<SurchargeRuleDto>(admin, "/api/crm/surcharge-rules", new SaveSurchargeRuleRequest("Demuraj", "Duration", "EUR", Percent: 10m,
            BaseKey: "quote.subtotal", DurationKey: ruleUsed.Key, FreeUnits: 7m, TaxRatePercent: 20m));

        var quote = await PostAsync<QuoteDto>(admin, "/api/crm/quotes", new SaveQuoteRequest("Teklif", s.CompanyId, Currency: "EUR",
            ValidUntil: DateTimeOffset.UtcNow.AddDays(30), Custom: new JsonObject { [flagged.Key] = "Banka", [ruleUsed.Key] = 5, [plain.Key] = "ilk" }));
        await PostAsync<QuoteLineDto>(admin, "/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, "Kalem", "Free", null, 1m, 100m));

        // Taslakta ticari alanlar da yazılır.
        var draft = await GetQuoteAsync(admin, quote.Id);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/crm/quotes/{quote.Id}", Save(draft, new JsonObject { [flagged.Key] = "Nakit", [ruleUsed.Key] = 6, [plain.Key] = "ilk" }))).StatusCode);

        var presented = await admin.PostAsJsonAsync($"/api/crm/quotes/{quote.Id}/present", new QuoteTransitionRequest((await GetQuoteAsync(admin, quote.Id)).Version));
        Assert.Equal(HttpStatusCode.OK, presented.StatusCode);

        var frozen = await GetQuoteAsync(admin, quote.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await WriteCustomAsync(admin, quote.Id, frozen, new JsonObject { [flagged.Key] = "Çek", [ruleUsed.Key] = 6, [plain.Key] = "ilk" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await WriteCustomAsync(admin, quote.Id, frozen, new JsonObject { [flagged.Key] = "Nakit", [ruleUsed.Key] = 30, [plain.Key] = "ilk" })).StatusCode);

        // İşaretsiz ve kuralda kullanılmayan alan sunulmuş teklifte de yazılır; kilitli alanın değeri değişmedi.
        var allowed = await WriteCustomAsync(admin, quote.Id, frozen, new JsonObject { [flagged.Key] = "Nakit", [ruleUsed.Key] = 6, [plain.Key] = "güncel" });
        Assert.True(allowed.StatusCode == HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        var after = await GetQuoteAsync(admin, quote.Id);
        Assert.Equal("güncel", after.Custom![plain.Key]!.GetValue<string>());
        Assert.Equal("Nakit", after.Custom![flagged.Key]!.GetValue<string>());
    }

    [Fact]
    public async Task Unflagging_the_field_releases_the_lock_and_an_inactive_rule_does_not_lock()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var field = await CreateFieldAsync(admin, new SaveCustomFieldRequest(QuoteKey, "Teminat türü", CustomFieldType.Text, IsCommercial: true));
        var ruleField = await CreateFieldAsync(admin, new SaveCustomFieldRequest(QuoteKey, "Süre gün", CustomFieldType.Number));
        await PostAsync<SurchargeRuleDto>(admin, "/api/crm/surcharge-rules", new SaveSurchargeRuleRequest("Pasif kural", "Duration", "EUR", Percent: 10m,
            BaseKey: "quote.subtotal", DurationKey: ruleField.Key, IsActive: false, TaxRatePercent: 20m));
        var quote = await PostAsync<QuoteDto>(admin, "/api/crm/quotes", new SaveQuoteRequest("Teklif", s.CompanyId, Currency: "EUR",
            ValidUntil: DateTimeOffset.UtcNow.AddDays(30), Custom: new JsonObject { [field.Key] = "Banka", [ruleField.Key] = 5 }));
        await PostAsync<QuoteLineDto>(admin, "/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, "Kalem", "Free", null, 1m, 100m));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/crm/quotes/{quote.Id}/present", new QuoteTransitionRequest((await GetQuoteAsync(admin, quote.Id)).Version))).StatusCode);
        var frozen = await GetQuoteAsync(admin, quote.Id);

        // Pasif kuralın alanı kilitlemez; işaretli alan kilitlidir, işaret kalkınca açılır.
        Assert.Equal(HttpStatusCode.OK, (await WriteCustomAsync(admin, quote.Id, frozen, new JsonObject { [field.Key] = "Banka", [ruleField.Key] = 9 })).StatusCode);
        var locked = await WriteCustomAsync(admin, quote.Id, await GetQuoteAsync(admin, quote.Id), new JsonObject { [field.Key] = "Çek", [ruleField.Key] = 9 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, locked.StatusCode);

        var unflag = new SaveCustomFieldRequest(QuoteKey, "Teminat türü", CustomFieldType.Text, IsCommercial: false);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/customization/fields/{field.Id}", unflag)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WriteCustomAsync(admin, quote.Id, await GetQuoteAsync(admin, quote.Id), new JsonObject { [field.Key] = "Çek", [ruleField.Key] = 9 })).StatusCode);
    }

    [Fact]
    public async Task The_flag_is_tenant_scoped_and_needs_the_field_management_permission()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        using var repA = await LoginAsync(a.Rep);

        // Aynı anahtarlı alan B firmasında işaretsizdir: A'nın bayrağı B'nin teklifini kilitlemez.
        await CreateFieldAsync(adminA, new SaveCustomFieldRequest(QuoteKey, "Teminat türü", CustomFieldType.Text, IsCommercial: true));
        var fieldB = await CreateFieldAsync(adminB, new SaveCustomFieldRequest(QuoteKey, "Teminat türü", CustomFieldType.Text));
        Assert.False(fieldB.IsCommercial);
        var quoteB = await PostAsync<QuoteDto>(adminB, "/api/crm/quotes", new SaveQuoteRequest("Teklif", b.CompanyId, Currency: "EUR",
            ValidUntil: DateTimeOffset.UtcNow.AddDays(30), Custom: new JsonObject { [fieldB.Key] = "Banka" }));
        await PostAsync<QuoteLineDto>(adminB, "/api/crm/quote-lines", new SaveQuoteLineRequest(quoteB.Id, "Kalem", "Free", null, 1m, 100m));
        Assert.Equal(HttpStatusCode.OK, (await adminB.PostAsJsonAsync($"/api/crm/quotes/{quoteB.Id}/present", new QuoteTransitionRequest((await GetQuoteAsync(adminB, quoteB.Id)).Version))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WriteCustomAsync(adminB, quoteB.Id, await GetQuoteAsync(adminB, quoteB.Id), new JsonObject { [fieldB.Key] = "Çek" })).StatusCode);

        // Yetki: teklif yazma izni olan alan tanımlayamaz ve bayrağı değiştiremez.
        var denied = await repA.PostAsJsonAsync("/api/customization/fields", new SaveCustomFieldRequest(QuoteKey, "Başka alan", CustomFieldType.Text, IsCommercial: true));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private static SaveQuoteRequest Save(QuoteDto q, JsonObject custom) => new(
        q.Title, q.CompanyId, q.Description, q.ContactId, q.OpportunityId, q.PriceListId, q.OwnerId, q.IssueDate, q.ValidUntil, q.Currency, q.ReportingCurrency,
        q.ExchangeRate, q.ExchangeRateDate, q.PriceIncludesTax, q.DocumentDiscountKind, q.DocumentDiscountValue, q.PaymentTerms, q.DeliveryTerms, q.CommercialTerms,
        q.InternalNotes, q.Version, custom);

    // Sunulmuş teklifte tipli PUT kaydın tamamını yazdığı için zaten reddedilir (başlık, firma… ticari alanlardır); özel alan yazımı genel kayıt ucundan kısmi yapılır.
    private static async Task<HttpResponseMessage> WriteCustomAsync(HttpClient client, Guid id, QuoteDto current, JsonObject custom) =>
        await client.PutAsJsonAsync($"/api/records/crm.quote/{id}", new RecordWriteRequest(new JsonObject { ["custom"] = custom }, current.Version));

    private static async Task<QuoteDto> GetQuoteAsync(HttpClient client, Guid id) => (await client.GetFromJsonAsync<QuoteDto>($"/api/crm/quotes/{id}"))!;

    private static async Task<CustomFieldDto> CreateFieldAsync(HttpClient client, SaveCustomFieldRequest request) =>
        await PostAsync<CustomFieldDto>(client, "/api/customization/fields", request);

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
