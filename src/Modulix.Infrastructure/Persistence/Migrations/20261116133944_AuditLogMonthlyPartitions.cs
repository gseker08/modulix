using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0052 dilim 1: <c>core.AuditLog</c> aylık bölümlenir. Birincil anahtar <c>(OccurredAt, Id)</c> olur ve bölüm şemasında kurulur,
    /// dizinler şemaya hizalanır. Kümelenmiş dizin yeniden kurulduğu için tablo büyükken bakım penceresinde çalıştırılmalıdır.
    /// Önceki sürümün kodu yalnızca ekler ve okur; anahtar değişikliği onu etkilemez.
    /// </summary>
    public partial class AuditLogMonthlyPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AuditPartitionSql.Up());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AuditPartitionSql.Down());
        }
    }
}
