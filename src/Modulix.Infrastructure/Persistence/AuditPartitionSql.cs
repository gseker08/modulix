using System.Globalization;
using System.Text;

namespace Modulix.Infrastructure.Persistence;

/// <summary>
/// <c>core.AuditLog</c>'un aylık bölümlemesi (ADR-0052). EF modelinde bölüm şeması olmadığı için bölüm işlevi, şema, kümelenmiş anahtar ve
/// dizinlerin şemaya hizalanması burada ham SQL'dir. Bölüm boşaltma (<c>TRUNCATE ... WITH (PARTITIONS)</c>) yalnızca bütün dizinler hizalıyken
/// çalışır; bu tabloya dizin ekleyen migration <see cref="AlignIndex"/> kullanmalıdır (entegrasyon testi denetler).
/// </summary>
public static class AuditPartitionSql
{
    public const string FunctionName = "AuditLogMonthly";
    public const string SchemeName = "AuditLogMonthlyScheme";

    /// <summary>Migration'ın açtığı ilk ve son ay sınırı; sonraki sınırları arşiv komutu açar.</summary>
    public static readonly DateOnly FirstBoundary = new(2026, 1, 1);

    public static readonly DateOnly LastBoundary = new(2030, 12, 1);

    private static readonly (string Name, string Columns)[] Indexes =
    [
        ("IX_AuditLog_TenantId_EntityKey_RecordId_OccurredAt", "[TenantId], [EntityKey], [RecordId], [OccurredAt]"),
        ("IX_AuditLog_TenantId_OccurredAt", "[TenantId], [OccurredAt]"),
        ("IX_AuditLog_TenantId_UserId_OccurredAt", "[TenantId], [UserId], [OccurredAt]"),
    ];

    /// <summary>Ay sınırının SQL değişmezi (ayın ilk günü 00:00 UTC).</summary>
    public static string Boundary(DateOnly month) =>
        string.Create(CultureInfo.InvariantCulture, $"'{month:yyyy-MM}-01T00:00:00.0000000+00:00'");

    /// <summary>Ay sınırının SQL değişmezi; değerin UTC ayı kullanılır.</summary>
    public static string Boundary(DateTimeOffset month) => Boundary(DateOnly.FromDateTime(month.UtcDateTime));

    /// <summary>Var olan bir dizini bölüm şemasına taşır (aynı ad ve sütunlarla yeniden kurar).</summary>
    public static string AlignIndex(string name, string columns) =>
        $"CREATE NONCLUSTERED INDEX [{name}] ON [core].[AuditLog] ({columns}) WITH (DROP_EXISTING = ON) ON [{SchemeName}]([OccurredAt]);";

    /// <summary>Bölüm işlevi ve şemasını açar, birincil anahtarı <c>(OccurredAt, Id)</c> olarak şemada kurar, dizinleri hizalar.</summary>
    public static string Up()
    {
        StringBuilder boundaries = new();
        for (DateOnly month = FirstBoundary; month <= LastBoundary; month = month.AddMonths(1))
        {
            boundaries.Append(boundaries.Length == 0 ? string.Empty : ", ").Append(Boundary(month));
        }

        StringBuilder sql = new();
        sql.AppendLine(CultureInfo.InvariantCulture, $"CREATE PARTITION FUNCTION [{FunctionName}] (datetimeoffset(7)) AS RANGE RIGHT FOR VALUES ({boundaries});");
        sql.AppendLine(CultureInfo.InvariantCulture, $"CREATE PARTITION SCHEME [{SchemeName}] AS PARTITION [{FunctionName}] ALL TO ([PRIMARY]);");
        sql.AppendLine("ALTER TABLE [core].[AuditLog] DROP CONSTRAINT [PK_AuditLog];");
        sql.AppendLine(CultureInfo.InvariantCulture,
            $"ALTER TABLE [core].[AuditLog] ADD CONSTRAINT [PK_AuditLog] PRIMARY KEY CLUSTERED ([OccurredAt], [Id]) ON [{SchemeName}]([OccurredAt]);");
        foreach ((string name, string columns) in Indexes)
        {
            sql.AppendLine(AlignIndex(name, columns));
        }

        return sql.ToString();
    }

    /// <summary>Bölümlemeyi kaldırır: anahtar yeniden <c>Id</c>, tablo ve dizinler <c>PRIMARY</c> dosya grubunda.</summary>
    public static string Down()
    {
        StringBuilder sql = new();
        sql.AppendLine("ALTER TABLE [core].[AuditLog] DROP CONSTRAINT [PK_AuditLog];");
        sql.AppendLine("ALTER TABLE [core].[AuditLog] ADD CONSTRAINT [PK_AuditLog] PRIMARY KEY CLUSTERED ([Id]) ON [PRIMARY];");
        foreach ((string name, string columns) in Indexes)
        {
            sql.AppendLine(CultureInfo.InvariantCulture,
                $"CREATE NONCLUSTERED INDEX [{name}] ON [core].[AuditLog] ({columns}) WITH (DROP_EXISTING = ON) ON [PRIMARY];");
        }

        sql.AppendLine(CultureInfo.InvariantCulture, $"DROP PARTITION SCHEME [{SchemeName}];");
        sql.AppendLine(CultureInfo.InvariantCulture, $"DROP PARTITION FUNCTION [{FunctionName}];");
        return sql.ToString();
    }
}
