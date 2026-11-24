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
/// Teklif başlığı ve satırı özel alana açıktır, teklif formu metadata'dan gelir (ADR-0021, ADR-0019): özel alan ve düzen çalışır,
/// zorunluluk ve tür sunucuda denetlenir, çoğaltmada değerler taşınır, başka firmanın alanı görünmez, yetkisiz kullanıcı yazamaz.
/// </summary>
public class QuoteCustomFieldTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string QuoteKey = "crm.quote";
    private const string LineKey = "crm.quote-line";

    private static readonly string[] RepPermissions = [QuotesRead, QuotesWrite, CompaniesRead, ProductsRead, PricesRead];
    private static readonly string[] ReaderPermissions = [QuotesRead, CompaniesRead];

    [Fact]
    public async Task Quote_and_line_accept_custom_fields_and_the_default_layout_places_them()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);

        var entities = (await admin.GetFromJsonAsync<List<CustomizableEntityDto>>("/api/customization/entities"))!;
        Assert.Contains(entities, e => e.Key == QuoteKey);
        Assert.Contains(entities, e => e.Key == LineKey);

        var cargo = await CreateFieldAsync(admin, new SaveCustomFieldRequest(QuoteKey, "Yük özelliği", CustomFieldType.Choice,
            Options: [new(null, "ADR"), new(null, "Standart")]));
        var pallets = await CreateFieldAsync(admin, new SaveCustomFieldRequest(LineKey, "Palet adedi", CustomFieldType.Number));

        // Varsayılan düzen sekmelidir ve özel alan "Ek bilgiler" bölümüne yerleşir.
        var layout = (await admin.GetFromJsonAsync<List<FormLayoutDto>>("/api/form-layouts"))!.Single(l => l.EntityKey == QuoteKey);
        Assert.Equal(["genel", "fiyat", "kosul", "ek"], layout.Tabs.Select(t => t.Key));
        Assert.Contains(layout.Fields, f => f is { Key: "title", Fixed: true, Required: true });
        Assert.Contains(layout.Fields, f => f.Key == "custom." + cargo.Key && f.IsCustom && f.Section == "ek");

        var quote = await CreateQuoteAsync(admin, s.CompanyId, new JsonObject { [cargo.Key] = cargo.Options.First(o => o.Label == "ADR").Key });
        Assert.Equal(cargo.Options.First(o => o.Label == "ADR").Key, (await GetQuoteAsync(admin, quote.Id)).Custom![cargo.Key]!.GetValue<string>());

        var line = await PostAsync<QuoteLineDto>(admin, "/api/crm/quote-lines",
            new SaveQuoteLineRequest(quote.Id, "Parsiyel ardiye", "Free", null, 1m, 150m, Custom: new JsonObject { [pallets.Key] = 6 }));
        var lines = (await admin.GetFromJsonAsync<CrmPage<QuoteLineDto>>($"/api/crm/quote-lines?quoteId={quote.Id}"))!.Items;
        Assert.Equal(6, lines.Single(l => l.Id == line.Id).Custom![pallets.Key]!.GetValue<int>());
    }

    [Fact]
    public async Task Required_and_mistyped_custom_values_are_rejected_by_the_server()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var cargo = await CreateFieldAsync(admin, new SaveCustomFieldRequest(QuoteKey, "Yük özelliği", CustomFieldType.Choice, Required: true,
            Options: [new(null, "ADR"), new(null, "Standart")]));

        var missing = await admin.PostAsJsonAsync("/api/crm/quotes", new SaveQuoteRequest("Teklif", s.CompanyId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Contains("custom." + cargo.Key, await missing.Content.ReadAsStringAsync());

        var wrong = await admin.PostAsJsonAsync("/api/crm/quotes", new SaveQuoteRequest("Teklif", s.CompanyId, Custom: new JsonObject { [cargo.Key] = "gemi" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
    }

    [Fact]
    public async Task Duplicating_a_quote_carries_custom_values_of_the_header_and_the_lines()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var note = await CreateFieldAsync(admin, new SaveCustomFieldRequest(QuoteKey, "Operasyon notu", CustomFieldType.Text));
        var lineNote = await CreateFieldAsync(admin, new SaveCustomFieldRequest(LineKey, "Kalem notu", CustomFieldType.Text));

        var quote = await CreateQuoteAsync(admin, s.CompanyId, new JsonObject { [note.Key] = "Soğuk zincir" });
        await PostAsync<QuoteLineDto>(admin, "/api/crm/quote-lines",
            new SaveQuoteLineRequest(quote.Id, "Kalem", "Free", null, 1m, 100m, Custom: new JsonObject { [lineNote.Key] = "Rampa 3" }));

        var copyResponse = await admin.PostAsync($"/api/crm/quotes/{quote.Id}/duplicate", null);
        Assert.Equal(HttpStatusCode.Created, copyResponse.StatusCode);
        var copy = (await copyResponse.Content.ReadFromJsonAsync<QuoteDto>())!;
        Assert.Equal("Soğuk zincir", copy.Custom![note.Key]!.GetValue<string>());
        var copiedLine = (await admin.GetFromJsonAsync<CrmPage<QuoteLineDto>>($"/api/crm/quote-lines?quoteId={copy.Id}"))!.Items.Single();
        Assert.Equal("Rampa 3", copiedLine.Custom![lineNote.Key]!.GetValue<string>());
    }

    [Fact]
    public async Task Custom_fields_of_one_tenant_are_invisible_to_another_and_a_reader_cannot_write_them()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        using var readerA = await LoginAsync(a.Reader);
        using var repA = await LoginAsync(a.Rep);

        var field = await CreateFieldAsync(adminA, new SaveCustomFieldRequest(QuoteKey, "Yük özelliği", CustomFieldType.Text));

        // Tenant izolasyonu: B firması A'nın alanını ne alan listesinde ne düzende görür.
        Assert.DoesNotContain((await adminB.GetFromJsonAsync<List<CustomFieldDto>>("/api/custom-fields"))!, f => f.Key == field.Key && f.EntityKey == QuoteKey);
        var layoutB = (await adminB.GetFromJsonAsync<List<FormLayoutDto>>("/api/form-layouts"))!.Single(l => l.EntityKey == QuoteKey);
        Assert.DoesNotContain(layoutB.Fields, f => f.Key == "custom." + field.Key);

        // Yetki: teklif yazma izni olan değeri yazar; yalnız okuma izni olan yazamaz ve alan tanımlayamaz.
        var quote = await CreateQuoteAsync(repA, a.CompanyId, new JsonObject { [field.Key] = "ADR" });
        var update = new SaveQuoteRequest("Teklif", a.CompanyId, Version: quote.Version, Custom: new JsonObject { [field.Key] = "Standart" });
        Assert.Equal(HttpStatusCode.Forbidden, (await readerA.PutAsJsonAsync($"/api/crm/quotes/{quote.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readerA.PostAsJsonAsync("/api/customization/fields",
            new SaveCustomFieldRequest(QuoteKey, "Başka alan", CustomFieldType.Text))).StatusCode);
        Assert.Equal("ADR", (await GetQuoteAsync(adminA, quote.Id)).Custom![field.Key]!.GetValue<string>());
    }

    private static async Task<QuoteDto> CreateQuoteAsync(HttpClient client, Guid companyId, JsonObject? custom) =>
        await PostAsync<QuoteDto>(client, "/api/crm/quotes", new SaveQuoteRequest("Teklif", companyId, ValidUntil: DateTimeOffset.UtcNow.AddDays(30), Custom: custom));

    private static async Task<QuoteDto> GetQuoteAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<QuoteDto>($"/api/crm/quotes/{id}"))!;

    private static async Task<CustomFieldDto> CreateFieldAsync(HttpClient client, SaveCustomFieldRequest request) =>
        await PostAsync<CustomFieldDto>(client, "/api/customization/fields", request);

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<(ModulixUser Admin, ModulixUser Rep, ModulixUser Reader, Guid CompanyId)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        var rep = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, RepPermissions, Scope.All, rep);
        await factory.CreateRoleAsync(tenant.Id, ReaderPermissions, Scope.All, reader);
        using var client = await LoginAsync(admin);
        var company = await PostAsync<CompanyDto>(client, "/api/crm/companies", new SaveCompanyRequest("Anka Ltd."));
        return (admin, rep, reader, company.Id);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
