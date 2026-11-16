using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Files;
using Modulix.Core.Records;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Audit;

/// <param name="Start">Dönemin başı (dahil); ilk bölümde <see cref="DateTimeOffset.MinValue"/>.</param>
/// <param name="End">Dönemin sonu (hariç).</param>
/// <param name="Tenants">Dosyası yazılan tenant sayısı.</param>
/// <param name="Rows">Dosyalara yazılan satır sayısı.</param>
/// <param name="Cleared">Bölüm boşaltıldı mı.</param>
/// <param name="Problem">Boşaltılmadıysa nedeni.</param>
public sealed record AuditArchivePeriod(DateTimeOffset Start, DateTimeOffset End, int Tenants, long Rows, bool Cleared, string? Problem);

/// <param name="Periods">İşlenen dönemler.</param>
/// <param name="BoundariesMerged">Kaldırılan boş bölüm sınırı sayısı.</param>
/// <param name="BoundariesAdded">İleriye açılan bölüm sınırı sayısı.</param>
public sealed record AuditArchiveResult(IReadOnlyList<AuditArchivePeriod> Periods, int BoundariesMerged, int BoundariesAdded);

/// <summary>
/// Denetim kaydı arşivi (ADR-0052 dilim 2; <c>audit-archive</c> komutu). Sıcak saklama süresinden (<see cref="AuditArchiveOptions.HotMonths"/>) eski,
/// dolu her bölüm için: her tenant'ın o dönemdeki satırlarını gzip'li JSON Lines dosyası olarak nesne deposuna yazar ve
/// <c>core.AuditArchives</c>'e kaydeder; dosyalara yazılan satır toplamı bölümün satır sayısını tutarsa bölümü boşaltır
/// (<c>TRUNCATE ... WITH (PARTITIONS)</c>). Sonra iki yanı boş eski sınırları birleştirir ve ileriye <see cref="AuditArchiveOptions.FutureMonths"/>
/// ay sınır açar.
/// <para>
/// Migrator bağlantısıyla çalışır: bölüm boşaltma <c>ALTER</c> ister, uygulama rolünde yoktur. Satırlar Row-Level Security altındadır;
/// her tenant ayrı oturum değeriyle okunur, başka tenant'ın satırı onun dosyasına giremez. Sayı tutmazsa (ör. tenant kaydı olmayan satır)
/// bölüm boşaltılmaz, yazılan dosyalar kalır. Tekrar çalıştırmak güvenlidir: boşaltılmamış dönem baştan arşivlenir, eski dosyanın yerine yenisi geçer.
/// Aynı anda tek çalışma olur (uygulama kilidi).
/// </para>
/// </summary>
public sealed class AuditArchiver(
    ModulixDatabaseOptions database,
    IBlobStorage blobs,
    IOptions<AuditArchiveOptions> options,
    ILogger<AuditArchiver> logger)
{
    private const string LockName = "Modulix.AuditArchive";

    public async Task<AuditArchiveResult> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var thisMonth = new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var cutoff = thisMonth.AddMonths(-Math.Max(1, settings.HotMonths));

        await using var admin = new SqlConnection(database.MigrationsConnectionString);
        await admin.OpenAsync(cancellationToken);
        if (!await TryLockAsync(admin, cancellationToken))
        {
            throw new InvalidOperationException("Başka bir denetim kaydı arşivi çalışıyor.");
        }

        await using var tenantConnection = new SqlConnection(database.MigrationsConnectionString);
        await tenantConnection.OpenAsync(cancellationToken);

        List<AuditArchivePeriod> periods = [];
        foreach (var partition in (await PartitionsAsync(admin, cancellationToken))
                     .Where(p => p.Upper is { } upper && upper <= cutoff && p.Rows > 0))
        {
            periods.Add(await ArchivePartitionAsync(admin, tenantConnection, partition, now, cancellationToken));
        }

        var merged = await MergeEmptyBoundariesAsync(admin, cutoff, cancellationToken);
        var added = await AddFutureBoundariesAsync(admin, thisMonth.AddMonths(Math.Max(0, settings.FutureMonths)), cancellationToken);
        return new AuditArchiveResult(periods, merged, added);
    }

    private async Task<AuditArchivePeriod> ArchivePartitionAsync(
        SqlConnection admin, SqlConnection tenantConnection, Partition partition, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var start = partition.Lower ?? DateTimeOffset.MinValue;
        var end = partition.Upper!.Value;
        var tenants = 0;
        long rows = 0;
        foreach (var tenantId in await TenantIdsAsync(admin, cancellationToken))
        {
            var written = await ArchiveTenantAsync(tenantConnection, tenantId, start, end, now, cancellationToken);
            if (written > 0)
            {
                tenants++;
                rows += written;
            }
        }

        // Satır sayısı bölüm istatistiğinden okunur (RLS dışı); kapalı dönemde yeni satır olmadığı için kesindir.
        var expected = (await PartitionsAsync(admin, cancellationToken)).Single(p => p.Number == partition.Number).Rows;
        if (rows != expected)
        {
            var problem = string.Create(CultureInfo.InvariantCulture,
                $"Bölümde {expected} satır var, tenant dosyalarına {rows} satır yazıldı; bölüm boşaltılmadı.");
            logger.LogError("Denetim kaydı arşivi {Start:o}–{End:o}: {Problem}", start, end, problem);
            return new AuditArchivePeriod(start, end, tenants, rows, false, problem);
        }

        await using (var truncate = admin.CreateCommand())
        {
            truncate.CommandText = string.Create(CultureInfo.InvariantCulture,
                $"TRUNCATE TABLE [core].[AuditLog] WITH (PARTITIONS ({partition.Number}));");
            await truncate.ExecuteNonQueryAsync(cancellationToken);
        }

        logger.LogInformation("Denetim kaydı arşivlendi {Start:o}–{End:o}: {Tenants} tenant, {Rows} satır; bölüm boşaltıldı.", start, end, tenants, rows);
        return new AuditArchivePeriod(start, end, tenants, rows, true, null);
    }

    /// <summary>Tenant'ın dönemdeki satırlarını dosyaya yazar ve arşiv satırını ekler ya da yeniler. Yazılan satır sayısını döner (0: dosya yok).</summary>
    private async Task<long> ArchiveTenantAsync(
        SqlConnection connection, Guid tenantId, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await SetTenantAsync(connection, tenantId, cancellationToken);
        try
        {
            await using (var any = connection.CreateCommand())
            {
                any.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM [core].[AuditLog] WHERE [TenantId] = @tenant AND [OccurredAt] >= @start AND [OccurredAt] < @end) THEN 1 ELSE 0 END;";
                AddRange(any, tenantId, start, end);
                if ((int)(await any.ExecuteScalarAsync(cancellationToken))! == 0)
                {
                    return 0;
                }
            }

            var path = Path.GetTempFileName();
            try
            {
                var rows = await WriteFileAsync(connection, tenantId, start, end, path, cancellationToken);
                string sha256;
                long size;
                await using (var file = File.OpenRead(path))
                {
                    size = file.Length;
                    sha256 = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant();
                }

                var key = StoredFile.NewStorageKey();
                await using (var file = File.OpenRead(path))
                {
                    await blobs.PutAsync(tenantId, key, file, cancellationToken);
                }

                var previousKey = await SaveEntryAsync(connection, tenantId, start, end, key, rows, size, sha256, now, cancellationToken);
                if (previousKey is not null)
                {
                    await blobs.DeleteAsync(tenantId, previousKey, cancellationToken);
                }

                return rows;
            }
            finally
            {
                File.Delete(path);
            }
        }
        finally
        {
            await SetTenantAsync(connection, null, cancellationToken);
        }
    }

    private static async Task<long> WriteFileAsync(
        SqlConnection connection, Guid tenantId, DateTimeOffset start, DateTimeOffset end, string path, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [Id], [UserId], [EntityKey], [RecordId], [Action], [Changes], [Source], [CorrelationId], [Subscription], [OccurredAt]
            FROM [core].[AuditLog]
            WHERE [TenantId] = @tenant AND [OccurredAt] >= @start AND [OccurredAt] < @end
            ORDER BY [OccurredAt], [Id];
            """;
        AddRange(command, tenantId, start, end);

        long rows = 0;
        await using var file = File.Create(path);
        await using (var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true))
        {
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                await using (var json = new Utf8JsonWriter(gzip))
                {
                    json.WriteStartObject();
                    json.WriteString("id", reader.GetGuid(0));
                    json.WriteString("userId", reader.GetGuid(1));
                    json.WriteString("entityKey", reader.GetString(2));
                    json.WriteString("recordId", reader.GetGuid(3));
                    json.WriteString("action", ((AuditAction)reader.GetInt32(4)).ToString());
                    json.WritePropertyName("changes");
                    json.WriteRawValue(reader.GetString(5));
                    json.WriteString("source", ((ExecutionSource)reader.GetInt32(6)).ToString());
                    WriteNullable(json, "correlationId", reader, 7);
                    WriteNullable(json, "subscription", reader, 8);
                    json.WriteString("occurredAt", reader.GetFieldValue<DateTimeOffset>(9));
                    json.WriteEndObject();
                }

                gzip.WriteByte((byte)'\n');
                rows++;
            }
        }

        return rows;
    }

    private static void WriteNullable(Utf8JsonWriter json, string name, SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, reader.GetString(ordinal));
        }
    }

    /// <summary>Arşiv satırını ekler ya da (tekrar çalışmada) yeniler; yenilenen satırın eski dosya anahtarını döner.</summary>
    private static async Task<string?> SaveEntryAsync(
        SqlConnection connection, Guid tenantId, DateTimeOffset start, DateTimeOffset end, string key, long rows, long size, string sha256,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @previous varchar(64) = (SELECT [StorageKey] FROM [core].[AuditArchives] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = @tenant AND [PeriodStart] = @start);
            IF @previous IS NULL
                INSERT INTO [core].[AuditArchives] ([Id], [TenantId], [PeriodStart], [PeriodEnd], [StorageKey], [RowCount], [SizeBytes], [Sha256], [ArchivedAt])
                VALUES (NEWID(), @tenant, @start, @end, @key, @rows, @size, @sha256, @now);
            ELSE
                UPDATE [core].[AuditArchives]
                SET [PeriodEnd] = @end, [StorageKey] = @key, [RowCount] = @rows, [SizeBytes] = @size, [Sha256] = @sha256, [ArchivedAt] = @now
                WHERE [TenantId] = @tenant AND [PeriodStart] = @start;
            SELECT @previous;
            """;
        AddRange(command, tenantId, start, end);
        command.Parameters.Add("@key", SqlDbType.VarChar, StoredFile.StorageKeyLength).Value = key;
        command.Parameters.Add("@rows", SqlDbType.BigInt).Value = rows;
        command.Parameters.Add("@size", SqlDbType.BigInt).Value = size;
        command.Parameters.Add("@sha256", SqlDbType.VarChar, AuditArchiveEntry.Sha256Length).Value = sha256;
        command.Parameters.Add("@now", SqlDbType.DateTimeOffset).Value = now;
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    /// <summary>İki yanı da boş olan, kesim tarihinden eski bölüm sınırlarını kaldırır (bölüm sayısı büyümez; veri taşınmaz).</summary>
    private static async Task<int> MergeEmptyBoundariesAsync(SqlConnection admin, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var merged = 0;
        while (true)
        {
            var partitions = await PartitionsAsync(admin, cancellationToken);
            var boundary = partitions.Zip(partitions.Skip(1))
                .Where(pair => pair.First.Rows == 0 && pair.Second.Rows == 0 && pair.Second.Upper is { } upper && upper <= cutoff)
                .Select(pair => pair.First.Upper)
                .FirstOrDefault();
            if (boundary is null)
            {
                return merged;
            }

            await using var command = admin.CreateCommand();
            command.CommandText = $"ALTER PARTITION FUNCTION [{AuditPartitionSql.FunctionName}]() MERGE RANGE ({AuditPartitionSql.Boundary(boundary.Value)});";
            await command.ExecuteNonQueryAsync(cancellationToken);
            merged++;
        }
    }

    /// <summary>Son sınır <paramref name="until"/>'den önceyse ay ay yeni sınır açar.</summary>
    private static async Task<int> AddFutureBoundariesAsync(SqlConnection admin, DateTimeOffset until, CancellationToken cancellationToken)
    {
        var last = (await PartitionsAsync(admin, cancellationToken)).Max(p => p.Upper);
        if (last is null)
        {
            return 0;
        }

        var added = 0;
        for (var next = last.Value.AddMonths(1); next <= until; next = next.AddMonths(1))
        {
            await using var command = admin.CreateCommand();
            command.CommandText = $"""
                ALTER PARTITION SCHEME [{AuditPartitionSql.SchemeName}] NEXT USED [PRIMARY];
                ALTER PARTITION FUNCTION [{AuditPartitionSql.FunctionName}]() SPLIT RANGE ({AuditPartitionSql.Boundary(next)});
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            added++;
        }

        return added;
    }

    private static async Task<List<Partition>> PartitionsAsync(SqlConnection admin, CancellationToken cancellationToken)
    {
        await using var command = admin.CreateCommand();
        command.CommandText = $"""
            SELECT ps.[partition_number], CAST(lo.[value] AS datetimeoffset(7)), CAST(hi.[value] AS datetimeoffset(7)), ps.[row_count]
            FROM sys.dm_db_partition_stats ps
            JOIN sys.indexes i ON i.[object_id] = ps.[object_id] AND i.[index_id] = ps.[index_id]
            JOIN sys.partition_schemes s ON s.[data_space_id] = i.[data_space_id]
            LEFT JOIN sys.partition_range_values lo ON lo.[function_id] = s.[function_id] AND lo.[boundary_id] = ps.[partition_number] - 1
            LEFT JOIN sys.partition_range_values hi ON hi.[function_id] = s.[function_id] AND hi.[boundary_id] = ps.[partition_number]
            WHERE ps.[object_id] = OBJECT_ID(N'[core].[AuditLog]') AND ps.[index_id] = 1 AND s.[name] = N'{AuditPartitionSql.SchemeName}'
            ORDER BY ps.[partition_number];
            """;
        List<Partition> partitions = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            partitions.Add(new Partition(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetInt64(3)));
        }

        if (partitions.Count == 0)
        {
            throw new InvalidOperationException("core.AuditLog bölümlenmemiş; önce migration'ları uygulayın (ADR-0052 dilim 1).");
        }

        return partitions;
    }

    private static async Task<List<Guid>> TenantIdsAsync(SqlConnection admin, CancellationToken cancellationToken)
    {
        await using var command = admin.CreateCommand();
        command.CommandText = "SELECT [Id] FROM [platform].[Tenants] ORDER BY [Id];";
        List<Guid> ids = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private static async Task SetTenantAsync(SqlConnection connection, Guid? tenantId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"EXEC sys.sp_set_session_context @key = N'{TenantIsolationSql.SessionKey}', @value = @tenant;";
        command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = (object?)tenantId ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddRange(SqlCommand command, Guid tenantId, DateTimeOffset start, DateTimeOffset end)
    {
        command.Parameters.Add("@tenant", SqlDbType.UniqueIdentifier).Value = tenantId;
        command.Parameters.Add("@start", SqlDbType.DateTimeOffset).Value = start;
        command.Parameters.Add("@end", SqlDbType.DateTimeOffset).Value = end;
    }

    /// <summary>Oturum boyunca tutulan uygulama kilidi; bağlantı kapanınca bırakılır.</summary>
    private static async Task<bool> TryLockAsync(SqlConnection admin, CancellationToken cancellationToken)
    {
        await using var command = admin.CreateCommand();
        command.CommandText = $"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'{LockName}', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0;
            SELECT @result;
            """;
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! >= 0;
    }

    private sealed record Partition(int Number, DateTimeOffset? Lower, DateTimeOffset? Upper, long Rows);
}
