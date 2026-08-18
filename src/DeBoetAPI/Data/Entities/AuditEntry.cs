namespace DeBoetAPI.Data.Entities;

/// <summary>What kind of record a change was made to. Add a member per audited entity.</summary>
public enum AuditedEntityType
{
    Category,
    Item,
}

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
    Moved,
    ChildPromoted,
    LinkAdded,
    LinkRemoved,
    CategoriesAssigned,
}

/// <summary>
/// One immutable record of a change, for every audited entity type rather than per-table.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public AuditedEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    /// <summary>The subject's display name at the moment of the change.</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>Extra identifier where one exists — a category's full route, say. Null for items.</summary>
    public string? EntityPath { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>Null for changes made outside a request: startup seeding, migrations.</summary>
    public Guid? ChangedById { get; set; }

    /// <summary>Snapshotted so the trail stays readable after an account is removed.</summary>
    public string ChangedByName { get; set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Human-readable summary of what changed, e.g. "name: Old -> New".</summary>
    public string? Summary { get; set; }
}
