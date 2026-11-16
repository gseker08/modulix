using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Modules;
using Modulix.Shared.Platform;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0051 dilim 2: modül hakkı yazılırken zorunlu bağımlılık denetlenir. Pazarlama CRM olmadan açılamaz, Pazarlama açıkken CRM
/// kapatılamaz; firma oluşturma, plan tanımı, plan güncelleme ve plan atama aynı kuralı uygular. İsteğe bağlı bağımlılık (Sipariş → CRM)
/// engellemez. Reddedilen işlem hiçbir hakkı değiştirmez.
/// </summary>
public partial class PlatformAdministrationTests
{
    private const string Crm = "crm";
    private const string Marketing = "marketing";

    private static Task<HttpResponseMessage> SetModuleAsync(HttpClient platform, Guid tenantId, string module, bool enabled) =>
        platform.PutAsJsonAsync(
            $"/api/platform/tenants/{tenantId}/modules/{module}", new SetTenantModuleRequest(enabled, Reason: enabled ? null : "Bağımlılık testi"));

    private static async Task<IReadOnlyList<string>> EnabledModulesAsync(HttpClient platform, Guid tenantId) =>
        (await platform.GetFromJsonAsync<PlatformTenantDetailResponse>($"/api/platform/tenants/{tenantId}"))!.Tenant.EnabledModules;

