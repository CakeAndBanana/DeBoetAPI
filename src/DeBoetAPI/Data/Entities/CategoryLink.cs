namespace DeBoetAPI.Data.Entities;

/// <summary>
/// A secondary placement: <see cref="ChildId"/> also appears under <see cref="ParentId"/>,
/// without that affecting its canonical path. Kept separate from
/// <see cref="Category.ParentId"/> so the main tree stays a plain hierarchy that cross-links
/// cannot confuse.
/// </summary>
public class CategoryLink
{
    public Guid ParentId { get; set; }

    public Category Parent { get; set; } = null!;

    public Guid ChildId { get; set; }

    public Category Child { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
