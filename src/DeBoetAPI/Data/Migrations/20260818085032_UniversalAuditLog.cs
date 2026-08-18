using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeBoetAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniversalAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_id",
                table: "categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_id",
                table: "categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entity_path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    changed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_by_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_changed_at",
                table: "audit_entries",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_changed_by_id",
                table: "audit_entries",
                column: "changed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_type_entity_id_changed_at",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropColumn(
                name: "created_by_id",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "updated_by_id",
                table: "categories");
        }
    }
}
