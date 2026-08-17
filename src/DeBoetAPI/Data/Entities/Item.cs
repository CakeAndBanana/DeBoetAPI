namespace DeBoetAPI.Data.Entities;

/// <summary>
/// Neutral demo resource. It exists to establish the owned-CRUD pattern — rename or replace
/// it once the real domain is known.
/// </summary>
public class Item
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid OwnerId { get; set; }

    public AppUser Owner { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ItemCategory> Categories { get; set; } = [];
}
