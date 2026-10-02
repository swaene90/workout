using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workout.Api.Migrations
{
    /// <inheritdoc />
    public partial class MemberAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "Members",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "light");

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "Members",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "green");

            migrationBuilder.UpdateData(
                table: "Members",
                keyColumn: "Id",
                keyValue: new Guid("b2222222-2222-4222-8222-222222222222"),
                columns: new[] { "Mode", "Theme" },
                values: new object[] { "light", "green" });

            migrationBuilder.UpdateData(
                table: "Members",
                keyColumn: "Id",
                keyValue: new Guid("e1111111-1111-4111-8111-111111111111"),
                columns: new[] { "Mode", "Theme" },
                values: new object[] { "light", "green" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "Members");
        }
    }
}
