using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmSurchargeQuoteLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                schema: "crm",
                table: "SurchargeRules",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceRuleId",
                schema: "crm",
                table: "QuoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceRuleVersion",
                schema: "crm",
                table: "QuoteLines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SurchargePresentation",
                schema: "crm",
                table: "QuoteLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SurchargeRules_TaxRate",
                schema: "crm",
                table: "SurchargeRules",
                sql: "[TaxRatePercent] IS NULL OR ([TaxRatePercent] >= 0 AND [TaxRatePercent] <= 100)");

            migrationBuilder.CreateIndex(
                name: "UX_QuoteLines_SurchargeRule",
                schema: "crm",
                table: "QuoteLines",
                columns: new[] { "TenantId", "QuoteId", "SourceRuleId" },
                unique: true,
                filter: "[SourceRuleId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SurchargeRules_TaxRate",
                schema: "crm",
                table: "SurchargeRules");

            migrationBuilder.DropIndex(
                name: "UX_QuoteLines_SurchargeRule",
                schema: "crm",
                table: "QuoteLines");

            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                schema: "crm",
                table: "SurchargeRules");

            migrationBuilder.DropColumn(
                name: "SourceRuleId",
                schema: "crm",
                table: "QuoteLines");

            migrationBuilder.DropColumn(
                name: "SourceRuleVersion",
                schema: "crm",
                table: "QuoteLines");

            migrationBuilder.DropColumn(
                name: "SurchargePresentation",
                schema: "crm",
                table: "QuoteLines");
        }
    }
}
