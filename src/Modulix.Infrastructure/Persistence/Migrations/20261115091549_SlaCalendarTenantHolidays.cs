using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SlaCalendarTenantHolidays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0049 dilim 6b: firmanın tatil listesi SLA takvimine de uygulanabilir. Mevcut takvimler kapalı başlar (açık SLA hedefleri
            // değişmesin); yeni takvimlerde seçenek açıktır (entity varsayılanı).
            migrationBuilder.AddColumn<bool>(
                name: "UseTenantHolidays",
                schema: "support",
                table: "SlaCalendars",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseTenantHolidays",
                schema: "support",
                table: "SlaCalendars");
        }
    }
}
