using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeBoetAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class CategoryMetaDescriptionAndMenuFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "description",
                table: "categories",
                newName: "meta_description");

            migrationBuilder.AlterColumn<string>(
                name: "meta_title",
                table: "categories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            // Backfilled true so categories that already exist behave like newly created ones
            // (CreateCategoryRequest.IsMenu also defaults to true). Left at false, every
            // existing category would silently disappear from navigation on deploy.
            migrationBuilder.AddColumn<bool>(
                name: "is_menu",
                table: "categories",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_menu",
                table: "categories");

            migrationBuilder.RenameColumn(
                name: "meta_description",
                table: "categories",
                newName: "description");

            migrationBuilder.AlterColumn<string>(
                name: "meta_title",
                table: "categories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
