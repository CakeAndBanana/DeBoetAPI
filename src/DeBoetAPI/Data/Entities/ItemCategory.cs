namespace DeBoetAPI.Data.Entities;

/// <summary>
/// Places an item in a category. At most one row per item may carry
/// <see cref="IsPrimary"/> — enforced by a filtered unique index, not just by convention.
/// </summary>
public class ItemCategory
{
    public Guid ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    /// <summary>The category used for this item's breadcrumb.</summary>
    public bool IsPrimary { get; set; }
}
