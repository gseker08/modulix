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
/// Sabit tutarlı kademe (ADR-0018 devamı, TierMethod=Flat): palet bandına göre tek tutar, miktarla çarpılmaz. Bant dışı miktar için fiyat
/// uydurulmaz, yayımlama kuralları, tenant izolasyonu ve fiyat izni.
/// </summary>
public class PricingFlatTierTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly string[] CatalogOnly = [ProductsRead];

    [Fact]
    public async Task A_flat_band_charges_one_amount_for_the_whole_quantity_and_prices_the_quote_line()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var product = await NewProductAsync(admin);
        var list = await NewFlatListAsync(admin, product.Id);

        // 1–2 palet 100 €, 5–7 palet 150 €: tutar miktarla çarpılmaz.
        Assert.Equal(100m, (await CalculateAsync(admin, product.Id, 1m)).ListTotal);
        Assert.Equal(100m, (await CalculateAsync(admin, product.Id, 2m)).ListTotal);
        var six = await CalculateAsync(admin, product.Id, 6m);
        Assert.Equal(150m, six.ListTotal);
        Assert.Equal(list.Id, six.PriceListId);

        // Teklif satırı: birim fiyat = bant tutarı / miktar, satır toplamı bant tutarıdır.
        var quote = await PostAsync<QuoteDto>(admin, "/api/crm/quotes",
            new SaveQuoteRequest("Ardiye teklifi", s.CompanyId, PriceListId: list.Id, Currency: "EUR"));
        var line = await PostAsync<QuoteLineDto>(admin, "/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, null, "Product", product.Id, 6m));
        Assert.Equal(25m, line.UnitPrice);
        Assert.Equal(150m, line.Net);
    }

    [Fact]
    public async Task Quantities_outside_every_band_get_no_price_and_a_gap_is_allowed_only_for_flat_lists()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var product = await NewProductAsync(admin);
        await NewFlatListAsync(admin, product.Id);

        // 8 palet bantların dışında (son bant 5–7): fiyat uydurulmaz.
        var response = await admin.PostAsJsonAsync("/api/crm/pricing/calculate", new PriceCalculationRequestDto(product.Id, 8m, "EUR"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("price-not-found", (await response.Content.ReadFromJsonAsync<PriceCalculationErrorDto>())!.Code);

        // Bantlar arasında boşluk Flat listede yayımlanabilir; Kademeli (Graduated) listede miktar karşılanamayacağı için reddedilir.
        foreach (var (method, expected) in new[] { ("Flat", HttpStatusCode.OK), ("Graduated", HttpStatusCode.UnprocessableEntity) })
        {
            var gapped = await PostAsync<PriceListDto>(admin, "/api/crm/price-lists", new SavePriceListRequest($"Boşluklu {method}", "EUR", TierMethod: method));
            await AddTierAsync(admin, gapped.Id, product.Id, 1m, 100m, 3m);
            await AddTierAsync(admin, gapped.Id, product.Id, 5m, 150m, 8m);
            var publish = await admin.PutAsJsonAsync($"/api/crm/price-lists/{gapped.Id}", Save(gapped) with { Status = "Published" });
            Assert.Equal(expected, publish.StatusCode);
        }

        // Çakışan bant yine reddedilir.
        var overlap = await PostAsync<PriceListDto>(admin, "/api/crm/price-lists", new SavePriceListRequest("Çakışan", "EUR", TierMethod: "Flat"));
        await AddTierAsync(admin, overlap.Id, product.Id, 1m, 100m, 4m);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsJsonAsync("/api/crm/price-list-items", new SavePriceListItemRequest(overlap.Id, product.Id, 3m, 125m, 8m))).StatusCode);
    }

    [Fact]
    public async Task Flat_lists_need_the_prices_permission_and_never_cross_tenants()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        using var catalogOnly = await LoginAsync(a.CatalogOnly);
        var product = await NewProductAsync(adminA);
        var list = await NewFlatListAsync(adminA, product.Id);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await catalogOnly.PostAsJsonAsync("/api/crm/price-lists", new SavePriceListRequest("İzinsiz", "EUR", TierMethod: "Flat"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await catalogOnly.PostAsJsonAsync("/api/crm/pricing/calculate", new PriceCalculationRequestDto(product.Id, 2m, "EUR"))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync($"/api/crm/price-lists/{list.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await adminB.PostAsJsonAsync("/api/crm/pricing/calculate", new PriceCalculationRequestDto(product.Id, 2m, "EUR"))).StatusCode);
    }

    private static async Task<ProductDto> NewProductAsync(HttpClient client)
    {
        var unit = await PostAsync<MeasureUnitDto>(client, "/api/crm/units", new SaveMeasureUnitRequest("PLT", "Palet", "count"));
        return await PostAsync<ProductDto>(client, "/api/crm/products", new SaveProductRequest("ARD-PARSIYEL", "Parsiyel ardiye", unit.Id) with { Status = "Active" });
    }

    // Parsiyel ardiye bantları (EUR, 1–7 gün): 1–2 palet 100, 3–4 palet 125, 5–7 palet 150; üst sınır hariç aralıklar [1,3) [3,5) [5,8).
    private static async Task<PriceListDto> NewFlatListAsync(HttpClient client, Guid productId)
    {
        var list = await PostAsync<PriceListDto>(client, "/api/crm/price-lists", new SavePriceListRequest("Ardiye 2026", "EUR", TierMethod: "Flat"));
        await AddTierAsync(client, list.Id, productId, 1m, 100m, 3m);
        await AddTierAsync(client, list.Id, productId, 3m, 125m, 5m);
        await AddTierAsync(client, list.Id, productId, 5m, 150m, 8m);
        var response = await client.PutAsJsonAsync($"/api/crm/price-lists/{list.Id}", Save(list) with { Status = "Published" });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PriceListDto>())!;
    }

    private static SavePriceListRequest Save(PriceListDto l) => new(
        l.Name, l.Currency, l.Scope, l.CompanyId, l.Segment, l.ValidFrom, l.ValidTo, l.Status, l.TierMethod, l.Version);

    private static Task<PriceListItemDto> AddTierAsync(HttpClient client, Guid listId, Guid productId, decimal min, decimal amount, decimal max) =>
        PostAsync<PriceListItemDto>(client, "/api/crm/price-list-items", new SavePriceListItemRequest(listId, productId, min, amount, max));

    private static async Task<PriceCalculationDto> CalculateAsync(HttpClient client, Guid productId, decimal quantity)
    {
        var response = await client.PostAsJsonAsync("/api/crm/pricing/calculate", new PriceCalculationRequestDto(productId, quantity, "EUR"));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PriceCalculationDto>())!;
    }

    private async Task<(ModulixUser Admin, ModulixUser CatalogOnly, Guid CompanyId)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        var catalogOnly = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, CatalogOnly, Scope.All, catalogOnly);
        using var client = await LoginAsync(admin);
        var company = await PostAsync<CompanyDto>(client, "/api/crm/companies", new SaveCompanyRequest("Anka Ltd."));
        return (admin, catalogOnly, company.Id);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
