using DeBoetAPI.Data;
using DeBoetAPI.Data.Entities;

namespace DeBoetAPI.Common;

/// <summary>
/// Records changes to the audit trail.
/// </summary>
public sealed class AuditLog(AppDbContext db, CurrentUser currentUser)
{
    /// <summary>
    /// Set for the duration of a bulk operation, and appended to every summary it writes. A
    /// spreadsheet import produces ordinary per-category entries — which is what makes them show
    /// up in the history like any other edit — and this is what tells a reader they arrived forty
    /// at a time rather than by hand.
    /// </summary>
    public string? Context { get; set; }

    public void Record(
        AuditedEntityType entityType,
        Guid entityId,
        string entityName,
        AuditAction action,
        string? entityPath = null,
        string? summary = null)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            EntityType = entityType,
            EntityId = entityId,
            EntityName = entityName,
            EntityPath = entityPath,
            Action = action,
            ChangedById = currentUser.Id,
            ChangedByName = currentUser.Name,
            Summary = Context is null ? summary : $"{summary} ({Context})".TrimStart(),
        });
    }

    public void RecordCategory(Category category, AuditAction action, string? summary = null) =>
        Record(AuditedEntityType.Category, category.Id, category.Name, action, category.PathSlug, summary);

    public void RecordUser(AppUser user, AuditAction action, string? summary = null) =>
        Record(AuditedEntityType.User, user.Id, user.UserName ?? user.Email ?? user.Id.ToString(),
            action, summary: summary);

    public void RecordItem(Item item, AuditAction action, string? summary = null) =>
        Record(AuditedEntityType.Item, item.Id, item.Name, action, summary: summary);

    public Guid? ActorId => currentUser.Id;

    public static string? Diff(params (string Field, object? Before, object? After)[] fields)
    {
        var changed = fields
            .Where(f => !Equals(f.Before, f.After))
            .Select(f => $"{f.Field}: {Render(f.Before)} -> {Render(f.After)}")
            .ToList();

        return changed.Count == 0 ? null : string.Join("; ", changed);

        static string Render(object? value) => value switch
        {
            null => "(none)",
            string s when s.Length == 0 => "(empty)",
            _ => value.ToString() ?? "(none)",
        };
    }
}
