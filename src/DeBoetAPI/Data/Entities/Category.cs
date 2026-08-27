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

    public string Slug { get; set; } = string.Empty;

    public string PathSlug { get; set; } = string.Empty;

    public string? MetaTitle { get; set; }

    public string? MetaDescription { get; set; }

    public Guid? ParentId { get; set; }

    public Category? Parent { get; set; }

    public ICollection<Category> Children { get; set; } = [];

    public string Path { get; set; } = string.Empty;

    public int Depth { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public bool IsMenu { get; set; }

    public Guid? CreatedById { get; set; }

    public Guid? UpdatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Another category this one forwards to, or null for an ordinary category.
    /// <para>
    /// For a node that exists to organise the tree rather than to be landed on: "Christmas
    /// Decorations" groups things but has nothing of its own to show, so its address should
    /// take a visitor to "Christmas Trees" instead of a bare listing. The API records the
    /// intent; the website turns it into an HTTP redirect.
    /// </para>
    /// Kept to a single hop — a target may not itself redirect — so a consumer never has to
    /// follow a chain, and a cycle cannot be built in the first place.
    /// </summary>
    public Guid? RedirectToId { get; set; }

    public Category? RedirectTo { get; set; }

    /// <summary>Extra parents this category also appears under.</summary>
    public ICollection<CategoryLink> SecondaryParentLinks { get; set; } = [];
}
