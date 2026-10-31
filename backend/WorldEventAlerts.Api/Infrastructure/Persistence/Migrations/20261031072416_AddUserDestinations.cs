using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldEventAlerts.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserDestinations",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    SlackLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDestinations", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_UserDestinations_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDestinations");
        }
    }
}
