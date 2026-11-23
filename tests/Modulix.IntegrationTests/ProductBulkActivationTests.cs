using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Authorization;
using Modulix.Infrastructure.Identity;
using Modulix.Modules.Crm;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Lookups;
using Modulix.Web.Services;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Ürünler listesindeki toplu aktif yapma her ürünü mevcut PUT ucundan geçirir (<see cref="ProductBulkActivation"/>): mutlu yol, yaşam döngüsü ve
/// sürüm doğrulaması, tenant izolasyonu, yetki; ayrıca teklif kalemi ürün seçicisinin yalnızca Aktif ürünleri getirmesi.
/// </summary>
public class ProductBulkActivationTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    [Fact]
    public async Task Draft_products_become_active_with_their_other_fields_unchanged_and_show_up_in_the_active_filter()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var unit = await CreateUnitAsync(admin, "ADT");
        var first = await CreateAsync(admin, "B-1", unit.Id, new SaveProductRequest("B-1", "Ürün B-1", unit.Id, Brand: "Marka", Tags: "etiket", TaxCategory: "KDV20"));
        var second = await CreateAsync(admin, "B-2", unit.Id);
        var third = await CreateAsync(admin, "B-3", unit.Id);

        foreach (var product in new[] { first, second, third })
        {
            Assert.True(ProductBulkActivation.NeedsActivation(product));
            var activated = await ActivateAsync(admin, product);
            Assert.Equal("Active", activated.Status);
            Assert.Equal((product.Code, product.Name, product.Brand, product.Tags, product.TaxCategory), (activated.Code, activated.Name, activated.Brand, activated.Tags, activated.TaxCategory));
        }

        var active = (await admin.GetFromJsonAsync<CrmPage<ProductDto>>("/api/crm/products?status=Active"))!;
        Assert.Equal(["B-1", "B-2", "B-3"], active.Items.Select(p => p.Code).Order().ToArray());
        Assert.Empty((await admin.GetFromJsonAsync<CrmPage<ProductDto>>("/api/crm/products?status=Draft"))!.Items);
    }

    [Fact]
    public async Task Withdrawn_or_stale_products_fail_individually_and_active_ones_are_skipped_without_blocking_the_others()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var unit = await CreateUnitAsync(admin, "ADT");
        var draft = await CreateAsync(admin, "V-1", unit.Id);
        var alreadyActive = await ActivateAsync(admin, await CreateAsync(admin, "V-2", unit.Id));
        var withdrawn = await PutAsync(admin, await ActivateAsync(admin, await CreateAsync(admin, "V-3", unit.Id)), "Withdrawn");
        var stale = await CreateAsync(admin, "V-4", unit.Id);
        await PutAsync(admin, stale, "Draft", name: "Başkası değiştirdi");

        // Aktif ürün listede atlanır (gereksiz sürüm artışı ve denetim kaydı olmasın); atlanan ürün değişmeden kalır.
        Assert.False(ProductBulkActivation.NeedsActivation(alreadyActive));
        // Satıştan kaldırılan geri açılamaz; eski sürümle yazım 409 döner.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(admin, withdrawn, ProductBulkActivation.ActivationRequest(withdrawn))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Put(admin, stale, ProductBulkActivation.ActivationRequest(stale))).StatusCode);

        // Hatalı kayıtlar diğerlerini engellemez ve bozmaz.
        Assert.Equal("Active", (await ActivateAsync(admin, draft)).Status);
        Assert.Equal(alreadyActive.Version, (await admin.GetFromJsonAsync<ProductDto>($"/api/crm/products/{alreadyActive.Id}"))!.Version);
        Assert.Equal("Withdrawn", (await admin.GetFromJsonAsync<ProductDto>($"/api/crm/products/{withdrawn.Id}"))!.Status);
        Assert.Equal("Draft", (await admin.GetFromJsonAsync<ProductDto>($"/api/crm/products/{stale.Id}"))!.Status);
    }

    [Fact]
    public async Task A_tenant_cannot_activate_another_tenants_product()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        var product = await CreateAsync(adminA, "T-1", (await CreateUnitAsync(adminA, "ADT")).Id);

        var response = await Put(adminB, product, ProductBulkActivation.ActivationRequest(product));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Draft", (await adminA.GetFromJsonAsync<ProductDto>($"/api/crm/products/{product.Id}"))!.Status);
    }

    [Fact]
    public async Task A_user_without_write_permission_cannot_activate_products()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        using var viewer = await LoginAsync(s.Viewer);
        var product = await CreateAsync(admin, "Y-1", (await CreateUnitAsync(admin, "ADT")).Id);

        var response = await Put(viewer, product, ProductBulkActivation.ActivationRequest(product));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Draft", (await admin.GetFromJsonAsync<ProductDto>($"/api/crm/products/{product.Id}"))!.Status);
    }

    [Fact]
    public async Task The_quote_line_product_picker_lists_only_active_products_of_the_own_tenant()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        var unitA = await CreateUnitAsync(adminA, "ADT");
        var unitB = await CreateUnitAsync(adminB, "ADT");
        await CreateAsync(adminA, "L-DRAFT", unitA.Id);
        await ActivateAsync(adminA, await CreateAsync(adminA, "L-ACTIVE", unitA.Id));
        await ActivateAsync(adminB, await CreateAsync(adminB, "L-OTHER", unitB.Id));

        var search = (await adminA.GetFromJsonAsync<LookupPageDto>($"/api/lookups/{CrmLookupDefinitions.QuoteLineProduct}/search?q={Uri.EscapeDataString("Ürün")}"))!;

        Assert.Equal("Ürün L-ACTIVE", Assert.Single(search.Items).Label);
    }

    private static Task<ProductDto> CreateAsync(HttpClient client, string code, Guid unitId, SaveProductRequest? request = null) =>
        PostAsync<ProductDto>(client, "/api/crm/products", request ?? new SaveProductRequest(code, $"Ürün {code}", unitId));

    private static async Task<ProductDto> ActivateAsync(HttpClient client, ProductDto product)
    {
        var response = await Put(client, product, ProductBulkActivation.ActivationRequest(product));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private static async Task<ProductDto> PutAsync(HttpClient client, ProductDto current, string status, string? name = null)
    {
        var request = ProductBulkActivation.ActivationRequest(current) with { Status = status, Name = name ?? current.Name };
        var response = await Put(client, current, request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, ProductDto product, SaveProductRequest request) =>
        client.PutAsJsonAsync($"/api/crm/products/{product.Id}", request);

    private static Task<MeasureUnitDto> CreateUnitAsync(HttpClient client, string code) =>
        PostAsync<MeasureUnitDto>(client, "/api/crm/units", new SaveMeasureUnitRequest(code, $"Birim {code}", "count"));

    private async Task<(ModulixUser Admin, ModulixUser Viewer)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        var viewer = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, [ProductsRead], Scope.All, viewer);
        return (admin, viewer);
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
