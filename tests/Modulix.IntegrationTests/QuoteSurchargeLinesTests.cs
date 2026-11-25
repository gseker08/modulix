using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
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
/// Ek ücret kurallarının teklif satırı olarak üretimi (ADR-0018 devamı, Dilim 2c ikinci aşama): sonuçlar satır ve toplam olur, ek ücret genel indirim
/// almaz, üretilen satır elle değiştirilemez, kural değişince bir sonraki hesapta satır kurulur ya da kalkar, sunulmuş teklif sabit kalır,
/// çoğaltma kendi kurallarıyla yeniden üretir; tenant izolasyonu ve izin.
/// </summary>
public class QuoteSurchargeLinesTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Rules = "/api/crm/surcharge-rules";
    private static readonly string[] RepPermissions = [QuotesRead, QuotesWrite, CompaniesRead]; // fiyat izni yok: üretim sunucu kararıdır

    [Fact]
    public async Task Rule_results_become_lines_that_enter_the_total_but_do_not_take_the_document_discount()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var cargo = await CreateFieldAsync(admin, new SaveCustomFieldRequest("crm.quote", "Yük özelliği", CustomFieldType.Choice, Options: [new(null, "ADR"), new(null, "Standart")]));
        var days = await CreateFieldAsync(admin, new SaveCustomFieldRequest("crm.quote", "Süre gün", CustomFieldType.Number));
        var declarations = await CreateFieldAsync(admin, new SaveCustomFieldRequest("crm.quote", "Beyanname adedi", CustomFieldType.Number));
        var adr = cargo.Options.Single(o => o.Label == "ADR").Key;

        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("ADR ek ücreti", "Fixed", "EUR", Priority: 20, Amount: 100m, Group: "ardiye-bedeli", Presentation: "IncludedInGroup",
            QuantityKeys: new JsonArray(declarations.Key), Conditions: new JsonArray(Condition(cargo.Key, "Equals", adr))));
        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Demuraj", "Duration", "EUR", Priority: 40, Percent: 10m, BaseKey: "quote.subtotal", DurationKey: days.Key,
            FreeUnits: 7m, Group: "ardiye-bedeli", Presentation: "BreakdownOnly", QuantityKeys: new JsonArray(declarations.Key)));
        await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Asgari ardiye", "Minimum", "EUR", Priority: 90, Amount: 125m, Group: "ardiye-bedeli"));

        var quote = await NewQuoteAsync(admin, s.CompanyId, new JsonObject { [cargo.Key] = adr, [days.Key] = 10, [declarations.Key] = 1 });

        // Kalem yokken ek ücret üretilmez.
        Assert.Empty(await LinesAsync(admin, quote.Id));
        Assert.Equal(0m, (await GetQuoteAsync(admin, quote.Id)).NetTotal);

        await AddLineAsync(admin, quote.Id, "Parsiyel ardiye", 150m, "Ardiye Bedeli");

        var lines = await LinesAsync(admin, quote.Id);
        var generated = lines.Where(l => l.SourceRuleId is not null).ToList();
        Assert.Equal(["ADR ek ücreti", "Demuraj"], generated.Select(l => l.Name));
        Assert.Equal([100m, 45m], generated.Select(l => l.Net));
        Assert.Equal(["IncludedInGroup", "BreakdownOnly"], generated.Select(l => l.SurchargePresentation));
        Assert.All(generated, l => Assert.True(l.Position > lines.Single(m => m.SourceRuleId is null).Position));
        var totals = await GetQuoteAsync(admin, quote.Id);
        Assert.Equal((295m, 59m, 354m), (totals.NetTotal, totals.TaxTotal, totals.GrossTotal)); // (150 + 100 + 45) × %20 vergi

        // %10 genel indirim yalnızca elle kaleme uygulanır (15 €); ek ücret satırları indirim almaz.
        var discounted = await admin.PutAsJsonAsync($"/api/crm/quotes/{quote.Id}", Save(totals) with { DocumentDiscountKind = "Percent", DocumentDiscountValue = 10m });
        Assert.Equal(HttpStatusCode.OK, discounted.StatusCode);
        var after = (await discounted.Content.ReadFromJsonAsync<QuoteDto>())!;
        Assert.Equal(280m, after.NetTotal); // 135 + 100 + 45
        var afterLines = await LinesAsync(admin, quote.Id);
        Assert.Equal(15m, afterLines.Single(l => l.SourceRuleId is null).DocumentDiscountShare);
        Assert.All(afterLines.Where(l => l.SourceRuleId is not null), l => Assert.Equal(0m, l.DocumentDiscountShare));
    }

    [Fact]
    public async Task Generated_lines_are_locked_and_come_and_go_with_the_rule_on_the_next_calculation()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var rule = await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Sabit ek ücret", "Fixed", "EUR", Amount: 100m));
        var quote = await NewQuoteAsync(admin, s.CompanyId, null);
        var manual = await AddLineAsync(admin, quote.Id, "Kalem", 100m, null);

        var generated = (await LinesAsync(admin, quote.Id)).Single(l => l.SourceRuleId == rule.Id);
        Assert.Equal(100m, generated.Net);

        // Elle değiştirme ve silme reddedilir; kural değiştirilir ya da kapatılır.
        var edit = await admin.PutAsJsonAsync($"/api/crm/quote-lines/{generated.Id}", Save(generated) with { UnitPrice = 1m });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, edit.StatusCode);
        Assert.Contains("name", (await edit.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/crm/quote-lines/{generated.Id}?version={generated.Version}")).StatusCode);

        // Kural kapanır; teklif bir sonraki hesapta satırı bırakır.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Rules}/{rule.Id}", SaveRule(rule) with { IsActive = false })).StatusCode);
        var current = (await LinesAsync(admin, quote.Id)).Single(l => l.SourceRuleId is null);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/crm/quote-lines/{manual.Id}", Save(current) with { Quantity = 2m })).StatusCode);
        Assert.DoesNotContain(await LinesAsync(admin, quote.Id), l => l.SourceRuleId is not null);
        Assert.Equal(200m, (await GetQuoteAsync(admin, quote.Id)).NetTotal);

        // Kural yeniden açılır: aynı kararlı anahtarla satır geri gelir.
        var disabled = (await admin.GetFromJsonAsync<SurchargeRuleDto>($"{Rules}/{rule.Id}"))!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Rules}/{rule.Id}", SaveRule(disabled) with { IsActive = true })).StatusCode);
        await AddLineAsync(admin, quote.Id, "İkinci kalem", 50m, null);
        var again = (await LinesAsync(admin, quote.Id)).Single(l => l.SourceRuleId == rule.Id);
        Assert.Equal(generated.StableKey, again.StableKey);
        Assert.Equal(350m, (await GetQuoteAsync(admin, quote.Id)).NetTotal); // 200 + 50 + 100
    }

    [Fact]
    public async Task Presented_quotes_stay_frozen_and_a_duplicate_regenerates_with_the_current_rules()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var rule = await PostRuleAsync(admin, new SaveSurchargeRuleRequest("Sabit ek ücret", "Fixed", "EUR", Amount: 100m));
        var quote = await NewQuoteAsync(admin, s.CompanyId, null);
        await AddLineAsync(admin, quote.Id, "Kalem", 100m, null);
        var presentedResponse = await admin.PostAsJsonAsync($"/api/crm/quotes/{quote.Id}/present", new QuoteTransitionRequest((await GetQuoteAsync(admin, quote.Id)).Version));
        Assert.Equal(HttpStatusCode.OK, presentedResponse.StatusCode);
        var frozen = (await LinesAsync(admin, quote.Id)).Where(l => l.SourceRuleId is not null).ToList();
        var frozenTotal = (await GetQuoteAsync(admin, quote.Id)).NetTotal;

        // Kural sonradan değişir: sunulmuş teklif etkilenmez.
        var current = (await admin.GetFromJsonAsync<SurchargeRuleDto>($"{Rules}/{rule.Id}"))!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Rules}/{rule.Id}", SaveRule(current) with { Amount = 500m })).StatusCode);
        var same = (await LinesAsync(admin, quote.Id)).Where(l => l.SourceRuleId is not null).ToList();
        Assert.Equal(frozen.Select(l => (l.Id, l.Net)), same.Select(l => (l.Id, l.Net)));
        Assert.Equal(frozenTotal, (await GetQuoteAsync(admin, quote.Id)).NetTotal);

        // Çoğaltma yeni taslaktır: kuraldan üretilmiş satır kopyalanmaz, güncel kuralla yeniden kurulur (yeni aile, yeni kararlı anahtar).
        var copyResponse = await admin.PostAsync($"/api/crm/quotes/{quote.Id}/duplicate", null);
        Assert.Equal(HttpStatusCode.Created, copyResponse.StatusCode);
        var copy = (await copyResponse.Content.ReadFromJsonAsync<QuoteDto>())!;
        var copyLines = await LinesAsync(admin, copy.Id);
        Assert.Equal(2, copyLines.Count);
        var regenerated = copyLines.Single(l => l.SourceRuleId == rule.Id);
        Assert.Equal(500m, regenerated.Net);
        Assert.NotEqual(frozen.Single().StableKey, regenerated.StableKey);
        Assert.Equal(600m, (await GetQuoteAsync(admin, copy.Id)).NetTotal);
    }

    [Fact]
    public async Task Generation_needs_no_price_permission_and_never_crosses_tenants()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        using var rep = await LoginAsync(a.Rep);
        var rule = await PostRuleAsync(adminA, new SaveSurchargeRuleRequest("Sabit ek ücret", "Fixed", "EUR", Amount: 100m));

        // Fiyat izni olmayan temsilci kalem ekler; ek ücret sunucu kararıdır, satırı görür ama değiştiremez.
        var quote = await NewQuoteAsync(rep, a.CompanyId, null);
        await PostAsync<QuoteLineDto>(rep, "/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, "Kalem", "Free", null, 1m, 100m));
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.GetAsync($"{Rules}/{rule.Id}")).StatusCode); // kuralı yönetemez, okuyamaz
        var generated = (await LinesAsync(rep, quote.Id)).Single(l => l.SourceRuleId == rule.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await rep.PutAsJsonAsync($"/api/crm/quote-lines/{generated.Id}", Save(generated) with { UnitPrice = 1m })).StatusCode);

        // Tenant izolasyonu: B firmasının teklifine A'nın kuralı karışmaz.
        var own = await NewQuoteAsync(adminB, b.CompanyId, null);
        await AddLineAsync(adminB, own.Id, "Kalem", 100m, null);
        Assert.DoesNotContain(await LinesAsync(adminB, own.Id), l => l.SourceRuleId is not null);
        Assert.Equal(100m, (await GetQuoteAsync(adminB, own.Id)).NetTotal);
    }

    private static JsonObject Condition(string key, string op, string value) => new() { ["key"] = key, ["operator"] = op, ["value"] = value };

    private static SaveQuoteRequest Save(QuoteDto q) => new(
        q.Title, q.CompanyId, q.Description, q.ContactId, q.OpportunityId, q.PriceListId, q.OwnerId, q.IssueDate, q.ValidUntil, q.Currency, q.ReportingCurrency,
        q.ExchangeRate, q.ExchangeRateDate, q.PriceIncludesTax, q.DocumentDiscountKind, q.DocumentDiscountValue, q.PaymentTerms, q.DeliveryTerms, q.CommercialTerms,
        q.InternalNotes, q.Version, q.Custom);

    private static SaveQuoteLineRequest Save(QuoteLineDto l) => new(
        l.QuoteId, l.Name, l.Kind, l.ProductId, l.Quantity, l.UnitPrice, l.UnitId, l.DiscountKind, l.DiscountValue, l.TaxRatePercent, l.TaxCode, l.Role, l.IsSelected,
        l.ParentStableKey, l.RecurringPeriod, l.Description, l.GroupName, l.Position, l.UnitCost, l.Version, l.Custom);

    private static SaveSurchargeRuleRequest SaveRule(SurchargeRuleDto r) => new(
        r.Name, r.Kind, r.Currency, r.Description, r.Priority, r.IsActive, r.Amount, r.Percent, r.Group, r.BaseKey, r.DurationKey, r.FreeUnits,
        r.QuantityKeys, r.Conditions, r.Presentation, r.Stackable, r.TaxRatePercent, r.Version);

    private static async Task<List<QuoteLineDto>> LinesAsync(HttpClient client, Guid quoteId) =>
        [.. (await client.GetFromJsonAsync<CrmPage<QuoteLineDto>>($"/api/crm/quote-lines?quoteId={quoteId}"))!.Items];

    private static async Task<QuoteDto> GetQuoteAsync(HttpClient client, Guid id) => (await client.GetFromJsonAsync<QuoteDto>($"/api/crm/quotes/{id}"))!;

    private static async Task<QuoteDto> NewQuoteAsync(HttpClient client, Guid companyId, JsonObject? custom) =>
        await PostAsync<QuoteDto>(client, "/api/crm/quotes", new SaveQuoteRequest("Ardiye teklifi", companyId, Currency: "EUR", ValidUntil: DateTimeOffset.UtcNow.AddDays(30), Custom: custom));

    private static async Task<QuoteLineDto> AddLineAsync(HttpClient client, Guid quoteId, string name, decimal price, string? group) =>
        await PostAsync<QuoteLineDto>(client, "/api/crm/quote-lines", new SaveQuoteLineRequest(quoteId, name, "Free", null, 1m, price, GroupName: group));

    private static async Task<SurchargeRuleDto> PostRuleAsync(HttpClient client, SaveSurchargeRuleRequest body) =>
        await PostAsync<SurchargeRuleDto>(client, Rules, body with { TaxRatePercent = body.TaxRatePercent ?? 20m });

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
