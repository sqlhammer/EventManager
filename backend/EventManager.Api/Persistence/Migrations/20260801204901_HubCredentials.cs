using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventManager.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HubCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "IngestedByCredentialId",
                table: "Events",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HubCredentials",
                columns: table => new
                {
                    CredentialId = table.Column<long>(type: "bigint", nullable: false),
                    EventScopeId = table.Column<long>(type: "bigint", nullable: false),
                    KeyHash = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IssuedByAccountId = table.Column<long>(type: "bigint", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubCredentials", x => x.CredentialId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HubCredentials_EventScopeId",
                table: "HubCredentials",
                column: "EventScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_HubCredentials_KeyHash",
                table: "HubCredentials",
                column: "KeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HubCredentials");

            migrationBuilder.DropColumn(
                name: "IngestedByCredentialId",
                table: "Events");
        }
    }
}
