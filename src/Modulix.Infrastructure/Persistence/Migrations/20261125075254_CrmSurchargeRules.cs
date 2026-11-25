using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmSurchargeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SurchargeRules",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    GroupKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    BaseKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DurationKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FreeUnits = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    QuantityKeys = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Conditions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Presentation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Stackable = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CustomFields = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurchargeRules", x => x.Id);
                    table.UniqueConstraint("AK_SurchargeRules_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_SurchargeRules_Amount", "[Amount] IS NULL OR [Amount] >= 0");
                    table.CheckConstraint("CK_SurchargeRules_FreeUnits", "[FreeUnits] >= 0");
                    table.CheckConstraint("CK_SurchargeRules_Percent", "[Percent] IS NULL OR ([Percent] >= 0 AND [Percent] <= 100)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SurchargeRules_TenantId_IsActive_Priority",
                schema: "crm",
                table: "SurchargeRules",
                columns: new[] { "TenantId", "IsActive", "Priority" });

            TenantIsolationSql.AddTable(migrationBuilder, "crm", "SurchargeRules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantIsolationSql.RemoveTable(migrationBuilder, "crm", "SurchargeRules");

            migrationBuilder.DropTable(
                name: "SurchargeRules",
                schema: "crm");
        }
    }
}
