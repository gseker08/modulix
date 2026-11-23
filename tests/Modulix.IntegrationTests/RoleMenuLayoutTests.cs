using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Records;
using Modulix.Infrastructure.Identity;
using Modulix.Infrastructure.Persistence;
using Modulix.Shared.Access;
using Modulix.Shared.Auth;
using static Modulix.Core.Modules.CoreModule.Permissions;

namespace Modulix.IntegrationTests;

/// <summary>E8 dilim 2a: rol başına menü düzeni. Kayıt, doğrulama, yetki, tenant izolasyonu, rol silinince temizlik, kullanıcının erişim yanıtı ve denetim.</summary>
public class RoleMenuLayoutTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    [Fact]
    public async Task Manager_saves_a_role_layout_and_members_get_it_in_their_access()
    {
        var tenant = await factory.CreateTenantAsync();
        var manager = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ManageRoles], manager);
        var member = await factory.CreateUserAsync(tenant);
        var role = await factory.CreateRoleAsync(tenant.Id, [], member);
        using var managerClient = await LoginAsync(manager);
        using var memberClient = await LoginAsync(member);

        // Düzen yokken boş; kullanıcının erişim yanıtında rol kimliği var, satır yok.
        var empty = (await managerClient.GetFromJsonAsync<RoleMenuResponse>($"/api/roles/{role.Id}/menu"))!;
        Assert.Empty(empty.Items);
        var before = (await memberClient.GetFromJsonAsync<MyAccessResponse>("/api/tenant/access"))!;
        Assert.Contains(role.Id, before.RoleIds);
        Assert.Empty(before.MenuLayout);

        var saved = await managerClient.PutAsJsonAsync($"/api/roles/{role.Id}/menu", new SaveRoleMenuRequest(
            [new("crm.leads", 2, false), new("crm.companies", 1, false), new("crm.quote-approvals", null, true)]));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var items = (await saved.Content.ReadFromJsonAsync<RoleMenuResponse>())!.Items;
        Assert.Equal(["crm.companies", "crm.leads", "crm.quote-approvals"], items.Select(i => i.ItemKey));
        Assert.True(items.Single(i => i.ItemKey == "crm.quote-approvals").Hidden);

        var access = (await memberClient.GetFromJsonAsync<MyAccessResponse>("/api/tenant/access"))!;
        Assert.Equal(3, access.MenuLayout.Count);
        Assert.All(access.MenuLayout, r => Assert.Equal(role.Id, r.RoleId));

        // Yeniden kaydetmek değiştirir; boş liste düzeni kaldırır.
        var replaced = await managerClient.PutAsJsonAsync($"/api/roles/{role.Id}/menu", new SaveRoleMenuRequest([new("crm.leads", 5, false)]));
        Assert.Single((await replaced.Content.ReadFromJsonAsync<RoleMenuResponse>())!.Items);
        var cleared = await managerClient.PutAsJsonAsync($"/api/roles/{role.Id}/menu", new SaveRoleMenuRequest([]));
        Assert.Empty((await cleared.Content.ReadFromJsonAsync<RoleMenuResponse>())!.Items);
        Assert.Empty((await memberClient.GetFromJsonAsync<MyAccessResponse>("/api/tenant/access"))!.MenuLayout);
    }

    [Fact]
    public async Task Section_order_rows_are_saved_and_returned_like_item_rows()
    {
        // E8 dilim 3: bölüm sırası aynı tabloda "section.<uygulama>.<bölüm>" anahtarlı satırdır; sunucu yeni bir kural eklemez.
        var (client, _, roleId) = await ManagerWithRoleAsync();
        var saved = await client.PutAsJsonAsync($"/api/roles/{roleId}/menu", new SaveRoleMenuRequest(
            [new("section.crm.crm.sales", 10, false), new("section.crm.crm.customers", 20, false), new("crm.leads", null, true)]));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var items = (await saved.Content.ReadFromJsonAsync<RoleMenuResponse>())!.Items;
        Assert.Equal(10, items.Single(i => i.ItemKey == "section.crm.crm.sales").Order);
        Assert.Equal(20, items.Single(i => i.ItemKey == "section.crm.crm.customers").Order);
    }

    [Fact]
    public async Task Invalid_keys_orders_and_duplicates_are_rejected_and_nothing_is_written()
    {
        var (client, tenantId, roleId) = await ManagerWithRoleAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/roles/{roleId}/menu", new SaveRoleMenuRequest([new("crm.leads", 1, false)]))).StatusCode);

        foreach (var bad in new SaveRoleMenuRequest[]
        {
            new([new("CRM.Leads", 1, false)]),            // büyük harf
            new([new("crm..leads", 1, false)]),           // art arda ayraç
            new([new("crm.leads; drop", 1, false)]),      // geçersiz karakter
            new([new(new string('a', 101), 1, false)]),   // çok uzun
            new([new("crm.leads", -1, false)]),           // sıra aralık dışı
            new([new("crm.leads", 10_001, false)]),
            new([new("crm.leads", 1, false), new("crm.leads", 2, true)]), // tekrar
        })
        {
            var response = await client.PutAsJsonAsync($"/api/roles/{roleId}/menu", bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // Başarısız kayıt eski düzeni bozmaz.
        var current = (await client.GetFromJsonAsync<RoleMenuResponse>($"/api/roles/{roleId}/menu"))!;
        Assert.Equal("crm.leads", Assert.Single(current.Items).ItemKey);
        using var scope = factory.OpenScope(tenantId);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().RoleMenuItems.CountAsync());
    }

    [Fact]
    public async Task Only_role_managers_write_and_the_system_role_has_no_layout()
    {
        var tenant = await factory.CreateTenantAsync();
        var manager = await factory.CreateUserAsync(tenant);
        var managerRole = await factory.CreateRoleAsync(tenant.Id, [ManageRoles], manager);
        var plain = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ManageSettings], plain);
        using var managerClient = await LoginAsync(manager);
        using var plainClient = await LoginAsync(plain);
        var body = new SaveRoleMenuRequest([new("crm.leads", 1, true)]);

        // Yetki: rol yönetme izni yoksa ne okur ne yazar.
        Assert.Equal(HttpStatusCode.Forbidden, (await plainClient.GetAsync($"/api/roles/{managerRole.Id}/menu")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plainClient.PutAsJsonAsync($"/api/roles/{managerRole.Id}/menu", body)).StatusCode);

        // Olmayan rol 404; sistem rolü (yönetici) 409: yönetici menüyü gizleyerek kendini kilitleyemez.
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.PutAsJsonAsync($"/api/roles/{Guid.NewGuid()}/menu", body)).StatusCode);
        // Sistem rolü ilk yönetici atanınca oluşur.
        var admin = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, admin);
        var system = await SystemRoleIdAsync(tenant.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await managerClient.PutAsJsonAsync($"/api/roles/{system}/menu", body)).StatusCode);

        // Yöneticinin erişim yanıtında düzen satırı gelmez.
        using var adminClient = await LoginAsync(admin);
        var access = (await adminClient.GetFromJsonAsync<MyAccessResponse>("/api/tenant/access"))!;
        Assert.True(access.IsAdministrator);
        Assert.Empty(access.MenuLayout);
    }

    [Fact]
    public async Task Layouts_are_tenant_isolated_and_deleted_with_the_role_and_audited()
    {
        var (clientA, tenantA, roleA) = await ManagerWithRoleAsync();
        var (clientB, _, roleB) = await ManagerWithRoleAsync();
        Assert.Equal(HttpStatusCode.OK, (await clientA.PutAsJsonAsync($"/api/roles/{roleA}/menu", new SaveRoleMenuRequest([new("crm.leads", 1, true)]))).StatusCode);

        // Tenant izolasyonu: B'nin yöneticisi A'nın rolünü göremez ve yazamaz; kendi rolünde düzen yok.
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/roles/{roleA}/menu")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.PutAsJsonAsync($"/api/roles/{roleA}/menu", new SaveRoleMenuRequest([new("crm.leads", 9, false)]))).StatusCode);
        Assert.Empty((await clientB.GetFromJsonAsync<RoleMenuResponse>($"/api/roles/{roleB}/menu"))!.Items);
        Assert.True((await clientA.GetFromJsonAsync<RoleMenuResponse>($"/api/roles/{roleA}/menu"))!.Items.Single().Hidden);

        // Denetim: değişiklik rolün kaydında "menu" alanı olarak görünür; aynı düzeni tekrar kaydetmek yeni kayıt yazmaz.
        async Task<int> AuditCountAsync()
        {
            using var scope = factory.OpenScope(tenantA);
            return await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().AuditLog
                .CountAsync(a => a.RecordId == roleA && a.Action == AuditAction.Updated && a.Changes.Contains("menu"));
        }

        Assert.Equal(1, await AuditCountAsync());
        await clientA.PutAsJsonAsync($"/api/roles/{roleA}/menu", new SaveRoleMenuRequest([new("crm.leads", 1, true)]));
        Assert.Equal(1, await AuditCountAsync());

        // Rol silinince düzeni de silinir.
        Assert.Equal(HttpStatusCode.NoContent, (await clientA.DeleteAsync($"/api/roles/{roleA}")).StatusCode);
        using var check = factory.OpenScope(tenantA);
        Assert.False(await check.ServiceProvider.GetRequiredService<ModulixDbContext>().RoleMenuItems.AnyAsync());
    }

    private async Task<(HttpClient Client, Guid TenantId, Guid RoleId)> ManagerWithRoleAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        var manager = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ManageRoles], manager);
        var role = await factory.CreateRoleAsync(tenant.Id, []);
        return (await LoginAsync(manager), tenant.Id, role.Id);
    }

    private async Task<Guid> SystemRoleIdAsync(Guid tenantId)
    {
        using var scope = factory.OpenScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().Roles.Where(r => r.IsSystem).Select(r => r.Id).SingleAsync();
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
