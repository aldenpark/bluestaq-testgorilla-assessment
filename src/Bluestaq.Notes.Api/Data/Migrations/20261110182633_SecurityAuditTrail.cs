using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bluestaq.Notes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SecurityAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecurityAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorCredentialId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEvents_ActorUserId_OccurredAt",
                table: "SecurityAuditEvents",
                columns: new[] { "ActorUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEvents_OccurredAt_Id",
                table: "SecurityAuditEvents",
                columns: new[] { "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEvents_TargetId",
                table: "SecurityAuditEvents",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEvents_TeamId_OccurredAt",
                table: "SecurityAuditEvents",
                columns: new[] { "TeamId", "OccurredAt" });

            migrationBuilder.Sql("""
                CREATE TRIGGER "SecurityAuditEvents_NoUpdate"
                BEFORE UPDATE ON "SecurityAuditEvents"
                BEGIN
                    SELECT RAISE(ABORT, 'Security audit events are append-only');
                END;
                CREATE TRIGGER "SecurityAuditEvents_NoDelete"
                BEFORE DELETE ON "SecurityAuditEvents"
                BEGIN
                    SELECT RAISE(ABORT, 'Security audit events are append-only');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "SecurityAuditEvents_NoUpdate";
                DROP TRIGGER IF EXISTS "SecurityAuditEvents_NoDelete";
                """);

            migrationBuilder.DropTable(
                name: "SecurityAuditEvents");
        }
    }
}
