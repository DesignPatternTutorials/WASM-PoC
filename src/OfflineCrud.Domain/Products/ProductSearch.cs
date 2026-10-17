namespace OfflineCrud.Domain.Products;

/// <summary>The rule for matching products against free-text search on this device.</summary>
public static class ProductSearch
{
    /// <summary>
    /// Whether every word in <paramref name="query"/> appears (case-insensitively) in the product's title, brand,
    /// category or description. A blank query matches everything.
    /// </summary>
    /// <remarks>
    /// This approximates DummyJSON's own search, which isn't documented. Results found offline can differ a little
    /// from the ones the server returns online.
    /// </remarks>
    public static bool Matches(ProductDetails details, string? query)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string[] words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.All(word =>
            Contains(details.Title, word)
            || Contains(details.Brand, word)
            || Contains(details.Category, word)
            || Contains(details.Description, word));
    }

    private static bool Contains(string? text, string word) =>
        text is not null && text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
