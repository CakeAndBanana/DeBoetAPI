namespace DeBoetAPI.Data.Entities;

/// <summary>
/// A node in the taxonomy. <see cref="ParentId"/> is the single <em>main</em> parent and
/// defines the canonical path and breadcrumb; additional placements live in
/// <see cref="CategoryLink"/> and never affect the path.
/// </summary>
public class Category
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// This category's own segment, e.g. <c>christmaslights</c>. Contains no dashes — the dash
    /// is reserved as the level separator in <see cref="PathSlug"/>, so allowing one here would
    /// make that path ambiguous. Unique among siblings (enforced via <see cref="PathSlug"/>).
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// The full route, ancestors included: <c>christmas-christmaslights-icelights</c>.
    /// Globally unique and the canonical way to address a category in a URL. Maintained
    /// alongside <see cref="Path"/>, and rewritten across the subtree whenever this category
    /// is renamed or moved.
    /// </summary>
    public string PathSlug { get; set; } = string.Empty;

    /// <summary>
    /// Meta title for Search Engines. Not required, but if not provided, the <see cref="Name"/> will be used as the meta title.
    /// </summary>
    public string? MetaTitle { get; set; } = string.Empty;

    /// <summary>
    /// Optional
    /// </summary>
    public string? MetaDescription { get; set; }

    /// <summary>The main parent. Null means this is a root category.</summary>
    public Guid? ParentId { get; set; }

    public Category? Parent { get; set; }

    public ICollection<Category> Children { get; set; } = [];

    /// <summary>
    /// Materialised ancestor chain, <c>/{ancestorId}/…/{ownId}/</c>. Derived from
    /// <see cref="ParentId"/> and rewritten whenever the node moves. Stores ids rather than
    /// slugs so renaming a category never touches its subtree.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Zero for roots. Kept alongside <see cref="Path"/> for cheap depth filtering.</summary>
    public int Depth { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Gives out if the category is active or not
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Indicates if the category is shown in the menu or not
    /// </summary>
    public bool IsMenu { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Extra parents this category also appears under.</summary>
    public ICollection<CategoryLink> SecondaryParentLinks { get; set; } = [];
}
