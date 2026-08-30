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

    /// <summary>
    /// Another category this one forwards to. Null for an ordinary category, and refused if
    /// the target already forwards somewhere itself.
    /// </summary>
    public Guid? RedirectToId { get; init; }

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

    /// <summary>See <see cref="CreateCategoryRequest.RedirectToId"/>. Null clears it.</summary>
    public Guid? RedirectToId { get; init; }

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

    public int? Position { get; init; }
}

/// <summary>
/// Adds a secondary parent to a category. The category will then appear under both parents in the tree.
/// </summary>
public sealed record AddCategoryLinkRequest
{
    [Required]
    public Guid ParentId { get; init; }
}

/// <summary>
/// Category response DTO used for returning category information to the client.
/// </summary>
/// <param name="Id">The category ID.</param>
/// <param name="Name">The category name.</param>
/// <param name="Slug">The category slug.</param>
/// <param name="PathSlug">The category path slug.</param>
/// <param name="MetaTitle">The SEO meta title.</param>
/// <param name="MetaDescription">The SEO meta description.</param>
/// <param name="RedirectToId">The ID of the category this one forwards to.</param>
/// <param name="ParentId">The ID of the parent category.</param>
/// <param name="Depth">The depth of the category in the tree.</param>
/// <param name="SortOrder">The sort order of the category.</param>
/// <param name="IsActive">Indicates if the category is active.</param>
/// <param name="IsMenu">Indicates if the category appears in the menu.</param>
/// <param name="CreatedById">The ID of the user who created the category.</param>
/// <param name="UpdatedById">The ID of the user who last updated the category.</param>
/// <param name="CreatedAt">The date and time the category was created.</param>
/// <param name="UpdatedAt">The date and time the category was last updated.</param>
public sealed record CategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    string PathSlug,
    string? MetaTitle,
    string? MetaDescription,
    /// <summary>Set when this category forwards to another one.</summary>
    Guid? RedirectToId,
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

/// <summary>
/// Category detail response DTO used for returning detailed category information to the client, including breadcrumb, children, secondary parents, and optional redirect information.
/// </summary>
/// <param name="Category">The main category information.</param>
/// <param name="Breadcrumb">The breadcrumb trail for the category.</param>
/// <param name="Children">The child categories.</param>
/// <param name="SecondaryParents">The secondary parent categories.</param>
/// <param name="RedirectTo">The category this one forwards to, if applicable.</param>
public sealed record CategoryDetailResponse(
    CategoryResponse Category,
    IReadOnlyList<CategoryResponse> Breadcrumb,
    IReadOnlyList<CategoryResponse> Children,
    IReadOnlyList<CategoryResponse> SecondaryParents,
    CategoryResponse? RedirectTo = null);

/// <summary>
/// Category node DTO used for returning category tree information to the client, including children nodes.
/// </summary>
/// <param name="Id">The category ID.</param>
/// <param name="Name">The category name.</param>
/// <param name="Slug">The category slug.</param>
/// <param name="PathSlug">The category path slug.</param>
/// <param name="MetaTitle">The SEO meta title.</param>
/// <param name="MetaDescription">The SEO meta description.</param>
/// <param name="RedirectToId">The ID of the category this one forwards to.</param>
/// <param name="Depth">The depth of the category in the tree.</param>
/// <param name="SortOrder">The sort order of the category.</param>
/// <param name="IsActive">Indicates if the category is active.</param>
/// <param name="IsMenu">Indicates if the category appears in the menu.</param>
/// <param name="Children">The child categories.</param>
public sealed record CategoryNode(
    Guid Id,
    string Name,
    string Slug,
    string PathSlug,
    string? MetaTitle,
    string? MetaDescription,
    Guid? RedirectToId,
    int Depth,
    int SortOrder,
    bool IsActive,
    bool IsMenu,
    IReadOnlyList<CategoryNode> Children);

/// <summary>
/// Delete catergory response DTO used for returning information about promoted child categories after a category deletion.
/// </summary>
/// <param name="PromotedChildIds"></param>
public sealed record DeleteCategoryResponse(IReadOnlyList<Guid> PromotedChildIds);

/// <summary>
/// Set item categories request DTO used for assigning categories to an item, including a list of category IDs and an optional primary category ID.
/// Not really used yet as items aren't a thing. could be WIP thing to do later
/// </summary>
public sealed record SetItemCategoriesRequest
{
    [Required]
    public IReadOnlyList<Guid> CategoryIds { get; init; } = [];

    public Guid? PrimaryCategoryId { get; init; }
}

/// <summary>One row of an import, as the panel shows it.</summary>
/// <param name="Sheet">The worksheet it came from, so it can be found again.</param>
/// <param name="Row">The row number in that sheet.</param>
/// <param name="State">Changed, Unchanged, Stale, Refused or Blocked.</param>
/// <param name="Changes">The fields that differ, worded as the change history words them.</param>
/// <param name="Reason">Why it will not be applied, when it will not be.</param>
public sealed record ImportRowResponse(
    string Sheet,
    int Row,
    Guid Id,
    string Name,
    string State,
    IReadOnlyList<string> Changes,
    string? Reason);

/// <summary>
/// What a preview or an applied import reports back.
///
/// Only the rows that need a decision are listed. A workbook is mostly rows nobody touched, and
/// sending several hundred of them back so the panel can count them and throw them away helps no
/// one — the count is the only part of "unchanged" worth knowing.
/// </summary>
public sealed record ImportResponse(
    int Changed,
    int Unchanged,
    int Stale,
    int Refused,
    int Blocked,
    int Applied,
    IReadOnlyList<ImportRowResponse> Rows)
{
    public static ImportResponse From(ImportOutcome outcome)
    {
        var plan = outcome.Plan;

        if (plan is null)
        {
            return new ImportResponse(0, 0, 0, 0, 0, 0, []);
        }

        var rows = plan.Rows
            .Where(r => r.State is not (ImportRowState.Unchanged or ImportRowState.Ignored))
            .Select(r => new ImportRowResponse(
                r.Sheet, r.Number, r.Id, r.Name, r.State.ToString(), r.Changes, r.Reason))
            .ToList();

        return new ImportResponse(
            plan.Changed, plan.Unchanged, plan.Stale, plan.Refused, plan.Blocked,
            outcome.Applied, rows);
    }
}
