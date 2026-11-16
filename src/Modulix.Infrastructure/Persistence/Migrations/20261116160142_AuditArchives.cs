using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <summary>ADR-0052 dilim 2: denetim kaydı arşivinin dönem kaydı (core.AuditArchives), RLS altında ve uygulamaya salt okunur.</summary>
    public partial class AuditArchives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditArchives",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RowCount = table.Column<long>(type: "bigint", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditArchives", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditArchives_TenantId_PeriodStart",
                schema: "core",
                table: "AuditArchives",
                columns: new[] { "TenantId", "PeriodStart" },
                unique: true);

            TenantIsolationSql.AddTable(migrationBuilder, "core", "AuditArchives");

            // Arşiv satırını yalnızca audit-archive komutu (migrator) yazar; uygulama rolü okur. Satır silinirse dosya yetim içerik temizliğine açılırdı (ADR-0052).
            migrationBuilder.Sql(
                "IF DATABASE_PRINCIPAL_ID(N'modulix_app_role') IS NOT NULL DENY INSERT, UPDATE, DELETE ON [core].[AuditArchives] TO [modulix_app_role];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantIsolationSql.RemoveTable(migrationBuilder, "core", "AuditArchives");

            migrationBuilder.DropTable(
                name: "AuditArchives",
                schema: "core");
        }
    }
}
