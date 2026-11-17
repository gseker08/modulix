using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Modules;
using Modulix.Shared.Access;
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

    // ADR-0051 dilim 3: bağımlılık bilgisi, firmadaki eksik bağımlılıklar ve "bağımlılığı eksik firmalar" raporu.

    private static Task<List<PlatformDependencyGapDto>?> GapsAsync(HttpClient platform) =>
        platform.GetFromJsonAsync<List<PlatformDependencyGapDto>>("/api/platform/dependency-gaps");

    private static async Task<IReadOnlyList<MissingDependencyDto>> MissingAsync(HttpClient platform, Guid tenantId) =>
        (await platform.GetFromJsonAsync<PlatformTenantDetailResponse>($"/api/platform/tenants/{tenantId}"))!.Tenant.MissingDependencies ?? [];

    [Fact]
    public async Task Modules_list_their_dependencies_and_a_tenant_shows_what_is_missing()
    {
        var tenant = await factory.CreateTenantAsync();
        using var platform = await LoginAsync(await CreatePlatformAdministratorAsync());

        // Modül listesi ve katalog beyanı taşır: Pazarlama → CRM zorunlu, Sipariş → CRM isteğe bağlı.
        var modules = (await platform.GetFromJsonAsync<PlatformModulesResponse>("/api/platform/modules"))!.Modules;
        var marketing = Assert.Single(modules.Single(m => m.Key == Marketing).Dependencies!);
        Assert.Equal((Crm, true), (marketing.ModuleKey, marketing.Required));
        Assert.False(string.IsNullOrWhiteSpace(marketing.DisplayName));
        Assert.Contains(modules.Single(m => m.Key == "orders").Dependencies!, d => d.ModuleKey == Crm && !d.Required);
        Assert.Empty(modules.Single(m => m.Key == Crm).Dependencies!);
        var catalog = await platform.GetFromJsonAsync<List<CatalogModuleDto>>("/api/platform/catalog");
        Assert.Contains(catalog!.Single(m => m.Key == Marketing).Dependencies!, d => d.ModuleKey == Crm && d.Required);

        // İsteğe bağlı eksik işlemi engellemez, firma detayında gerekçesiyle görünür; sağlayan açılınca kalkar.
        Assert.Empty(await MissingAsync(platform, tenant.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, "orders", true)).StatusCode);
        var missing = Assert.Single(await MissingAsync(platform, tenant.Id));
        Assert.Equal(("orders", Crm, false), (missing.ModuleKey, missing.DependencyKey, missing.Required));
        Assert.False(string.IsNullOrWhiteSpace(missing.Reason));
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, Crm, true)).StatusCode);
        Assert.Empty(await MissingAsync(platform, tenant.Id));
    }

    [Fact]
    public async Task The_gap_report_lists_only_operational_tenants_with_a_missing_required_dependency()
    {
        var broken = await factory.CreateTenantAsync();
        var optionalOnly = await factory.CreateTenantAsync();
        var healthy = await factory.CreateTenantAsync();
        var closed = await factory.CreateTenantAsync();
        using var platform = await LoginAsync(await CreatePlatformAdministratorAsync());

        // Mevcut ihlal: kural gelmeden önce CRM'siz açılmış Pazarlama.
        await EnableDirectlyAsync(broken.Id, Marketing);
        await EnableDirectlyAsync(closed.Id, Marketing);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, optionalOnly.Id, "orders", true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, healthy.Id, Crm, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, healthy.Id, Marketing, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await platform.PostAsJsonAsync(
            $"/api/platform/tenants/{closed.Id}/close", new TenantReasonRequest("Rapor testi"))).StatusCode);

        var gaps = (await GapsAsync(platform))!;
        var gap = gaps.Single(g => g.TenantId == broken.Id);
        var missing = Assert.Single(gap.Missing);
        Assert.Equal((Marketing, Crm, true), (missing.ModuleKey, missing.DependencyKey, missing.Required));
        Assert.DoesNotContain(gaps, g => g.TenantId == optionalOnly.Id || g.TenantId == healthy.Id || g.TenantId == closed.Id);

        // Rapor hiçbir hakkı değiştirmez; eksik giderilince firma listeden çıkar.
        Assert.Equal(["marketing"], await EnabledModulesAsync(platform, broken.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, broken.Id, Crm, true)).StatusCode);
        Assert.DoesNotContain((await GapsAsync(platform))!, g => g.TenantId == broken.Id);
    }

    [Fact]
    public async Task Only_platform_administrators_read_the_gap_report()
    {
        var tenant = await factory.CreateTenantAsync();
        var tenantAdmin = await factory.CreateUserAsync(tenant);
        await MakeAdministratorAsync(tenant.Id, tenantAdmin);
        using var tenantClient = await LoginAsync(tenantAdmin);
        using var anonymous = factory.CreateBrowserClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await tenantClient.GetAsync("/api/platform/dependency-gaps")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/platform/dependency-gaps")).StatusCode);
    }

    // ADR-0051 dilim 3b: firma yöneticisi eksik bağımlılığı bilgi olarak görür; hakkı değiştiremez.

    [Fact]
    public async Task A_tenant_administrator_sees_missing_dependencies_and_members_and_other_tenants_do_not()
    {
        var tenant = await factory.CreateTenantAsync();
        var other = await factory.CreateTenantAsync();
        var admin = await factory.CreateUserAsync(tenant);
        await MakeAdministratorAsync(tenant.Id, admin);
        var member = await factory.CreateUserAsync(tenant);
        var otherAdmin = await factory.CreateUserAsync(other);
        await MakeAdministratorAsync(other.Id, otherAdmin);
        using var platform = await LoginAsync(await CreatePlatformAdministratorAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, "orders", true)).StatusCode);

        using var adminClient = await LoginAsync(admin);
        var missing = Assert.Single((await adminClient.GetFromJsonAsync<MyAccessResponse>("/api/tenant/access"))!.MissingDependencies);
        Assert.Equal(("orders", Crm, false), (missing.ModuleKey, missing.DependencyKey, missing.Required));
        Assert.False(string.IsNullOrWhiteSpace(missing.ModuleName));
        Assert.False(string.IsNullOrWhiteSpace(missing.DependencyName));
        Assert.False(string.IsNullOrWhiteSpace(missing.Reason));

        // Yönetici olmayan üye ve başka firmanın yöneticisi bu bilgiyi görmez.
        using var memberClient = await LoginAsync(member);
        Assert.Empty((await memberClient.GetFromJsonAsync<MyAccessResponse>("/api/tenant/access"))!.MissingDependencies);
        using var otherClient = await LoginAsync(otherAdmin);
        Assert.Empty((await otherClient.GetFromJsonAsync<MyAccessResponse>("/api/tenant/access"))!.MissingDependencies);

        // Firma yöneticisi hakkı değiştiremez; sağlayan platformdan açılınca bilgi kalkar.
        Assert.Equal(HttpStatusCode.Forbidden, (await SetModuleAsync(adminClient, tenant.Id, Crm, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SetModuleAsync(platform, tenant.Id, Crm, true)).StatusCode);
        Assert.Empty((await adminClient.GetFromJsonAsync<MyAccessResponse>("/api/tenant/access"))!.MissingDependencies);
    }
}
