using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrimaryTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0054 dilim 4b: kişinin birincil ekibi. Yalnızca sütun ve süzgeçli tekil dizin eklenir; mevcut satırlar işaretsizdir
            // (hiçbiri işaretli değilse en önce katıldığı ekip birincil sayılır), bu yüzden önceki sürümle geriye uyumludur.
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                schema: "core",
                table: "TeamMembers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "UX_TeamMembers_Primary",
                schema: "core",
                table: "TeamMembers",
                columns: new[] { "TenantId", "MembershipId" },
                unique: true,
                filter: "[IsPrimary] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TeamMembers_Primary",
                schema: "core",
                table: "TeamMembers");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                schema: "core",
                table: "TeamMembers");
        }
    }
}