    private async Task EnableDirectlyAsync(Guid tenantId, string module)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IModuleEntitlements>().SetEnabledAsync(tenantId, module, enabled: true, CancellationToken.None);
    }

    [Fact]
    public async Task Modules_open_and_close_in_dependency_order_and_an_optional_dependency_never_blocks()
    {
        var tenant = await factory.CreateTenantAsync();
        using var platform = await LoginAsync(await CreatePlatformAdministratorAsync());

        // İsteğe bağlı bağımlılık: Sipariş CRM olmadan açılır.
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, "orders", true)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, Crm, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, Marketing, true)).StatusCode);
        Assert.Equal(["crm", "marketing", "orders"], (await EnabledModulesAsync(platform, tenant.Id)).Order());

        // Önce bağımlı modül, sonra sağlayan kapanır.
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, Marketing, false)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, Crm, false)).StatusCode);
        Assert.Equal(["orders"], await EnabledModulesAsync(platform, tenant.Id));
    }

    [Fact]
    public async Task A_missing_required_dependency_is_rejected_on_every_entitlement_path_and_changes_nothing()
    {
        var tenant = await factory.CreateTenantAsync();
        using var platform = await LoginAsync(await CreatePlatformAdministratorAsync());

        // Modül açma: CRM kapalıyken Pazarlama açılmaz; ileti iki modülü adıyla söyler.
        var open = await SetModuleAsync(platform, tenant.Id, Marketing, true);
        Assert.Equal(HttpStatusCode.BadRequest, open.StatusCode);
        var message = await open.Content.ReadAsStringAsync();
        Assert.Contains("Pazarlama", message, StringComparison.Ordinal);
        Assert.Contains("CRM", message, StringComparison.Ordinal);
        Assert.Empty(await EnabledModulesAsync(platform, tenant.Id));

        // Modül kapama: Pazarlama açıkken CRM kapatılmaz.
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, Crm, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, Marketing, true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetModuleAsync(platform, tenant.Id, Crm, false)).StatusCode);
        Assert.Equal(["crm", "marketing"], (await EnabledModulesAsync(platform, tenant.Id)).Order());

        // Firma oluşturma: modül listesi kapalı küme olmalı; firma oluşmaz.
        var slug = $"d-{Guid.NewGuid():N}"[..20];
        var created = await platform.PostAsJsonAsync("/api/platform/tenants", new CreateTenantRequest(
            "Bağımlılık Ltd.", slug, [Marketing], [], $"dep-{Guid.NewGuid():N}@test.modulix", "Ilk Yonetici"));
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Contains("modules", await created.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Plan tanımı: Pazarlama CRM'siz plana konamaz; ikisi birlikte konabilir.
        Assert.Equal(HttpStatusCode.BadRequest, (await platform.PostAsJsonAsync(
            "/api/platform/plans", new SavePlanRequest(NewPlanKey(), "Eksik plan", [Marketing]))).StatusCode);
        var full = await CreatePlanAsync(platform, NewPlanKey(), [Crm, Marketing]);
        Assert.Equal(HttpStatusCode.BadRequest, (await platform.PutAsJsonAsync(
            $"/api/platform/plans/{full.Id}", new SavePlanRequest(full.Key, full.Name, [Marketing], null, true))).StatusCode);
    }

    [Fact]
    public async Task A_plan_change_that_would_close_a_required_provider_is_rejected_and_keeps_the_entitlements()
    {
        var tenant = await factory.CreateTenantAsync();
        using var platform = await LoginAsync(await CreatePlatformAdministratorAsync());
        var crmPlan = await CreatePlanAsync(platform, NewPlanKey(), [Crm]);
        var supportPlan = await CreatePlanAsync(platform, NewPlanKey(), ["support"]);

        // CRM plandan, Pazarlama elle (plan dışı) açık.
        Assert.Equal(HttpStatusCode.NoContent, (await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{tenant.Id}/plan", new AssignPlanRequest(crmPlan.Key, "İlk plan"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, Marketing, true)).StatusCode);

        // CRM'siz plana geçiş CRM'i kapatırdı; Pazarlama açık kaldığı için atama reddedilir.
        Assert.Equal(HttpStatusCode.BadRequest, (await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{tenant.Id}/plan", new AssignPlanRequest(supportPlan.Key, "Plan değişikliği"))).StatusCode);
        Assert.Equal(crmPlan.Key, (await PlanOf(platform, tenant.Id)).Subscription!.PlanKey);
        Assert.Equal(["crm", "marketing"], (await EnabledModulesAsync(platform, tenant.Id)).Order());

        // Planın modüllerini değiştirmek de aynı aboneyi bozardı: plan kaydedilmez.
        Assert.Equal(HttpStatusCode.BadRequest, (await platform.PutAsJsonAsync(
            $"/api/platform/plans/{crmPlan.Id}", new SavePlanRequest(crmPlan.Key, crmPlan.Name, ["support"], null, true))).StatusCode);
        var stored = (await platform.GetFromJsonAsync<PlansResponse>("/api/platform/plans"))!.Plans.Single(p => p.Id == crmPlan.Id);
        Assert.Equal([Crm], stored.Modules);

        // Pazarlama kapatılınca aynı plan değişikliği geçer.
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, Marketing, false)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await platform.PutAsJsonAsync(
            $"/api/platform/tenants/{tenant.Id}/plan", new AssignPlanRequest(supportPlan.Key, "Plan değişikliği"))).StatusCode);
        Assert.Equal(["support"], await EnabledModulesAsync(platform, tenant.Id));
    }

    [Fact]
    public async Task One_tenants_modules_do_not_satisfy_or_block_another_tenants_dependencies()
    {
        var tenantA = await factory.CreateTenantAsync();
        var tenantB = await factory.CreateTenantAsync();
        using var platform = await LoginAsync(await CreatePlatformAdministratorAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenantA.Id, Crm, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenantA.Id, Marketing, true)).StatusCode);

        // A'da CRM'in açık olması B'de Pazarlama'yı açtırmaz.
        Assert.Equal(HttpStatusCode.BadRequest, (await SetModuleAsync(platform, tenantB.Id, Marketing, true)).StatusCode);
        Assert.Empty(await EnabledModulesAsync(platform, tenantB.Id));

        // A'da Pazarlama'nın açık olması B'de CRM'in kapatılmasını engellemez.
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenantB.Id, Crm, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenantB.Id, Crm, false)).StatusCode);
        Assert.Equal(["crm", "marketing"], (await EnabledModulesAsync(platform, tenantA.Id)).Order());
    }

    [Fact]
    public async Task Only_platform_administrators_change_modules_and_an_existing_violation_does_not_block_other_changes()
    {
        var tenant = await factory.CreateTenantAsync();
        var tenantAdmin = await factory.CreateUserAsync(tenant);
        await MakeAdministratorAsync(tenant.Id, tenantAdmin);
        using var tenantClient = await LoginAsync(tenantAdmin);
        using var anonymous = factory.CreateBrowserClient();
        using var platform = await LoginAsync(await CreatePlatformAdministratorAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await SetModuleAsync(tenantClient, tenant.Id, Crm, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetModuleAsync(anonymous, tenant.Id, Crm, true)).StatusCode);
        Assert.Empty(await EnabledModulesAsync(platform, tenant.Id));

        // Kural gelmeden önce CRM'siz açılmış Pazarlama (mevcut ihlal): başka modül yine açılır, ihlal kendiliğinden düzeltilmez.
        await EnableDirectlyAsync(tenant.Id, Marketing);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, "support", true)).StatusCode);
        Assert.Equal(["marketing", "support"], (await EnabledModulesAsync(platform, tenant.Id)).Order());
    }
}
