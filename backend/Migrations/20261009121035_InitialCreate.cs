using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currencies", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "conversions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_currency = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    to_currency = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(38,18)", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(38,18)", nullable: false),
                    result = table.Column<decimal>(type: "numeric(38,18)", nullable: false),
                    rate_source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    rate_as_of_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversions", x => x.id);
                    table.ForeignKey(
                        name: "FK_conversions_currencies_from_currency",
                        column: x => x.from_currency,
                        principalTable: "currencies",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conversions_currencies_to_currency",
                        column: x => x.to_currency,
                        principalTable: "currencies",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "currencies",
                columns: new[] { "code", "kind", "name" },
                values: new object[,]
                {
                    { "BTC", "crypto", "Bitcoin" },
                    { "ETH", "crypto", "Ethereum" },
                    { "EUR", "fiat", "Euro" },
                    { "GEL", "fiat", "Georgian lari" },
                    { "RUB", "fiat", "Russian ruble" },
                    { "USD", "fiat", "US dollar" },
                    { "USDT", "crypto", "Tether" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversions_created_at_utc",
                table: "conversions",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_conversions_from_currency",
                table: "conversions",
                column: "from_currency");

            migrationBuilder.CreateIndex(
                name: "IX_conversions_to_currency",
                table: "conversions",
                column: "to_currency");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversions");

            migrationBuilder.DropTable(
                name: "currencies");
        }
    }
}
