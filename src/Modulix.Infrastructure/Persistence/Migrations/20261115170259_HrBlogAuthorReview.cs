using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrBlogAuthorReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                schema: "hr",
                table: "BlogPosts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                schema: "hr",
                table: "BlogPosts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedAt",
                schema: "hr",
                table: "BlogPosts",
                type: "datetimeoffset",
                nullable: true);

            // Yazma izni ayrıldı (ADR-0042 bölüm 27): blog yöneten roller yazı yazmaya ve ek eklemeye devam eder.
            migrationBuilder.Sql(PermissionGrantSql.GrantWhereGranted("hr.blog.write", "hr.blog.manage"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewNote",
                schema: "hr",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                schema: "hr",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                schema: "hr",
                table: "BlogPosts");
        }
    }
}
