using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketingJourneys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Journeys",
                schema: "marketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Target = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SegmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReentryPolicy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExitCondition = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CustomFields = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Journeys", x => x.Id);
                    table.UniqueConstraint("AK_Journeys_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Journeys_Segments_TenantId_SegmentId",
                        columns: x => new { x.TenantId, x.SegmentId },
                        principalSchema: "marketing",
                        principalTable: "Segments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JourneyEnrollments",
                schema: "marketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JourneyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EnteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExitedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExitReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CustomFields = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneyEnrollments", x => x.Id);
                    table.UniqueConstraint("AK_JourneyEnrollments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_JourneyEnrollments_Journeys_TenantId_JourneyId",
                        columns: x => new { x.TenantId, x.JourneyId },
                        principalSchema: "marketing",
                        principalTable: "Journeys",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEnrollments_Journey",
                schema: "marketing",
                table: "JourneyEnrollments",
                columns: new[] { "TenantId", "JourneyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_JourneyEnrollments_Active",
                schema: "marketing",
                table: "JourneyEnrollments",
                columns: new[] { "TenantId", "JourneyId", "TargetId" },
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_Journeys_Status",
                schema: "marketing",
                table: "Journeys",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Journeys_TenantId_SegmentId",
                schema: "marketing",
                table: "Journeys",
                columns: new[] { "TenantId", "SegmentId" });

            migrationBuilder.CreateIndex(
                name: "UX_Journeys_Code",
                schema: "marketing",
                table: "Journeys",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            // Yeni tenant tabloları Row-Level Security politikasına girer (ADR-0002/0020).
            TenantIsolationSql.AddTable(migrationBuilder, "marketing", "Journeys");
            TenantIsolationSql.AddTable(migrationBuilder, "marketing", "JourneyEnrollments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantIsolationSql.RemoveTable(migrationBuilder, "marketing", "JourneyEnrollments");
            TenantIsolationSql.RemoveTable(migrationBuilder, "marketing", "Journeys");

            migrationBuilder.DropTable(
                name: "JourneyEnrollments",
                schema: "marketing");

            migrationBuilder.DropTable(
                name: "Journeys",
                schema: "marketing");
        }
    }
}
