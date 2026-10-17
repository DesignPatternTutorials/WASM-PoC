namespace OfflineCrud.Application.Common;

/// <summary>One page of a larger result set.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items on this page.</param>
/// <param name="TotalCount">How many items exist across all pages.</param>
/// <param name="Skip">How many items come before this page.</param>
/// <param name="Take">The requested page size.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Skip, int Take)
{
    /// <summary>The 1-based number of this page.</summary>
    public int PageNumber => Take == 0 ? 1 : (Skip / Take) + 1;

    /// <summary>The number of pages, at least 1 so an empty result still has a page to show.</summary>
    public int PageCount => Take == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Take));

    /// <summary>Whether a page follows this one.</summary>
    public bool HasNextPage => Skip + Items.Count < TotalCount;

    /// <summary>Whether a page precedes this one.</summary>
    public bool HasPreviousPage => Skip > 0;
}
