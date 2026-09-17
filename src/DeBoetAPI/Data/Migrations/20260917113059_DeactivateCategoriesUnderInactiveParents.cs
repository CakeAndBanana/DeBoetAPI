using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeBoetAPI.Data.Migrations
{
    /// <summary>
    /// Brings the stored tree in line with the rule the API now enforces: a category is inactive
    /// whenever anything above it is. Nothing about the schema changes — this is the one-off
    /// repair of rows written before the rule existed, which would otherwise stay active and
    /// unreachable until somebody happened to toggle the category above them.
    /// </summary>
    public partial class DeactivateCategoriesUnderInactiveParents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Matched on the materialised path, so one statement covers every level at once: a
            // descendant's path is its ancestor's path with more on the end.
            migrationBuilder.Sql(
                """
                UPDATE categories AS c
                SET is_active = false,
                    updated_at = now()
                WHERE c.is_active
                  AND EXISTS (
                      SELECT 1
                      FROM categories AS a
                      WHERE a.id <> c.id
                        AND NOT a.is_active
                        AND c.path LIKE a.path || '%'
                  )
                """);
        }

        /// <summary>
        /// Deliberately empty. Which categories were switched off here is not recorded anywhere
        /// this could read back, and switching them all on again would publish sections that were
        /// never meant to be published.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
