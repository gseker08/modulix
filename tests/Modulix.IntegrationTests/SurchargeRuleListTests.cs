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
/// Ek ücret kuralı liste ekranının sunucu tarafı (dilim 2d): sıralama anahtarları ve süzgeçler çalışır, geçersiz kural reddedilir,
/// başka firmanın kuralı görünmez, yalnız okuma izni olan yazamaz.
/// </summary>
public class SurchargeRuleListTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly string[] ReaderPermissions = [PricesRead];

    [Fact]
    public async Task List_sorts_by_server_keys_and_filters_by_state()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        await CreateAsync(admin, new SaveSurchargeRuleRequest("Bravo", Amount: 50m, Priority: 2, TaxRatePercent: 20m));
        await CreateAsync(admin, new SaveSurchargeRuleRequest("Alfa", Amount: 70m, Priority: 3, TaxRatePercent: 20m, IsActive: false));
        await CreateAsync(admin, new SaveSurchargeRuleRequest("Charlie", Kind: "Percent", Percent: 5m, BaseKey: "quote.subtotal", Priority: 1, TaxRatePercent: 20m));

        Assert.Equal(["Alfa", "Bravo", "Charlie"], await NamesAsync(admin, "sort=name"));
        Assert.Equal(["Charlie", "Bravo", "Alfa"], await NamesAsync(admin, "sort=-name"));
        Assert.Equal(["Charlie", "Bravo", "Alfa"], await NamesAsync(admin, "sort=priority"));
        Assert.Equal(["Alfa"], await NamesAsync(admin, "active=false"));
        Assert.Equal(["Charlie"], await NamesAsync(admin, "kind=Percent"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/crm/surcharge-rules?sort=bilinmeyen")).StatusCode);
    }

    [Fact]
    public async Task Invalid_rule_is_rejected_with_field_errors()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var response = await admin.PostAsJsonAsync("/api/crm/surcharge-rules", new SaveSurchargeRuleRequest("", Amount: 10m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Rules_are_tenant_isolated_and_a_reader_cannot_write()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        using var readerA = await LoginAsync(a.Reader);
        await CreateAsync(adminA, new SaveSurchargeRuleRequest("Yalnız A", Amount: 10m, TaxRatePercent: 20m));

        Assert.Empty(await NamesAsync(adminB, "search=Yaln"));
        Assert.Equal(["Yalnız A"], await NamesAsync(readerA, "search=Yaln"));
        var denied = await readerA.PostAsJsonAsync("/api/crm/surcharge-rules", new SaveSurchargeRuleRequest("Yetkisiz", Amount: 10m, TaxRatePercent: 20m));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private static async Task<List<string>> NamesAsync(HttpClient client, string query) =>
        [.. (await client.GetFromJsonAsync<CrmPage<SurchargeRuleDto>>($"/api/crm/surcharge-rules?{query}"))!.Items.Select(r => r.Name)];

    private static async Task CreateAsync(HttpClient client, SaveSurchargeRuleRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/crm/surcharge-rules", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
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
