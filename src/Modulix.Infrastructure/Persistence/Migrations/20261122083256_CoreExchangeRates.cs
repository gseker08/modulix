using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreExchangeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromCurrency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    ValidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                    table.CheckConstraint("CK_ExchangeRates_Pair", "[FromCurrency] <> [ToCurrency]");
                    table.CheckConstraint("CK_ExchangeRates_Rate", "[Rate] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "UX_ExchangeRates_PairDay",
                schema: "core",
                table: "ExchangeRates",
                columns: new[] { "TenantId", "FromCurrency", "ToCurrency", "ValidOn" },
                unique: true);

            // Yeni tenant tablosu Row-Level Security politikasına girer (ADR-0002/0020).
            TenantIsolationSql.AddTable(migrationBuilder, "core", "ExchangeRates");

            // ADR-0049 dilim 5a, adım 1/2 (kopyala): firmanın kurları CRM'den Core'a taşınır. Kimlik, sürüm ve zamanlar korunur. Migration'da oturum
            // değeri olmadığı için RLS altında SELECT 0 satır döner ve INSERT engellenir; politika kopya süresince kapatılır. Tekrar çalıştırmak güvenlidir.
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = OFF);");
            migrationBuilder.Sql("""
                INSERT INTO [core].[ExchangeRates] ([Id], [TenantId], [FromCurrency], [ToCurrency], [Rate], [ValidOn], [Source], [Note], [CreatedAt], [UpdatedAt], [Version])
                SELECT r.[Id], r.[TenantId], r.[FromCurrency], r.[ToCurrency], r.[Rate], r.[ValidOn], r.[Source], r.[Note], r.[CreatedAt], r.[UpdatedAt], r.[Version]
                FROM [crm].[ExchangeRates] r
                WHERE NOT EXISTS (SELECT 1 FROM [core].[ExchangeRates] c WHERE c.[TenantId] = r.[TenantId] AND c.[FromCurrency] = r.[FromCurrency]
                    AND c.[ToCurrency] = r.[ToCurrency] AND c.[ValidOn] = r.[ValidOn]);
                """);
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = ON);");

            // Kur yazma izni CRM'den Core'a geçti: fiyatları düzenleyebilen roller kurları düzenlemeye devam eder.
            migrationBuilder.Sql(PermissionGrantSql.GrantWhereGranted("core.exchange-rates.manage", "crm.prices.write"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alma: veri crm.ExchangeRates'te durduğu için yalnızca Core tablosu kaldırılır (sonradan Core'a eklenen kurlar kaybolur).
            TenantIsolationSql.RemoveTable(migrationBuilder, "core", "ExchangeRates");

            migrationBuilder.DropTable(
                name: "ExchangeRates",
                schema: "core");
        }
    }
}
