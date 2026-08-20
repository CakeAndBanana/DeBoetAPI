namespace DeBoetAPI.Data.Entities;

public class CategoryLink
{
    public Guid ParentId { get; set; }

    public Category Parent { get; set; } = null!;

    public Guid ChildId { get; set; }

    public Category Child { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
