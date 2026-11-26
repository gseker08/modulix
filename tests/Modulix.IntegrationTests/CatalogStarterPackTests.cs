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
/// Sektör profilinin önerdiği katalog kalemleri (lojistikte masraf kalemleri): profili uygulanmış firma kalemleri tek tıkla yükler, tekrar yüklemek
/// kalem çoğaltmaz, profilsiz firma öneri görmez, başka firmanın kalemi etkilenmez, yalnız okuma izni olan yükleyemez.
/// </summary>
public class CatalogStarterPackTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Pack = "/api/crm/products/starter-pack";
    private static readonly string[] ReaderPermissions = [ProductsRead];

    [Fact]
    public async Task Logistics_profile_loads_expense_items_once_and_active()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/customization/profiles/logistics/apply", null)).StatusCode);

        var preview = (await admin.GetFromJsonAsync<CatalogStarterPackDto>(Pack))!;
        Assert.Equal(11, preview.Items.Count);
        Assert.Equal(11, preview.Missing);
        Assert.All(preview.Items, i => Assert.Equal("Expense", i.Kind));

        var first = await admin.PostAsync(Pack, JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(11, (await first.Content.ReadFromJsonAsync<CatalogStarterPackResultDto>())!.Created);
        var products = (await admin.GetFromJsonAsync<CrmPage<ProductDto>>("/api/crm/products?kind=Expense&pageSize=50"))!.Items;
        Assert.Equal(11, products.Count);
        Assert.All(products, p => Assert.Equal("Active", p.Status));
        Assert.Contains(products, p => p.Name == "T1 kapama");

        // Tekrar yüklemek güvenlidir: kalem çoğalmaz.
        var second = await admin.PostAsync(Pack, JsonContent.Create(new { }));
        Assert.Equal(0, (await second.Content.ReadFromJsonAsync<CatalogStarterPackResultDto>())!.Created);
        Assert.Equal(0, (await admin.GetFromJsonAsync<CatalogStarterPackDto>(Pack))!.Missing);
        Assert.Equal(11, (await admin.GetFromJsonAsync<CrmPage<ProductDto>>("/api/crm/products?kind=Expense&pageSize=50"))!.Items.Count);
    }

    [Fact]
    public async Task A_tenant_without_a_suggesting_profile_sees_nothing_and_other_tenants_are_untouched()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        Assert.Equal(HttpStatusCode.OK, (await adminA.PostAsync("/api/customization/profiles/logistics/apply", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await adminA.PostAsync(Pack, JsonContent.Create(new { }))).StatusCode);

        // B firmasının profili yok: öneri boş, yükleme hiçbir şey eklemez, A'nın kalemleri B'de görünmez.
        var previewB = (await adminB.GetFromJsonAsync<CatalogStarterPackDto>(Pack))!;
        Assert.Empty(previewB.Items);
        var applyB = await adminB.PostAsync(Pack, JsonContent.Create(new { }));
        Assert.Equal(0, (await applyB.Content.ReadFromJsonAsync<CatalogStarterPackResultDto>())!.Created);
        Assert.Empty((await adminB.GetFromJsonAsync<CrmPage<ProductDto>>("/api/crm/products?kind=Expense"))!.Items);
    }

    [Fact]
    public async Task A_reader_can_preview_but_cannot_load_the_pack()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        using var reader = await LoginAsync(s.Reader);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/customization/profiles/logistics/apply", null)).StatusCode);

        Assert.Equal(11, (await reader.GetFromJsonAsync<CatalogStarterPackDto>(Pack))!.Missing);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsync(Pack, JsonContent.Create(new { }))).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<CrmPage<ProductDto>>("/api/crm/products?kind=Expense"))!.Items);
    }

    private async Task<(ModulixUser Admin, ModulixUser Reader)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, ReaderPermissions, Scope.All, reader);
        return (admin, reader);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
