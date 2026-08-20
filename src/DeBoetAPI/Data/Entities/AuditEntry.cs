namespace DeBoetAPI.Data.Entities;

/// <summary>What kind of record a change was made to. Add a member per audited entity.</summary>
public enum AuditedEntityType
{
    Category,
    Item,
    User,
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
    PasswordReset,
    PasswordChanged,
    RolesChanged,
    Disabled,
    Enabled,
}

/// <summary>
/// One immutable record of a change, for every audited entity type rather than per-table.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public AuditedEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    public string EntityName { get; set; } = string.Empty;

    public string? EntityPath { get; set; }

    public AuditAction Action { get; set; }

    public Guid? ChangedById { get; set; }

    public string ChangedByName { get; set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? Summary { get; set; }
}
