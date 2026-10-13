using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCryptocurrencySeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep legacy currencies when existing conversion history references them.
            migrationBuilder.Sql("""
                DELETE FROM currencies AS c
                WHERE c.code IN ('BTC', 'ETH', 'USDT')
                  AND NOT EXISTS (
                    SELECT 1 FROM conversions AS h
                    WHERE h.from_currency = c.code OR h.to_currency = c.code
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO currencies (code, kind, name)
                VALUES ('BTC', 'crypto', 'Bitcoin'),
                       ('ETH', 'crypto', 'Ethereum'),
                       ('USDT', 'crypto', 'Tether')
                ON CONFLICT (code) DO NOTHING;
                """);
        }
    }
}
