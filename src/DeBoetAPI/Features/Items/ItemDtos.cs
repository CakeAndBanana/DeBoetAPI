using System.ComponentModel.DataAnnotations;

namespace DeBoetAPI.Features.Items;

public sealed record CreateItemRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; init; }
}

public sealed record UpdateItemRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; init; }
}

public sealed record ItemCategoryResponse(Guid CategoryId, string Name, string Slug, bool IsPrimary);

public sealed record ItemResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
