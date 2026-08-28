using System.ComponentModel.DataAnnotations;

namespace DeBoetAPI.Features.Items;

/// <summary>
/// Creates an item request DTO used for creating new items in the system.
/// </summary>
public sealed record CreateItemRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; init; }
}

/// <summary>
/// Updates an existing item.
/// </summary>
public sealed record UpdateItemRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; init; }
}

/// <summary>
/// Item category response DTO used for returning item category information to the client.
/// </summary>
/// <param name="CategoryId"></param>
/// <param name="Name"></param>
/// <param name="Slug"></param>
/// <param name="IsPrimary"></param>
public sealed record ItemCategoryResponse(Guid CategoryId, string Name, string Slug, bool IsPrimary);

/// <summary>
/// Item response DTO used for returning item information to the client.
/// </summary>
/// <param name="Id">The unique identifier of the item.</param>
/// <param name="Name">The name of the item.</param>
/// <param name="Description">The description of the item.</param>
/// <param name="CreatedAt">The date and time when the item was created.</param>
/// <param name="UpdatedAt">The date and time when the item was last updated.</param>
public sealed record ItemResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
