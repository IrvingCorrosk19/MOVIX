using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Movix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RowVersionPostgresDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "vehicles",
                type: "bytea",
                nullable: false,
                defaultValueSql: "gen_random_bytes(8)",
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "users",
                type: "bytea",
                nullable: false,
                defaultValueSql: "gen_random_bytes(8)",
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "trips",
                type: "bytea",
                nullable: false,
                defaultValueSql: "gen_random_bytes(8)",
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "tenants",
                type: "bytea",
                nullable: false,
                defaultValueSql: "gen_random_bytes(8)",
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "payments",
                type: "bytea",
                nullable: false,
                defaultValueSql: "gen_random_bytes(8)",
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "drivers",
                type: "bytea",
                nullable: false,
                defaultValueSql: "gen_random_bytes(8)",
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "driver_availability",
                type: "bytea",
                nullable: false,
                defaultValueSql: "gen_random_bytes(8)",
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "vehicles",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValueSql: "gen_random_bytes(8)");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "users",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValueSql: "gen_random_bytes(8)");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "trips",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValueSql: "gen_random_bytes(8)");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "tenants",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValueSql: "gen_random_bytes(8)");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "payments",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValueSql: "gen_random_bytes(8)");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "drivers",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValueSql: "gen_random_bytes(8)");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                table: "driver_availability",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldDefaultValueSql: "gen_random_bytes(8)");
        }
    }
}
