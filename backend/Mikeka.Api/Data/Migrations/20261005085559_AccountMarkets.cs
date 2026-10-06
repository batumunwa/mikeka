using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mikeka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountMarkets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Markets",
                table: "Accounts",
                type: "jsonb",
                nullable: true);

            // Existing accounts start with the markets from the requirements: total corners and total yellow cards, Under.
            migrationBuilder.Sql("""
                UPDATE "Accounts"
                SET "Markets" = '[{"Market":"corners","Side":"Under"},{"Market":"cards","Side":"Under"}]'::jsonb
                WHERE "Markets" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Markets",
                table: "Accounts");
        }
    }
}
