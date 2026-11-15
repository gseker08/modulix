using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalDueInWorkingHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DueInWorkingHours",
                schema: "core",
                table: "ApprovalRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DueInWorkingHours",
                schema: "core",
                table: "ApprovalDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueInWorkingHours",
                schema: "core",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "DueInWorkingHours",
                schema: "core",
                table: "ApprovalDefinitions");
        }
    }
}
