using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportSurveyReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReminderQueuedAt",
                schema: "support",
                table: "Surveys",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReminderTokenHash",
                schema: "support",
                table: "Surveys",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Surveys_ReminderToken",
                schema: "support",
                table: "Surveys",
                columns: new[] { "TenantId", "ReminderTokenHash" },
                unique: true,
                filter: "[ReminderTokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Surveys_ReminderToken",
                schema: "support",
                table: "Surveys");

            migrationBuilder.DropColumn(
                name: "ReminderQueuedAt",
                schema: "support",
                table: "Surveys");

            migrationBuilder.DropColumn(
                name: "ReminderTokenHash",
                schema: "support",
                table: "Surveys");
        }
    }
}
