using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bluestaq.Notes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ManagedCollaboration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeys_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "ApiKeys" ("Id", "UserId", "KeyHash", "Name", "CreatedAt")
                SELECT
                    lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' ||
                    substr(lower(hex(randomblob(2))), 2) || '-' ||
                    substr('89ab', abs(random()) % 4 + 1, 1) ||
                    substr(lower(hex(randomblob(2))), 2) || '-' || lower(hex(randomblob(6))),
                    "Id", "ApiKeyHash", 'Development', CURRENT_TIMESTAMP
                FROM "Users"
                """);

            migrationBuilder.DropIndex(
                name: "IX_Users_ApiKeyHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApiKeyHash",
                table: "Users");

            migrationBuilder.CreateTable(
                name: "NotePermissions",
                columns: table => new
                {
                    NoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotePermissions", x => new { x.NoteId, x.UserId });
                    table.ForeignKey(
                        name: "FK_NotePermissions_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotePermissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UserId",
                table: "ApiKeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotePermissions_UserId",
                table: "NotePermissions",
                column: "UserId");

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE "NotesFts" USING fts5(
                    "Title", "Content", content='Notes', content_rowid='rowid'
                );
                INSERT INTO "NotesFts" (rowid, "Title", "Content")
                    SELECT rowid, "Title", "Content" FROM "Notes";
                CREATE TRIGGER "NotesFts_Insert" AFTER INSERT ON "Notes" BEGIN
                    INSERT INTO "NotesFts" (rowid, "Title", "Content")
                    VALUES (new.rowid, new."Title", new."Content");
                END;
                CREATE TRIGGER "NotesFts_Delete" AFTER DELETE ON "Notes" BEGIN
                    INSERT INTO "NotesFts" ("NotesFts", rowid, "Title", "Content")
                    VALUES ('delete', old.rowid, old."Title", old."Content");
                END;
                CREATE TRIGGER "NotesFts_Update" AFTER UPDATE OF "Title", "Content" ON "Notes" BEGIN
                    INSERT INTO "NotesFts" ("NotesFts", rowid, "Title", "Content")
                    VALUES ('delete', old.rowid, old."Title", old."Content");
                    INSERT INTO "NotesFts" (rowid, "Title", "Content")
                    VALUES (new.rowid, new."Title", new."Content");
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "NotesFts_Update";
                DROP TRIGGER IF EXISTS "NotesFts_Delete";
                DROP TRIGGER IF EXISTS "NotesFts_Insert";
                DROP TABLE IF EXISTS "NotesFts";
                """);

            migrationBuilder.DropTable(
                name: "NotePermissions");

            migrationBuilder.AddColumn<string>(
                name: "ApiKeyHash",
                table: "Users",
                type: "TEXT",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "Users"
                SET "ApiKeyHash" = (
                    SELECT "KeyHash"
                    FROM "ApiKeys"
                    WHERE "ApiKeys"."UserId" = "Users"."Id"
                    ORDER BY "CreatedAt", "Id"
                    LIMIT 1
                )
                """);

            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ApiKeyHash",
                table: "Users",
                column: "ApiKeyHash",
                unique: true);
        }
    }
}
