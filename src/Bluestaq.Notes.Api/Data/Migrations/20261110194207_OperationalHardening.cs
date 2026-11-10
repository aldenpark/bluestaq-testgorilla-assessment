using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bluestaq.Notes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperationalHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Teams",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "IntegrityHash",
                table: "SecurityAuditEvents",
                type: "TEXT",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.CreateTable(
                name: "ReadinessProbes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadinessProbes", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ReadinessProbes",
                columns: new[] { "Id", "CheckedAt" },
                values: new object[] { 1, new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadinessProbes");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "IntegrityHash",
                table: "SecurityAuditEvents");
        }
    }
}
