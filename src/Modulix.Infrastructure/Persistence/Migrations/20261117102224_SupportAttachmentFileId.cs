using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportAttachmentFileId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FileId",
                schema: "support",
                table: "Attachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_NotMoved",
                schema: "support",
                table: "Attachments",
                columns: new[] { "TenantId", "CreatedAt" },
                filter: "[FileId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Attachments_NotMoved",
                schema: "support",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "FileId",
                schema: "support",
                table: "Attachments");
        }
    }
}
