using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmailMessageLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0034 §6, dilim 18b: kuyruğa yazılan e-postanın dili satırla saklanır. Boş bırakılabilir (geriye uyumlu): eski satırların ve
            // dile bağlı olmayan e-postaların dili yoktur. platform tablosudur; RLS dışıdır.
            migrationBuilder.AddColumn<string>(
                name: "Language",
                schema: "platform",
                table: "EmailMessages",
                type: "varchar(5)",
                unicode: false,
                maxLength: 5,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                schema: "platform",
                table: "EmailMessages");
        }
    }
}
