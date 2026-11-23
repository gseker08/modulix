using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Sectors;
using Modulix.Infrastructure.Identity;
using Modulix.Infrastructure.Persistence;
using Modulix.Shared.Auth;
using Modulix.Shared.Sectors;

namespace Modulix.IntegrationTests;

/// <summary>
/// Test fabrikasının faaliyet kataloğu temizliği: katalog platform tablosudur, test düğümleri temizlenmezse aynı veritabanını
/// kullanan her tenant'ın faaliyet seçiminde görünür. Fabrika kapanınca kendi oluşturduğu düğümleri siler; başka kayıt
/// başvuruyorsa pasife alır.
/// </summary>
public class CatalogNodeCleanupTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    [Fact]
    public async Task Disposing_the_factory_removes_the_catalog_nodes_it_created()
    {
        Guid sector, activity, direct;
        using (var temporary = new ModulixApiFactory())
        {
            using var platform = await LoginAsPlatformAdministratorAsync(temporary);
            sector = await CreateNodeAsync(platform, null, "Sector");
            activity = await CreateNodeAsync(platform, sector, "Activity");

            using var scope = temporary.Services.CreateScope();
            var node = new ActivityNode(NewCode(), null, ActivityNodeKind.Sector, "Test sektörü");
            Db(scope).ActivityNodes.Add(node);
            await Db(scope).SaveChangesAsync();
            temporary.TrackCatalogNode(node.Id);
            direct = node.Id;
        }

        using var read = factory.Services.CreateScope();
        Guid[] created = [sector, activity, direct];
        Assert.Empty(await Db(read).ActivityNodes.Where(n => created.Contains(n.Id)).Select(n => n.Code).ToListAsync());
    }

    [Fact]
    public async Task A_node_still_selected_by_another_tenant_is_deactivated_instead_of_deleted()
    {
        var tenant = await factory.CreateTenantAsync();
        Guid sector, activity;
        using (var temporary = new ModulixApiFactory())
        {
            using var platform = await LoginAsPlatformAdministratorAsync(temporary);
            sector = await CreateNodeAsync(platform, null, "Sector");
            activity = await CreateNodeAsync(platform, sector, "Activity");

            // Seçim başka fabrikanın tenant'ına aittir; kapanan fabrika onu silmez, düğüm de bu yüzden silinemez.
            using var scope = factory.OpenScope(tenant.Id);
            Db(scope).ActivitySelections.Add(new TenantActivitySelection(activity));
            await Db(scope).SaveChangesAsync();
        }

        using var read = factory.OpenScope(tenant.Id);
        var left = await Db(read).ActivityNodes.Where(n => n.Id == activity || n.Id == sector).ToListAsync();
        Assert.Equal(2, left.Count);
        Assert.All(left, n => Assert.False(n.IsActive));
        Assert.Single(await Db(read).ActivitySelections.Where(s => s.NodeId == activity).ToListAsync());
    }

    private static string NewCode() => $"t-{Guid.NewGuid():N}"[..14];

    private static ModulixDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ModulixDbContext>();

    private static async Task<Guid> CreateNodeAsync(HttpClient platform, Guid? parentId, string kind)
    {
        var response = await platform.PostAsJsonAsync("/api/platform/sectors/nodes", new CreateActivityNodeRequest(NewCode(), parentId, kind, "Test düğümü", null, 0));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ActivityNodeDto>())!.Id;
    }

    private static async Task<HttpClient> LoginAsPlatformAdministratorAsync(ModulixApiFactory host)
    {
        var user = await host.CreateUserAsync();
        using (var scope = host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulixUser>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            stored.IsPlatformAdministrator = true;
            Assert.True((await users.UpdateAsync(stored)).Succeeded);
        }

        var client = host.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
