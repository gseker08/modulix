using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulix.Core.Files;
using Modulix.Infrastructure.Audit;
using Modulix.Infrastructure.Persistence;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0052 dilim 2: <c>audit-archive</c>. Sıcak saklama süresinden eski bölümün satırları tenant başına dosyaya yazılır, bölüm boşaltılır;
/// sayı tutmazsa bölüme dokunulmaz; arşiv satırı tenant'a özeldir, uygulama onu yazamaz ve silemez; yetim içerik temizliği arşiv dosyasını silmez.
/// <para>
/// Testler Ocak ve Şubat 2026 bölümlerini kullanır (başka test o aylara satır yazmaz). "Şimdi" Mart 2028 verilir: 24 aylık sıcak dönemle kesim
/// 1 Mart 2026'dır, yani bugünkü satırlara dokunulmaz.
/// </para>
/// </summary>
public class AuditArchiveTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly DateTimeOffset Now = new(2028, 3, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset February = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset January = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Old_month_is_written_per_tenant_to_the_object_store_and_its_partition_is_cleared()
    {
        var a = await factory.CreateTenantAsync();
        var b = await factory.CreateTenantAsync();
        var recordA = Guid.NewGuid();
        var recordB = Guid.NewGuid();
        await InsertAsync(a.Id, recordA, February.AddDays(3));
        await InsertAsync(a.Id, recordA, February.AddDays(20));
        await InsertAsync(b.Id, recordB, February.AddDays(5));
        var current = Guid.NewGuid();
        await InsertAsync(a.Id, current, DateTimeOffset.UtcNow);

        var result = await Archiver(futureMonths: 36).RunAsync(Now, CancellationToken.None);

        var period = Assert.Single(result.Periods, p => p.End == February.AddMonths(1));
        Assert.True(period.Cleared, period.Problem);
        Assert.True(period.Rows >= 3);
        Assert.DoesNotContain(result.Periods, p => !p.Cleared);

        using (var scope = factory.OpenScope(a.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
            Assert.Equal(0, await db.AuditLog.CountAsync(e => e.RecordId == recordA));
            Assert.Equal(1, await db.AuditLog.CountAsync(e => e.RecordId == current));

            var entry = await db.AuditArchives.SingleAsync(e => e.PeriodEnd == February.AddMonths(1));
            Assert.Equal(2, entry.RowCount);
            var lines = await ReadArchiveAsync(scope, a.Id, entry);
            Assert.Equal(2, lines.Count);
            Assert.All(lines, l => Assert.Equal(recordA.ToString(), l.GetProperty("recordId").GetString()));
            Assert.True(lines[0].GetProperty("occurredAt").GetDateTimeOffset() < lines[1].GetProperty("occurredAt").GetDateTimeOffset());
            Assert.Equal("Updated", lines[0].GetProperty("action").GetString());
            Assert.Equal("eski", lines[0].GetProperty("changes").GetProperty("name").GetProperty("old").GetString());
        }

        using (var scope = factory.OpenScope(b.Id))
        {
            var entry = await scope.ServiceProvider.GetRequiredService<ModulixDbContext>().AuditArchives.SingleAsync();
            var line = Assert.Single(await ReadArchiveAsync(scope, b.Id, entry));
            Assert.Equal(recordB.ToString(), line.GetProperty("recordId").GetString());
        }

        // Boşalan eski bölümlerin sınırları kalkar, ileriye sınır açılır (36 ay: Mart 2031'e kadar).
        var boundaries = await BoundariesAsync();
        Assert.DoesNotContain(February, boundaries);
        Assert.Contains(new DateTimeOffset(2031, 3, 1, 0, 0, 0, TimeSpan.Zero), boundaries);
        Assert.Contains(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), boundaries);

        // Tekrar çalışma dönem bulmaz, hiçbir şeyi değiştirmez.
        var again = await Archiver(futureMonths: 36).RunAsync(Now, CancellationToken.None);
        Assert.Empty(again.Periods);
        Assert.Equal(0, again.BoundariesAdded);
    }

    [Fact]
    public async Task Partition_is_not_cleared_when_rows_of_an_unknown_tenant_would_be_lost()
    {
        var a = await factory.CreateTenantAsync();
        var record = Guid.NewGuid();
        await InsertAsync(a.Id, record, January.AddDays(10));
        var stranger = Guid.NewGuid(); // platform.Tenants'ta yok: hiçbir tenant dosyasına yazılamaz
        await InsertAsync(stranger, Guid.NewGuid(), January.AddDays(11));

        try
        {
            var result = await Archiver().RunAsync(Now, CancellationToken.None);

            var period = Assert.Single(result.Periods, p => p.Start < January.AddDays(11) && p.End > January.AddDays(11));
            Assert.False(period.Cleared);
            Assert.Contains("boşaltılmadı", period.Problem);

            using var scope = factory.OpenScope(a.Id);
            var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
            Assert.Equal(1, await db.AuditLog.CountAsync(e => e.RecordId == record));
            Assert.Equal(1, (await db.AuditArchives.SingleAsync()).RowCount); // dosya yazıldı, tekrar çalışmada yenilenir
        }
        finally
        {
            await DeleteAsMigratorAsync(stranger);
            await DeleteAsMigratorAsync(a.Id);
        }
    }

    [Fact]
    public async Task Archive_entries_are_tenant_private_read_only_for_the_application_and_survive_orphan_sweeping()
    {
        var a = await factory.CreateTenantAsync();
        var b = await factory.CreateTenantAsync();
        await InsertAsync(a.Id, Guid.NewGuid(), February.AddDays(1));
        await Archiver().RunAsync(Now, CancellationToken.None);

        using (var scope = factory.OpenScope(b.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
            Assert.Empty(await db.AuditArchives.ToListAsync());
            Assert.Empty(await db.AuditArchives.IgnoreQueryFilters().Where(e => e.TenantId == a.Id).ToListAsync()); // RLS
        }

        using (var scope = factory.OpenScope(a.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
            var entry = await db.AuditArchives.SingleAsync();

            await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlAsync($"DELETE FROM core.AuditArchives WHERE Id = {entry.Id}"));
            await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlAsync($"UPDATE core.AuditArchives SET [RowCount] = 0 WHERE Id = {entry.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"""
                INSERT INTO core.AuditArchives (Id, TenantId, PeriodStart, PeriodEnd, StorageKey, [RowCount], SizeBytes, Sha256, ArchivedAt)
                VALUES ({Guid.NewGuid()}, {a.Id}, {January}, {February}, {StoredFile.NewStorageKey()}, 0, 0, {new string('0', 64)}, {DateTimeOffset.UtcNow})
                """));

            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IOrphanBlobSweeper>().SweepAsync(TimeSpan.Zero, CancellationToken.None));
            await using var content = await scope.ServiceProvider.GetRequiredService<IBlobStorage>().OpenReadAsync(a.Id, entry.StorageKey, CancellationToken.None);
            Assert.NotNull(content);
        }
    }

    private AuditArchiver Archiver(int futureMonths = 24) => new(
        factory.Services.GetRequiredService<ModulixDatabaseOptions>(),
        factory.Services.GetRequiredService<IBlobStorage>(),
        Options.Create(new AuditArchiveOptions { HotMonths = 24, FutureMonths = futureMonths }),
        NullLogger<AuditArchiver>.Instance);

    /// <summary>Geçmiş tarihli denetim satırı (uygulama zamanı kendisi yazdığı için migrator bağlantısıyla, tenant oturum değeriyle).</summary>
    private async Task InsertAsync(Guid tenantId, Guid recordId, DateTimeOffset occurredAt)
    {
        await using var connection = await MigratorAsync(tenantId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO core.AuditLog (Id, TenantId, UserId, EntityKey, RecordId, Action, Changes, Source, OccurredAt)
            VALUES (NEWID(), @tenant, @user, N'crm.company', @record, 2, N'{"name":{"old":"eski","new":"yeni"}}', 1, @at);
            """;
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@user", Guid.NewGuid());
        command.Parameters.AddWithValue("@record", recordId);
        command.Parameters.AddWithValue("@at", occurredAt);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Testin eski aylara yazdığı satırları kaldırır (uygulama rolü silemez).</summary>
    private async Task DeleteAsMigratorAsync(Guid tenantId)
    {
        await using var connection = await MigratorAsync(tenantId);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM core.AuditLog WHERE TenantId = @tenant AND OccurredAt < @before;";
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@before", February.AddMonths(1));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqlConnection> MigratorAsync(Guid tenantId)
    {
        var connection = new SqlConnection(factory.Services.GetRequiredService<ModulixDatabaseOptions>().MigrationsConnectionString);
        await connection.OpenAsync();
        await using var session = connection.CreateCommand();
        session.CommandText = $"EXEC sys.sp_set_session_context @key = N'{TenantIsolationSql.SessionKey}', @value = @tenant;";
        session.Parameters.AddWithValue("@tenant", tenantId);
        await session.ExecuteNonQueryAsync();
        return connection;
    }

    private async Task<List<DateTimeOffset>> BoundariesAsync()
    {
        await using var connection = new SqlConnection(factory.Services.GetRequiredService<ModulixDatabaseOptions>().MigrationsConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT CAST(r.value AS datetimeoffset(7)) FROM sys.partition_range_values r
            JOIN sys.partition_functions f ON f.function_id = r.function_id WHERE f.name = N'{AuditPartitionSql.FunctionName}';
            """;
        List<DateTimeOffset> boundaries = [];
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            boundaries.Add(reader.GetFieldValue<DateTimeOffset>(0));
        }

        return boundaries;
    }

    /// <summary>Arşiv dosyasını açar, özetini satırdakiyle karşılaştırır ve satırlarını döner.</summary>
    private static async Task<List<JsonElement>> ReadArchiveAsync(IServiceScope scope, Guid tenantId, AuditArchiveEntry entry)
    {
        await using var stored = await scope.ServiceProvider.GetRequiredService<IBlobStorage>().OpenReadAsync(tenantId, entry.StorageKey, CancellationToken.None);
        Assert.NotNull(stored);
        using var buffer = new MemoryStream();
        await stored.CopyToAsync(buffer);
        Assert.Equal(entry.SizeBytes, buffer.Length);
        Assert.Equal(entry.Sha256, Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant());

        buffer.Position = 0;
        await using var gzip = new GZipStream(buffer, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        List<JsonElement> lines = [];
        while (await reader.ReadLineAsync() is { } line)
        {
            lines.Add(JsonDocument.Parse(line).RootElement.Clone());
        }

        return lines;
    }
}
