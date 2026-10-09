using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mikeka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PickResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Result",
                table: "SlipPicks",
                type: "integer",
                nullable: true);

            // Slips already won: every pick won (PickResult.Won = 0).
            migrationBuilder.Sql("UPDATE \"SlipPicks\" SET \"Result\" = 0 WHERE \"SlipId\" IN (SELECT \"Id\" FROM \"Slips\" WHERE \"Status\" = 'Won');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Result",
                table: "SlipPicks");
        }
    }
}
