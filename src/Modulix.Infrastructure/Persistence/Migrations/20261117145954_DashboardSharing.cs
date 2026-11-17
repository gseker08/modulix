using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DashboardSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                schema: "core",
                table: "Dashboards",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Private");

            migrationBuilder.CreateTable(
                name: "DashboardDefaults",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DashboardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardDefaults", x => x.Id);
                    table.UniqueConstraint("AK_DashboardDefaults_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_DashboardDefaults_Dashboards_TenantId_DashboardId",
                        columns: x => new { x.TenantId, x.DashboardId },
                        principalSchema: "core",
                        principalTable: "Dashboards",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DashboardShares",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DashboardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardShares", x => x.Id);
                    table.UniqueConstraint("AK_DashboardShares_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_DashboardShares_Dashboards_TenantId_DashboardId",
                        columns: x => new { x.TenantId, x.DashboardId },
                        principalSchema: "core",
                        principalTable: "Dashboards",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dashboards_TenantId_Visibility",
                schema: "core",
                table: "Dashboards",
                columns: new[] { "TenantId", "Visibility" });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardDefaults_TenantId_DashboardId",
                schema: "core",
                table: "DashboardDefaults",
                columns: new[] { "TenantId", "DashboardId" });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardDefaults_TenantId_UserId",
                schema: "core",
                table: "DashboardDefaults",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardShares_TenantId_DashboardId_UserId",
                schema: "core",
                table: "DashboardShares",
                columns: new[] { "TenantId", "DashboardId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardShares_TenantId_UserId",
                schema: "core",
                table: "DashboardShares",
                columns: new[] { "TenantId", "UserId" });

            // Yeni tenant tabloları Row-Level Security politikasına girer (ADR-0002/0020, ikinci katman).
            TenantIsolationSql.AddTable(migrationBuilder, "core", "DashboardShares");
            TenantIsolationSql.AddTable(migrationBuilder, "core", "DashboardDefaults");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantIsolationSql.RemoveTable(migrationBuilder, "core", "DashboardDefaults");
            TenantIsolationSql.RemoveTable(migrationBuilder, "core", "DashboardShares");

            migrationBuilder.DropTable(
                name: "DashboardDefaults",
                schema: "core");

            migrationBuilder.DropTable(
                name: "DashboardShares",
                schema: "core");

            migrationBuilder.DropIndex(
                name: "IX_Dashboards_TenantId_Visibility",
                schema: "core",
                table: "Dashboards");

            migrationBuilder.DropColumn(
                name: "Visibility",
                schema: "core",
                table: "Dashboards");
        }
    }
}
