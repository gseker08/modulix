using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreHolidays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Holidays",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HalfDay = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_TenantId_Date",
                schema: "core",
                table: "Holidays",
                columns: new[] { "TenantId", "Date" },
                unique: true);

            // Yeni tenant tablosu Row-Level Security politikasına girer (ADR-0002/0020).
            TenantIsolationSql.AddTable(migrationBuilder, "core", "Holidays");

            // ADR-0049 dilim 6a: firmanın tatil listesi İK'dan Core'a taşınır. Kimlik, sürüm ve zamanlar korunur. Migration'da oturum değeri olmadığı
            // için RLS altında SELECT 0 satır döner ve INSERT engellenir; politika kopya süresince kapatılır. Tekrar çalıştırmak güvenlidir.
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = OFF);");
            migrationBuilder.Sql("""
                INSERT INTO [core].[Holidays] ([Id], [TenantId], [Date], [Name], [HalfDay], [CreatedAt], [UpdatedAt], [Version])
                SELECT h.[Id], h.[TenantId], h.[Date], h.[Name], h.[HalfDay], h.[CreatedAt], h.[UpdatedAt], h.[Version]
                FROM [hr].[Holidays] h
                WHERE NOT EXISTS (SELECT 1 FROM [core].[Holidays] c WHERE c.[TenantId] = h.[TenantId] AND c.[Date] = h.[Date]);
                """);
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = ON);");

            // Tatil yazma izni İK'dan Core'a geçti: listeyi düzenleyebilen roller düzenlemeye devam eder.
            migrationBuilder.Sql(PermissionGrantSql.GrantWhereGranted("core.holidays.manage", "hr.leaves.manage"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alma: veri hr.Holidays'te durduğu için yalnızca Core tablosu kaldırılır (sonradan Core'a eklenen günler kaybolur).
            TenantIsolationSql.RemoveTable(migrationBuilder, "core", "Holidays");

            migrationBuilder.DropTable(
                name: "Holidays",
                schema: "core");
        }
    }
}
