using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mikeka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BettingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MinPickOdds = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: false),
                    MaxPickOdds = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: false),
                    MinCombinedOdds = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: false),
                    MaxCombinedOdds = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "MaxCombinedOdds", "MaxPickOdds", "MinCombinedOdds", "MinPickOdds", "UpdatedAt" },
                values: new object[] { 1, 2.20m, 1.20m, 2.10m, 1.10m, new DateTime(2026, 10, 5, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
