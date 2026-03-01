using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Movix.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxEventIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "outbox_messages",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_EventId",
                table: "outbox_messages",
                column: "EventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_EventId",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "outbox_messages");
        }
    }
}
