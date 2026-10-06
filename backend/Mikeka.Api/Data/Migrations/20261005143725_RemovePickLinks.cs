using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mikeka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemovePickLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HowTo",
                table: "SlipPicks");

            migrationBuilder.DropColumn(
                name: "MatchUrl",
                table: "SlipPicks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HowTo",
                table: "SlipPicks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchUrl",
                table: "SlipPicks",
                type: "text",
                nullable: true);
        }
    }
}
