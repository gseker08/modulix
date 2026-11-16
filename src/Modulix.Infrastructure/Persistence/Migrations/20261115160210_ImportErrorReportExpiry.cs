using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportErrorReportExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ErrorReportExpiresAt",
                schema: "core",
                table: "ImportRuns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_TenantId_ErrorReportExpiresAt",
                schema: "core",
                table: "ImportRuns",
                columns: new[] { "TenantId", "ErrorReportExpiresAt" },
                filter: "[ErrorReportExpiresAt] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportRuns_TenantId_ErrorReportExpiresAt",
                schema: "core",
                table: "ImportRuns");

            migrationBuilder.DropColumn(
                name: "ErrorReportExpiresAt",
                schema: "core",
                table: "ImportRuns");
        }
    }
}
