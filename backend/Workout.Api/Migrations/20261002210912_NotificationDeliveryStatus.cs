using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workout.Api.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDeliveryStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Sent",
                table: "NotificationDeliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sent",
                table: "NotificationDeliveries");
        }
    }
}
