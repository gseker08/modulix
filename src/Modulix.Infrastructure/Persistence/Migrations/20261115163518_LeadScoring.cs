using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Score",
                schema: "crm",
                table: "Leads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LeadScoreRules",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Field = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Operator = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Points = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CustomFields = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadScoreRules", x => x.Id);
                    table.UniqueConstraint("AK_LeadScoreRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_LeadScoreRules_Points", "[Points] BETWEEN -100 AND 100 AND [Points] <> 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId_Score",
                schema: "crm",
                table: "Leads",
                columns: new[] { "TenantId", "Score" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Leads_Score",
                schema: "crm",
                table: "Leads",
                sql: "[Score] BETWEEN 0 AND 100");

            migrationBuilder.CreateIndex(
                name: "IX_LeadScoreRules_TenantId_IsActive",
                schema: "crm",
                table: "LeadScoreRules",
                columns: new[] { "TenantId", "IsActive" });

            // Yeni tenant tablosu Row-Level Security politikasına girer (ADR-0002/0020). Puan kuralları: ADR-0050.
            TenantIsolationSql.AddTable(migrationBuilder, "crm", "LeadScoreRules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantIsolationSql.RemoveTable(migrationBuilder, "crm", "LeadScoreRules");

            migrationBuilder.DropTable(
                name: "LeadScoreRules",
                schema: "crm");

            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId_Score",
                schema: "crm",
                table: "Leads");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Leads_Score",
                schema: "crm",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "Score",
                schema: "crm",
                table: "Leads");
        }
    }
}
