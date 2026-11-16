using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropHrHolidays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0049 dilim 6a'da tatil listesi core.Holidays'e taşındı; eski tablo kesintisiz dağıtım için bir sürüm durdu, artık okunmuyor ve
            // yazılmıyor. Son güvence: taşımadan sonra eski sürümün yazmış olabileceği günler de kopyalanır (oturum değeri olmadığı için RLS kopya
            // süresince kapatılır; tekrar çalıştırmak güvenlidir).
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = OFF);");
            migrationBuilder.Sql("""
                INSERT INTO [core].[Holidays] ([Id], [TenantId], [Date], [Name], [HalfDay], [CreatedAt], [UpdatedAt], [Version])
                SELECT h.[Id], h.[TenantId], h.[Date], h.[Name], h.[HalfDay], h.[CreatedAt], h.[UpdatedAt], h.[Version]
                FROM [hr].[Holidays] h
                WHERE NOT EXISTS (SELECT 1 FROM [core].[Holidays] c WHERE c.[TenantId] = h.[TenantId] AND c.[Date] = h.[Date]);
                """);
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = ON);");

            // Tablo politikadan çıkmadan düşürülemez (yüklemler şemaya bağlıdır).
            TenantIsolationSql.RemoveTable(migrationBuilder, "hr", "Holidays");

            migrationBuilder.DropTable(
                name: "Holidays",
                schema: "hr");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Holidays",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CustomFields = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    HalfDay = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                    table.UniqueConstraint("AK_Holidays_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "UX_Holidays_Date",
                schema: "hr",
                table: "Holidays",
                columns: new[] { "TenantId", "Date" },
                unique: true);

            // Geri alma: tablo yeniden politikaya girer ve Core'daki liste geri kopyalanır (CoreHolidays geri alınırsa veri burada durur).
            TenantIsolationSql.AddTable(migrationBuilder, "hr", "Holidays");
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = OFF);");
            migrationBuilder.Sql("""
                INSERT INTO [hr].[Holidays] ([Id], [TenantId], [Date], [Name], [HalfDay], [CreatedAt], [UpdatedAt], [Version])
                SELECT c.[Id], c.[TenantId], c.[Date], c.[Name], c.[HalfDay], c.[CreatedAt], c.[UpdatedAt], c.[Version]
                FROM [core].[Holidays] c;
                """);
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = ON);");
        }
    }
}
