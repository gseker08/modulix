using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Api.Jobs;
using Modulix.Infrastructure.Persistence;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0046 dilim 3: modül işleri tek zamanlayıcıdan çalışır. İş yalnızca erişime açık ve modülü açık tenant için çalışır; lisansı salt okunur
/// olan modülde yazan iş çalışmaz; aynı tenant için tur başka yerde çalışıyorsa atlanır; bir tenant'ın turu başka tenant'a dokunmaz.
/// </summary>
public class ModuleJobsTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string JobKey = OrderJobsRunner.JobKey;
    private const string ModuleKey = "orders";

    private ModuleJobRunner Runner => factory.Services.GetRequiredService<ModuleJobRunner>();

    [Fact]
    public async Task The_job_runs_for_a_tenant_with_the_module_enabled()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, ModuleKey, enabled: true);

        Assert.Contains(tenant.Id, await Runner.TenantIdsAsync(ModuleKey, CancellationToken.None));
        var run = await Runner.RunTenantAsync(JobKey, tenant.Id, CancellationToken.None);

        Assert.Equal(ModuleJobRunStatus.Completed, run.Status);
        Assert.NotNull(run.Result);
    }

    [Fact]
    public async Task An_unknown_job_is_rejected()
    {
        var tenant = await factory.CreateTenantAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Runner.RunTenantAsync("orders.missing", tenant.Id, CancellationToken.None));
    }

    [Fact]
    public async Task The_job_does_not_run_for_a_tenant_without_the_module()
    {
        // Tenant izolasyonu: modülü açık A'nın turu, modülü kapalı B'yi listeye almaz; B için doğrudan çağrı da çalışmaz.
        var a = await factory.CreateTenantAsync();
        var b = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(a.Id, ModuleKey, enabled: true);
        await factory.SetModuleAsync(b.Id, ModuleKey, enabled: false);

        var tenants = await Runner.TenantIdsAsync(ModuleKey, CancellationToken.None);

        Assert.Contains(a.Id, tenants);
        Assert.DoesNotContain(b.Id, tenants);
        Assert.Equal(ModuleJobRunStatus.ModuleNotAllowed, (await Runner.RunTenantAsync(JobKey, b.Id, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task The_job_does_not_run_while_the_module_licence_is_read_only()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, ModuleKey, enabled: true);
        await factory.SetEntitlementEndAsync(tenant.Id, ModuleKey, DateTimeOffset.UtcNow.AddDays(-400));

        Assert.Equal(ModuleJobRunStatus.ModuleNotAllowed, (await Runner.RunTenantAsync(JobKey, tenant.Id, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_closed_tenant_is_not_visited()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, ModuleKey, enabled: true);
        await factory.CloseTenantAsync(tenant.Id);

        Assert.DoesNotContain(tenant.Id, await Runner.TenantIdsAsync(ModuleKey, CancellationToken.None));
    }

    [Fact]
    public async Task The_round_is_skipped_while_the_same_job_holds_the_tenant_lock()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, ModuleKey, enabled: true);
        string connectionString = factory.Services.GetRequiredService<ModulixDatabaseOptions>().AppConnectionString;

        await using var other = new SqlConnection(connectionString);
        await other.OpenAsync();
        await using (var take = other.CreateCommand())
        {
            take.CommandText = "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource = @name, @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0; SELECT @r;";
            take.Parameters.AddWithValue("@name", ModuleJobRunner.LockResource(JobKey, tenant.Id));
            Assert.True(Convert.ToInt32(await take.ExecuteScalarAsync()) >= 0);
        }

        Assert.Equal(ModuleJobRunStatus.Locked, (await Runner.RunTenantAsync(JobKey, tenant.Id, CancellationToken.None)).Status);

        await other.CloseAsync();
        Assert.Equal(ModuleJobRunStatus.Completed, (await Runner.RunTenantAsync(JobKey, tenant.Id, CancellationToken.None)).Status);
    }
}
