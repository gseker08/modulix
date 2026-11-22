using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireCrmExchangeRatesEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0049 dilim 5a kapanışı, adım 1/2 (daralt): eski crm.ExchangeRates tablosu modelden çıkar ama veritabanında ve RLS politikasında KALIR.
            // Bir önceki sürümün modelinde tablo hâlâ vardır; aynı migration'da düşürmek geriye uyumsuzdur (migration-compat). Tablo, bu sürüm
            // her yerde yayına girdikten sonraki migration'da düşürülür (elle yazılır: TenantIsolationSql.RemoveTable + DROP TABLE).
            //
            // Son güvence: taşımadan (CoreExchangeRates) sonra eski sürümün yazmış olabileceği kurlar Core'a kopyalanır. Migration'da oturum değeri
            // olmadığı için RLS kopya süresince kapatılır; tekrar çalıştırmak güvenlidir.
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = OFF);");
            migrationBuilder.Sql("""
                INSERT INTO [core].[ExchangeRates] ([Id], [TenantId], [FromCurrency], [ToCurrency], [Rate], [ValidOn], [Source], [Note], [CreatedAt], [UpdatedAt], [Version])
                SELECT r.[Id], r.[TenantId], r.[FromCurrency], r.[ToCurrency], r.[Rate], r.[ValidOn], r.[Source], r.[Note], r.[CreatedAt], r.[UpdatedAt], r.[Version]
                FROM [crm].[ExchangeRates] r
                WHERE NOT EXISTS (SELECT 1 FROM [core].[ExchangeRates] c WHERE c.[TenantId] = r.[TenantId] AND c.[FromCurrency] = r.[FromCurrency]
                    AND c.[ToCurrency] = r.[ToCurrency] AND c.[ValidOn] = r.[ValidOn]);
                """);
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = ON);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tablo düşürülmedi; geri alınacak şema değişikliği yok (kopyalanan kurlar Core'da kalır).
        }
    }
}
