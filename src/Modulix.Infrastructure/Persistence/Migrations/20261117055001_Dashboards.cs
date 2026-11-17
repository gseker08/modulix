using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Dashboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Dashboards",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dashboards", x => x.Id);
                    table.UniqueConstraint("AK_Dashboards_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "DashboardWidgets",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DashboardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardWidgets", x => x.Id);
                    table.UniqueConstraint("AK_DashboardWidgets_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_DashboardWidgets_Dashboards_TenantId_DashboardId",
                        columns: x => new { x.TenantId, x.DashboardId },
                        principalSchema: "core",
                        principalTable: "Dashboards",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DashboardWidgets_Reports_TenantId_ReportId",
                        columns: x => new { x.TenantId, x.ReportId },
                        principalSchema: "core",
                        principalTable: "Reports",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dashboards_TenantId_OwnerUserId_Name",
                schema: "core",
                table: "Dashboards",
                columns: new[] { "TenantId", "OwnerUserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardWidgets_TenantId_DashboardId_Position",
                schema: "core",
                table: "DashboardWidgets",
                columns: new[] { "TenantId", "DashboardId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardWidgets_TenantId_ReportId",
                schema: "core",
                table: "DashboardWidgets",
                columns: new[] { "TenantId", "ReportId" });

            // Yeni tenant tabloları Row-Level Security politikasına girer (ADR-0002/0020, ikinci katman).
            TenantIsolationSql.AddTable(migrationBuilder, "core", "Dashboards");
            TenantIsolationSql.AddTable(migrationBuilder, "core", "DashboardWidgets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantIsolationSql.RemoveTable(migrationBuilder, "core", "DashboardWidgets");
            TenantIsolationSql.RemoveTable(migrationBuilder, "core", "Dashboards");

            migrationBuilder.DropTable(
                name: "DashboardWidgets",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Dashboards",
                schema: "core");
        }
    }
}
