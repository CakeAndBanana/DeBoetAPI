namespace DeBoetAPI.Common;

public static class RateLimitPolicies
{
    public const string Auth = "auth";

    public const string Export = "export";

    /// <summary>
    /// The spreadsheet import, which reads a whole workbook and rewrites part of the tree from
    /// it. Slower and far more consequential than the export, so it gets a tighter window.
    /// </summary>
    public const string Import = "import";
}
