using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mikeka.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BalanceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BalanceHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceHistory_AccountId_At",
                table: "BalanceHistory",
                columns: new[] { "AccountId", "At" });

            // Start the history from what is already known: balances stored on slips, then each account's last balance.
            migrationBuilder.Sql("""
                INSERT INTO "BalanceHistory" ("AccountId", "At", "Balance")
                SELECT "AccountId", "CreatedAt", "BalanceBefore" FROM "Slips" WHERE "BalanceBefore" IS NOT NULL
                UNION ALL
                SELECT "AccountId", "SettledAt", "BalanceAfter" FROM "Slips" WHERE "BalanceAfter" IS NOT NULL AND "SettledAt" IS NOT NULL
                UNION ALL
                SELECT "Id", now(), "LastBalance" FROM "Accounts" WHERE "LastBalance" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BalanceHistory");
        }
    }
}
