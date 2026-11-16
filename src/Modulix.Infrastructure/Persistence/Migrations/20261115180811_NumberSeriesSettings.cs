using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NumberSeriesSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NumberSeriesSettings",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Digits = table.Column<int>(type: "int", nullable: true),
                    Reset = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSeriesSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NumberSeriesSettings_TenantId_SeriesKey",
                schema: "core",
                table: "NumberSeriesSettings",
                columns: new[] { "TenantId", "SeriesKey" },
                unique: true);

            // Yeni tenant tablosu Row-Level Security politikasına girer (ADR-0002/0020).
            TenantIsolationSql.AddTable(migrationBuilder, "core", "NumberSeriesSettings");

            // ADR-0049 dilim 2a: seri yapılandırması yeni izindir; kuruluş ayarlarını yönetebilen roller serileri de yönetir.
            migrationBuilder.Sql(PermissionGrantSql.GrantWhereGranted("core.numbering.manage", "core.settings.manage"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantIsolationSql.RemoveTable(migrationBuilder, "core", "NumberSeriesSettings");

            migrationBuilder.DropTable(
                name: "NumberSeriesSettings",
                schema: "core");
        }
    }
}
