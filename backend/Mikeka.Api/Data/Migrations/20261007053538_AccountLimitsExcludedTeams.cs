using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mikeka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountLimitsExcludedTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "ExcludedTeams",
                table: "Settings",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int>(
                name: "MaxLosses",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxCombinedOdds",
                table: "Accounts",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "MaxLosses",
                table: "Accounts",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxPickOdds",
                table: "Accounts",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinCombinedOdds",
                table: "Accounts",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinPickOdds",
                table: "Accounts",
                type: "numeric(8,3)",
                precision: 8,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ExcludedTeams", "MaxLosses" },
                values: new object[] { new List<string>(), 4 });

            // Existing accounts keep the odds they used until now: the current Settings values.
            migrationBuilder.Sql("""
                UPDATE "Accounts" a SET "MinPickOdds" = s."MinPickOdds", "MaxPickOdds" = s."MaxPickOdds",
                    "MinCombinedOdds" = s."MinCombinedOdds", "MaxCombinedOdds" = s."MaxCombinedOdds"
                FROM "Settings" s WHERE s."Id" = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludedTeams",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "MaxLosses",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "MaxCombinedOdds",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "MaxLosses",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "MaxPickOdds",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "MinCombinedOdds",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "MinPickOdds",
                table: "Accounts");
        }
    }
}
