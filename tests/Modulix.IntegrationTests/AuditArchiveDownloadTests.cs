using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Records;
using Modulix.Core.Files;
using Modulix.Core.Modules;
using Modulix.Infrastructure.Identity;
using Modulix.Infrastructure.Persistence;
using Modulix.Shared.Audit;
using Modulix.Shared.Auth;
using static Modulix.Core.Modules.CoreModule.Permissions;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0052 dilim 3: denetim kaydı arşivinin listelenmesi ve indirilmesi (<c>/api/audit/archives</c>). <c>audit.read</c> ister; dosya olduğu
/// gibi verilir ve indirme denetim kaydına yazılır; başka tenant'ın dönemi ve depoda olmayan dosya 404'tür. Arşiv satırı uygulama rolüyle
/// yazılamadığı için testler satırı migrator bağlantısıyla, dosyayı depoya doğrudan yazar (bölümlere dokunmaz).
/// </summary>
public class AuditArchiveDownloadTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly DateTimeOffset March = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Auditor_lists_archived_periods_and_downloads_the_file_and_the_download_is_audited()
    {
        var tenant = await factory.CreateTenantAsync();
        var (older, _) = await SeedAsync(tenant.Id, March.AddMonths(-2), March.AddMonths(-1));
        var (id, content) = await SeedAsync(tenant.Id, March.AddMonths(-1), March);
        using var client = await AuditorAsync(tenant);

        var list = (await client.GetFromJsonAsync<List<AuditArchiveDto>>("/api/audit/archives"))!;
        Assert.Equal([id, older], list.Select(a => a.Id));
        var item = list[0];
        Assert.Equal("2026-02", item.Period);
        Assert.Equal(content.Length, item.SizeBytes);
        Assert.Equal(Hash(content), item.Sha256);

        var response = await client.GetAsync($"/api/audit/archives/{id}/download");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/gzip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("denetim-arsivi-2026-02.jsonl.gz", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());

        using var scope = factory.OpenScope(tenant.Id);
        var entry = await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().AuditLog
            .SingleAsync(e => e.EntityKey == CoreModule.AuditedEntityKeys.AuditArchive && e.RecordId == id);
        Assert.Equal(AuditAction.Exported, entry.Action);
        Assert.Contains("2026-02", entry.Changes);
    }

    [Fact]
    public async Task Missing_file_or_unknown_period_is_not_found_and_nothing_is_audited()
    {
        var tenant = await factory.CreateTenantAsync();
        var (lost, _) = await SeedAsync(tenant.Id, March.AddMonths(-1), March, storeFile: false);
        using var client = await AuditorAsync(tenant);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/audit/archives/{lost}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/audit/archives/{Guid.NewGuid()}/download")).StatusCode);

        using var scope = factory.OpenScope(tenant.Id);
        Assert.False(await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().AuditLog
            .AnyAsync(e => e.EntityKey == CoreModule.AuditedEntityKeys.AuditArchive));
    }

    [Fact]
    public async Task Tenant_cannot_list_or_download_another_tenants_archive()
    {
        var a = await factory.CreateTenantAsync();
        var b = await factory.CreateTenantAsync();
        var (id, _) = await SeedAsync(a.Id, March.AddMonths(-1), March);
        using var client = await AuditorAsync(b);

        Assert.Empty((await client.GetFromJsonAsync<List<AuditArchiveDto>>("/api/audit/archives"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/audit/archives/{id}/download")).StatusCode);
    }

    [Fact]
    public async Task User_without_audit_permission_cannot_list_or_download()
    {
        var tenant = await factory.CreateTenantAsync();
        var (id, _) = await SeedAsync(tenant.Id, March.AddMonths(-1), March);
        var user = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ManageSettings], user);
        using var client = await LoginAsync(user);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit/archives")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/audit/archives/{id}/download")).StatusCode);
    }

    /// <summary>Arşiv satırı (migrator bağlantısıyla, tenant oturum değeriyle) ve istenirse dosyası.</summary>
    private async Task<(Guid Id, byte[] Content)> SeedAsync(Guid tenantId, DateTimeOffset start, DateTimeOffset end, bool storeFile = true)
    {
        var id = Guid.NewGuid();
        var key = StoredFile.NewStorageKey();
        var content = RandomNumberGenerator.GetBytes(256);
        if (storeFile)
        {
            using var stream = new MemoryStream(content);
            await factory.Services.GetRequiredService<IBlobStorage>().PutAsync(tenantId, key, stream, CancellationToken.None);
        }

        await using var connection = new SqlConnection(factory.Services.GetRequiredService<ModulixDatabaseOptions>().MigrationsConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            EXEC sys.sp_set_session_context @key = N'{TenantIsolationSql.SessionKey}', @value = @tenant;
            INSERT INTO core.AuditArchives (Id, TenantId, PeriodStart, PeriodEnd, StorageKey, [RowCount], SizeBytes, Sha256, ArchivedAt)
            VALUES (@id, @tenant, @start, @end, @key, 3, @size, @sha, SYSUTCDATETIME());
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@start", start);
        command.Parameters.AddWithValue("@end", end);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@size", (long)content.Length);
        command.Parameters.AddWithValue("@sha", Hash(content));
        await command.ExecuteNonQueryAsync();
        return (id, content);
    }

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private async Task<HttpClient> AuditorAsync(Core.Platform.Tenant tenant)
    {
        var auditor = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ReadAudit], auditor);
        return await LoginAsync(auditor);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
