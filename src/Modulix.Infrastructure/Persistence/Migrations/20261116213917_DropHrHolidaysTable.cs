using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropHrHolidaysTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0049 dilim 6a kapanışı, adım 2/2: eski hr.Holidays tablosu düşer. Entity bir önceki sürümde modelden çıktı (RetireHrHolidaysEntity),
            // o yüzden önceki sürüm de bu şemayla çalışır. Elle yazıldı: tablo modelde olmadığı için EF bu değişikliği üretmez.
            //
            // Düşürmeden önce Core'da olmayan günler bir kez daha kopyalanır (veri kaybı olmasın; tekrar çalıştırmak güvenlidir). Migration'da oturum
            // değeri olmadığı için RLS kopya süresince kapatılır.
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = OFF);");
            migrationBuilder.Sql("""
                INSERT INTO [core].[Holidays] ([Id], [TenantId], [Date], [Name], [HalfDay], [CreatedAt], [UpdatedAt], [Version])
                SELECT h.[Id], h.[TenantId], h.[Date], h.[Name], h.[HalfDay], h.[CreatedAt], h.[UpdatedAt], h.[Version]
                FROM [hr].[Holidays] h
                WHERE NOT EXISTS (SELECT 1 FROM [core].[Holidays] c WHERE c.[TenantId] = h.[TenantId] AND c.[Date] = h.[Date]);
                """);
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = ON);");

            // Politika tabloya bağlıyken tablo düşürülemez: önce koşullar kaldırılır.
            TenantIsolationSql.RemoveTable(migrationBuilder, "hr", "Holidays");

            migrationBuilder.DropTable(
                name: "Holidays",
                schema: "hr");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tablo boş olarak geri kurulur (HrLeaves'teki tanımıyla) ve RLS politikasına yeniden girer; veri Core'da kaldığı için geri kopyalanmaz.
            migrationBuilder.CreateTable(
                name: "Holidays",
                schema: "hr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HalfDay = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CustomFields = table.Column<string>(type: "nvarchar(max)", nullable: true)
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

            TenantIsolationSql.AddTable(migrationBuilder, "hr", "Holidays");
        }
    }
}
