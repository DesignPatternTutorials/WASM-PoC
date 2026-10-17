namespace OfflineCrud.Domain.Products;

/// <summary>
/// The user-editable fields of a product, validated as a whole.
/// </summary>
/// <remarks>
/// A value object: two instances with the same values are equal, and an instance can't be changed after
/// construction. Editing a product means building a new <see cref="ProductDetails"/>, which guarantees
/// the rules below hold for every instance that exists.
/// </remarks>
public sealed record ProductDetails
{
    /// <summary>Maximum length of <see cref="Title"/>.</summary>
    public const int TitleMaxLength = 100;

    /// <summary>Maximum length of <see cref="Description"/>.</summary>
    public const int DescriptionMaxLength = 1000;

    /// <summary>Maximum length of <see cref="Category"/>.</summary>
    public const int CategoryMaxLength = 50;

    /// <summary>Maximum length of <see cref="Brand"/>.</summary>
    public const int BrandMaxLength = 50;

    /// <summary>Highest allowed <see cref="Price"/>.</summary>
    public const decimal MaxPrice = 1_000_000m;

    /// <summary>Highest allowed <see cref="Stock"/>.</summary>
    public const int MaxStock = 1_000_000;

    /// <summary>
    /// Creates a validated set of product details. Text values are trimmed, and a blank brand becomes <see langword="null"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A required text value is blank or too long.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A numeric value is outside its allowed range.</exception>
    public ProductDetails(
        string title,
        string description,
        string category,
        string? brand,
        decimal price,
        decimal discountPercentage,
        int stock)
    {
        Title = RequiredText(title, TitleMaxLength, nameof(title));
        Description = RequiredText(description, DescriptionMaxLength, nameof(description));
        Category = RequiredText(category, CategoryMaxLength, nameof(category));
        Brand = OptionalText(brand, BrandMaxLength, nameof(brand));

        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(price, MaxPrice);
        ArgumentOutOfRangeException.ThrowIfNegative(discountPercentage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(discountPercentage, 100m);
        ArgumentOutOfRangeException.ThrowIfNegative(stock);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(stock, MaxStock);

        Price = price;
        DiscountPercentage = discountPercentage;
        Stock = stock;
    }

    /// <summary>Short display name.</summary>
    public string Title { get; }

    /// <summary>Longer free-text description.</summary>
    public string Description { get; }

    /// <summary>Category slug, e.g. <c>smartphones</c>.</summary>
    public string Category { get; }

    /// <summary>Brand name, or <see langword="null"/> when the product has none.</summary>
    public string? Brand { get; }

    /// <summary>List price before discount.</summary>
    public decimal Price { get; }

    /// <summary>Discount as a percentage of <see cref="Price"/>, from 0 to 100.</summary>
    public decimal DiscountPercentage { get; }

    /// <summary>Units in stock.</summary>
    public int Stock { get; }

    private static string RequiredText(string value, int maxLength, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return OptionalText(value, maxLength, paramName)!;
    }

    private static string? OptionalText(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"Must be {maxLength} characters or fewer.", paramName);
        }

        return trimmed;
    }
}
