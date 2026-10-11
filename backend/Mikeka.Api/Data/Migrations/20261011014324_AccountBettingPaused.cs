using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mikeka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountBettingPaused : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BettingPaused",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BettingPaused",
                table: "Accounts");
        }
    }
}
