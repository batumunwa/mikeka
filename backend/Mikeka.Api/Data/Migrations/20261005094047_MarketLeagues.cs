using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mikeka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MarketLeagues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Markets now carry their own leagues: give existing market rows the account's leagues.
            migrationBuilder.Sql("""
                UPDATE "Accounts" a
                SET "Markets" = (
                    SELECT jsonb_agg(m || jsonb_build_object('Leagues', to_jsonb(a."Leagues")))
                    FROM jsonb_array_elements(a."Markets") AS m)
                WHERE a."Markets" IS NOT NULL AND jsonb_array_length(a."Markets") > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
