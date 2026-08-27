namespace DeBoetAPI.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class Paging
{
    /// <summary>
    /// The most a caller may ask for in one page.
    /// <para>
    /// 250 because the change history is read a screenful at a time and the alternative is
    /// paging through thousands of rows twenty at a time. These are small, flat records; the
    /// cap exists to stop someone asking for the entire table, not to keep responses tiny.
    /// </para>
    /// </summary>
    public const int MaxPageSize = 250;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 20, 1, MaxPageSize));
}
