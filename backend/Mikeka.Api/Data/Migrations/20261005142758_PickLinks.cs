using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mikeka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PickLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            // Existing picks: the match page was already kept in MatchId; 1win goal-interval picks get their instructions.
            migrationBuilder.Sql("""
                UPDATE "SlipPicks" SET "MatchUrl" = "MatchId" WHERE "MatchId" LIKE 'http%';
                UPDATE "SlipPicks" p
                SET "HowTo" = 'Intervals → "Total from ' || split_part(p."Interval", '-', 1) || ' to ' || split_part(p."Interval", '-', 2)
                              || ' minute" → ' || p."Side" || ' ' || trim(trailing '.' from trim(trailing '0' from p."Line"::text))
                FROM "Slips" s JOIN "Accounts" a ON a."Id" = s."AccountId"
                WHERE s."Id" = p."SlipId" AND a."Site" = '1win' AND p."Market" = 'goals' AND p."Interval" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HowTo",
                table: "SlipPicks");

            migrationBuilder.DropColumn(
                name: "MatchUrl",
                table: "SlipPicks");
        }
    }
}
