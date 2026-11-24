using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketingJourneyResponse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastResponseAt",
                schema: "marketing",
                table: "JourneyEnrollments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastResponseKind",
                schema: "marketing",
                table: "JourneyEnrollments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastResponseAt",
                schema: "marketing",
                table: "JourneyEnrollments");

            migrationBuilder.DropColumn(
                name: "LastResponseKind",
                schema: "marketing",
                table: "JourneyEnrollments");
        }
    }
}
