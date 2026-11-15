using System.Net;
using System.Net.Http.Json;
using Modulix.Core.Platform;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using static Modulix.Modules.Crm.CrmModule.Permissions;

namespace Modulix.IntegrationTests;

/// <summary>
/// Yetki önbelleği (ADR-0048): istek başı yetki verisi önbellekteyken de yetki değişikliği bir sonraki istekte geçerlidir
/// (aynı süreçte yazma önbelleği hemen boşaltır), bir tenant'ın girdisi başka tenant'ın isteğinde kullanılmaz.
/// </summary>
public class AccessCacheTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Url = "/api/crm/companies";

    [Fact]
    public async Task Repeated_requests_keep_working_from_the_cache()
    {
        var (tenant, _) = await SetUpAsync();
        var reader = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead], reader);
        using var client = await LoginAsync(reader);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
        }
    }

    [Fact]
    public async Task Taking_away_a_permission_applies_on_the_next_request_even_when_cached()
    {
        var (tenant, _) = await SetUpAsync();
        var reader = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead], reader);
        using var client = await LoginAsync(reader);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);

        // Rol yalnızca kişi okuma izni verenle değiştirilir: önbellekteki roller artık geçersizdir.
        await factory.CreateRoleAsync(tenant.Id, [ContactsRead], reader);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Disabling_the_membership_or_the_module_applies_on_the_next_request()
    {
        var (tenant, admin) = await SetUpAsync();
        var reader = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead], reader);
        using var readerClient = await LoginAsync(reader);
        using var adminClient = await LoginAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await readerClient.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync(Url)).StatusCode);

        await factory.DisableMembershipAsync(reader.Id, tenant.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await readerClient.GetAsync(Url)).StatusCode);

        await factory.SetModuleAsync(tenant.Id, "crm", enabled: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task A_cached_permission_in_one_tenant_is_not_used_in_another_tenant()
    {
        var (tenantA, _) = await SetUpAsync();
        var (tenantB, _) = await SetUpAsync();
        var person = await factory.CreateUserAsync(tenantA, tenantB);
        await factory.CreateRoleAsync(tenantA.Id, [CompaniesRead], person);
        await factory.CreateRoleAsync(tenantB.Id, [ContactsRead], person);
        using var client = await LoginAsync(person);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/active-tenant", new SelectTenantRequest(tenantA.Id))).StatusCode);
        var inA = await client.GetFromJsonAsync<CrmPage<CompanyDto>>(Url);
        Assert.NotNull(inA);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/active-tenant", new SelectTenantRequest(tenantB.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
    }

    private async Task<(Tenant Tenant, ModulixUser Admin)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, admin);
        return (tenant, admin);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
