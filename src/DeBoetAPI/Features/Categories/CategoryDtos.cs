using System.ComponentModel.DataAnnotations;

namespace DeBoetAPI.Features.Categories;

public sealed record CreateCategoryRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional — derived from <see cref="Name"/> when omitted.</summary>
    [MaxLength(200)]
    public string? Slug { get; init; }

    /// <summary>SEO meta title. Falls back to <see cref="Name"/> when blank.</summary>
    [MaxLength(200)]
    public string? MetaTitle { get; init; }

    /// <summary>SEO meta description. Optional.</summary>
    [MaxLength(2000)]
    public string? MetaDescription { get; init; }

    /// <summary>The main parent. Null creates a root category.</summary>
    public Guid? ParentId { get; init; }

    public int SortOrder { get; init; }

    /// <summary>Defaults to true so a newly created category is immediately visible.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>Whether the category appears in navigation. Defaults to true.</summary>
    public bool IsMenu { get; init; } = true;
}

public sealed record UpdateCategoryRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? Slug { get; init; }

    /// <summary>SEO meta title. Falls back to <see cref="Name"/> when blank.</summary>
    [MaxLength(200)]
    public string? MetaTitle { get; init; }

    /// <summary>SEO meta description. Optional.</summary>
    [MaxLength(2000)]
    public string? MetaDescription { get; init; }

    public int SortOrder { get; init; }

    public bool IsActive { get; init; } = true;

    public bool IsMenu { get; init; } = true;
}

/// <summary>Null <see cref="NewParentId"/> promotes the category to a root.</summary>
public sealed record MoveCategoryRequest
{
    public Guid? NewParentId { get; init; }
}

public sealed record AddCategoryLinkRequest
{
    [Required]
    public Guid ParentId { get; init; }
}

public sealed record CategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    string PathSlug,
    string MetaTitle,
    string? MetaDescription,
    Guid? ParentId,
    int Depth,
    int SortOrder,
    bool IsActive,
    bool IsMenu,
    Guid? CreatedById,
    /// <summary>Who last changed it. Full history is at /categories/{id}/history.</summary>
    Guid? UpdatedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CategoryDetailResponse(
    CategoryResponse Category,
    IReadOnlyList<CategoryResponse> Breadcrumb,
    IReadOnlyList<CategoryResponse> Children,
    IReadOnlyList<CategoryResponse> SecondaryParents);

public sealed record CategoryNode(
    Guid Id,
    string Name,
    string Slug,
    string PathSlug,
    string MetaTitle,
    string? MetaDescription,
    int Depth,
    int SortOrder,
    bool IsActive,
    bool IsMenu,
    IReadOnlyList<CategoryNode> Children);

public sealed record DeleteCategoryResponse(IReadOnlyList<Guid> PromotedChildIds);

public sealed record SetItemCategoriesRequest
{
    [Required]
    public IReadOnlyList<Guid> CategoryIds { get; init; } = [];

    public Guid? PrimaryCategoryId { get; init; }
}
