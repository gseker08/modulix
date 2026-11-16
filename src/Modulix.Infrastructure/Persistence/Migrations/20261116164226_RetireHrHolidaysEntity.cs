using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireHrHolidaysEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0049 dilim 6a kapanışı, adım 1/2 (daralt): eski hr.Holidays tablosu modelden çıkar ama veritabanında ve RLS politikasında KALIR.
            // Bir önceki sürümün modelinde tablo hâlâ vardır; aynı migration'da düşürmek geriye uyumsuzdur (migration-compat). Tablo, bu sürüm
            // her yerde yayına girdikten sonraki migration'da düşürülür (elle yazılır: TenantIsolationSql.RemoveTable + DROP TABLE).
            //
            // Son güvence: taşımadan (CoreHolidays) sonra eski sürümün yazmış olabileceği günler Core'a kopyalanır. Migration'da oturum değeri
            // olmadığı için RLS kopya süresince kapatılır; tekrar çalıştırmak güvenlidir.
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = OFF);");
            migrationBuilder.Sql("""
                INSERT INTO [core].[Holidays] ([Id], [TenantId], [Date], [Name], [HalfDay], [CreatedAt], [UpdatedAt], [Version])
                SELECT h.[Id], h.[TenantId], h.[Date], h.[Name], h.[HalfDay], h.[CreatedAt], h.[UpdatedAt], h.[Version]
                FROM [hr].[Holidays] h
                WHERE NOT EXISTS (SELECT 1 FROM [core].[Holidays] c WHERE c.[TenantId] = h.[TenantId] AND c.[Date] = h.[Date]);
                """);
            migrationBuilder.Sql("ALTER SECURITY POLICY [core].[TenantIsolationPolicy] WITH (STATE = ON);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tablo düşürülmedi; geri alınacak şema değişikliği yok (kopyalanan günler Core'da kalır).
        }
    }
}
