using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Movix.Infrastructure.Persistence.Migrations
{
    public partial class AddTariffPlanPriorityAndWindow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "tariff_plans",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveUntilUtc",
                table: "tariff_plans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.DropIndex(
                name: "IX_tariff_plans_TenantId_IsActive",
                table: "tariff_plans");

            migrationBuilder.CreateIndex(
                name: "IX_tariff_plans_Applicable",
                table: "tariff_plans",
                columns: new[] { "TenantId", "IsActive", "Priority", "EffectiveFromUtc" },
                filter: "\"IsActive\" = true");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tariff_plans_Applicable",
                table: "tariff_plans");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "tariff_plans");

            migrationBuilder.DropColumn(
                name: "EffectiveUntilUtc",
                table: "tariff_plans");

            migrationBuilder.CreateIndex(
                name: "IX_tariff_plans_TenantId_IsActive",
                table: "tariff_plans",
                columns: new[] { "TenantId", "IsActive" });
        }
    }
}
