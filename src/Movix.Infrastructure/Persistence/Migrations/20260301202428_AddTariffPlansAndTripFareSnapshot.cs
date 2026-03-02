using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Movix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTariffPlansAndTripFareSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaseFareUsed",
                table: "trips",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DistanceKm",
                table: "trips",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DurationMinutes",
                table: "trips",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumFareUsed",
                table: "trips",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricePerKmUsed",
                table: "trips",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricePerMinuteUsed",
                table: "trips",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TariffPlanIdUsed",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tariff_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BaseFare = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PricePerKm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PricePerMinute = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MinimumFare = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tariff_plans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tariff_plans_TenantId_IsActive",
                table: "tariff_plans",
                columns: new[] { "TenantId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tariff_plans");

            migrationBuilder.DropColumn(
                name: "BaseFareUsed",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "DistanceKm",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "MinimumFareUsed",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "PricePerKmUsed",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "PricePerMinuteUsed",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "TariffPlanIdUsed",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "trips");
        }
    }
}
