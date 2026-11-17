using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TargetedSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0054 dilim 4a: rapor ve pano paylaşımına hedef türü (kullanıcı, rol, ekip, iş birimi). Yalnızca sütun eklenir; UserId sütunu
            // hedef kimliğini taşımaya devam eder. Mevcut satırlar ve önceki sürümün yazdığı satırlar "User" olur (geriye uyumlu).
            migrationBuilder.DropIndex(
                name: "IX_DashboardShares_TenantId_DashboardId_UserId",
                schema: "core",
                table: "DashboardShares");

            migrationBuilder.DropIndex(
                name: "IX_DashboardShares_TenantId_UserId",
                schema: "core",
                table: "DashboardShares");

            migrationBuilder.DropIndex(
                name: "IX_ReportShares_TenantId_ReportId_UserId",
                schema: "core",
                table: "ReportShares");

            migrationBuilder.DropIndex(
                name: "IX_ReportShares_TenantId_UserId",
                schema: "core",
                table: "ReportShares");

            migrationBuilder.AddColumn<string>(
                name: "TargetKind",
                schema: "core",
                table: "DashboardShares",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<string>(
                name: "TargetKind",
                schema: "core",
                table: "ReportShares",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardShares_TenantId_DashboardId_TargetKind_UserId",
                schema: "core",
                table: "DashboardShares",
                columns: new[] { "TenantId", "DashboardId", "TargetKind", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardShares_TenantId_TargetKind_UserId",
                schema: "core",
                table: "DashboardShares",
                columns: new[] { "TenantId", "TargetKind", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportShares_TenantId_ReportId_TargetKind_UserId",
                schema: "core",
                table: "ReportShares",
                columns: new[] { "TenantId", "ReportId", "TargetKind", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportShares_TenantId_TargetKind_UserId",
                schema: "core",
                table: "ReportShares",
                columns: new[] { "TenantId", "TargetKind", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Grup hedefli satırlar geri alınırken silinir: eski şemada "UserId" yalnızca kullanıcıdır.
            migrationBuilder.Sql("DELETE FROM [core].[DashboardShares] WHERE [TargetKind] <> N'User';");
            migrationBuilder.Sql("DELETE FROM [core].[ReportShares] WHERE [TargetKind] <> N'User';");

            migrationBuilder.DropIndex(
                name: "IX_DashboardShares_TenantId_DashboardId_TargetKind_UserId",
                schema: "core",
                table: "DashboardShares");

            migrationBuilder.DropIndex(
                name: "IX_DashboardShares_TenantId_TargetKind_UserId",
                schema: "core",
                table: "DashboardShares");

            migrationBuilder.DropIndex(
                name: "IX_ReportShares_TenantId_ReportId_TargetKind_UserId",
                schema: "core",
                table: "ReportShares");

            migrationBuilder.DropIndex(
                name: "IX_ReportShares_TenantId_TargetKind_UserId",
                schema: "core",
                table: "ReportShares");

            migrationBuilder.DropColumn(
                name: "TargetKind",
                schema: "core",
                table: "DashboardShares");

            migrationBuilder.DropColumn(
                name: "TargetKind",
                schema: "core",
                table: "ReportShares");

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

            migrationBuilder.CreateIndex(
                name: "IX_ReportShares_TenantId_ReportId_UserId",
                schema: "core",
                table: "ReportShares",
                columns: new[] { "TenantId", "ReportId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportShares_TenantId_UserId",
                schema: "core",
                table: "ReportShares",
                columns: new[] { "TenantId", "UserId" });
        }
    }
}
