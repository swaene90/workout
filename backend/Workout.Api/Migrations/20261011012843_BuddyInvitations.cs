using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workout.Api.Migrations
{
    /// <inheritdoc />
    public partial class BuddyInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuddyConnections",
                columns: table => new
                {
                    FirstId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuddyConnections", x => new { x.FirstId, x.SecondId });
                    table.CheckConstraint("CK_BuddyConnection_Order", "\"FirstId\" < \"SecondId\"");
                    table.ForeignKey(
                        name: "FK_BuddyConnections_Members_FirstId",
                        column: x => x.FirstId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuddyConnections_Members_SecondId",
                        column: x => x.SecondId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BuddyInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InviterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Expires = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    AcceptedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuddyInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuddyInvitations_Members_AcceptedBy",
                        column: x => x.AcceptedBy,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuddyInvitations_Members_InviterId",
                        column: x => x.InviterId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "BuddyConnections",
                columns: new[] { "FirstId", "SecondId", "Created" },
                values: new object[] { new Guid("b2222222-2222-4222-8222-222222222222"), new Guid("e1111111-1111-4111-8111-111111111111"), new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "IX_BuddyConnections_SecondId",
                table: "BuddyConnections",
                column: "SecondId");

            migrationBuilder.CreateIndex(
                name: "IX_BuddyInvitations_AcceptedBy",
                table: "BuddyInvitations",
                column: "AcceptedBy");

            migrationBuilder.CreateIndex(
                name: "IX_BuddyInvitations_InviterId_Email",
                table: "BuddyInvitations",
                columns: new[] { "InviterId", "Email" },
                unique: true,
                filter: "\"Status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_BuddyInvitations_TokenHash",
                table: "BuddyInvitations",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuddyConnections");

            migrationBuilder.DropTable(
                name: "BuddyInvitations");
        }
    }
}
