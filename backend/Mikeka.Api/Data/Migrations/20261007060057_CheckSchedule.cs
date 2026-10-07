using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mikeka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CheckSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResultCheckedAt",
                table: "Slips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<List<DateTime>>(
                name: "SettlementChecks",
                table: "Slips",
                type: "timestamp with time zone[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int>(
                name: "CheckIntervalMinutes",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "MatchMinutes",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 180);

            migrationBuilder.AddColumn<int>(
                name: "MaxDaysAhead",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "SameSiteDelayMinutes",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "SettlementGapMinutes",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextCheckAt",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultCheckedAt",
                table: "Slips");

            migrationBuilder.DropColumn(
                name: "SettlementChecks",
                table: "Slips");

            migrationBuilder.DropColumn(
                name: "CheckIntervalMinutes",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "MatchMinutes",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "MaxDaysAhead",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SameSiteDelayMinutes",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SettlementGapMinutes",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NextCheckAt",
                table: "Accounts");

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "ExcludedTeams",
                value: new List<string>());
        }
    }
}
