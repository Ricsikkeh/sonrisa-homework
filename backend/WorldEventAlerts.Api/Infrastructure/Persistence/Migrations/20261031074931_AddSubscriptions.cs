using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldEventAlerts.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    EventTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<bool>(type: "boolean", nullable: false),
                    Slack = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.CheckConstraint("CK_Subscriptions_Channels", "\"Email\" OR \"Slack\"");
                    table.ForeignKey(
                        name: "FK_Subscriptions_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_EventTypes_EventTypeId",
                        column: x => x.EventTypeId,
                        principalTable: "EventTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_EventTypeId",
                table: "Subscriptions",
                column: "EventTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_OwnerId_EventTypeId",
                table: "Subscriptions",
                columns: new[] { "OwnerId", "EventTypeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Subscriptions");
        }
    }
}
