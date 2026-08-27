using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeBoetAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class CategoryRedirect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "redirect_to_id",
                table: "categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_categories_redirect_to_id",
                table: "categories",
                column: "redirect_to_id");

            migrationBuilder.AddForeignKey(
                name: "fk_categories_categories_redirect_to_id",
                table: "categories",
                column: "redirect_to_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_categories_categories_redirect_to_id",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "ix_categories_redirect_to_id",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "redirect_to_id",
                table: "categories");
        }
    }
}
