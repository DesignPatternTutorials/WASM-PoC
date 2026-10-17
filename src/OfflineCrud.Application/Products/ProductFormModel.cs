using System.ComponentModel.DataAnnotations;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Products;

/// <summary>
/// Mutable model bound to the create/edit form. Its data annotations mirror the rules in
/// <see cref="ProductDetails"/>, so the form catches invalid input before the domain rejects it.
/// </summary>
/// <remarks>
/// <c>EditForm</c> needs settable properties, which a value object can't offer, so the form edits this
/// model and converts it with <see cref="ToDetails"/> on submit. The rules run in the browser, which
/// matters here because DummyJSON doesn't validate anything (see <c>docs/api-notes.md</c>).
/// </remarks>
public sealed class ProductFormModel
{
    /// <summary>Short display name.</summary>
    [Required]
    [StringLength(ProductDetails.TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Longer free-text description.</summary>
    [Required]
    [StringLength(ProductDetails.DescriptionMaxLength)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Category slug.</summary>
    [Required(ErrorMessage = "Choose a category.")]
    [StringLength(ProductDetails.CategoryMaxLength)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Optional brand name.</summary>
    [StringLength(ProductDetails.BrandMaxLength)]
    public string? Brand { get; set; }

    /// <summary>List price before discount.</summary>
    // Attribute arguments can't be decimal, so the range is given as strings. Keep it in step with ProductDetails.MaxPrice.
    [Range(typeof(decimal), "0", "1000000")]
    public decimal Price { get; set; }

    /// <summary>Discount percentage, 0 to 100.</summary>
    [Display(Name = "Discount %")]
    [Range(typeof(decimal), "0", "100")]
    public decimal DiscountPercentage { get; set; }

    /// <summary>Units in stock.</summary>
    [Range(0, ProductDetails.MaxStock)]
    public int Stock { get; set; }

    /// <summary>Creates a form model pre-filled from existing details.</summary>
    public static ProductFormModel From(ProductDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        return new ProductFormModel
        {
            Title = details.Title,
            Description = details.Description,
            Category = details.Category,
            Brand = details.Brand,
            Price = details.Price,
            DiscountPercentage = details.DiscountPercentage,
            Stock = details.Stock,
        };
    }

    /// <summary>Converts the form values into validated domain details.</summary>
    /// <exception cref="ArgumentException">The values break a domain rule.</exception>
    public ProductDetails ToDetails()
    {
        return new ProductDetails(Title, Description, Category, Brand, Price, DiscountPercentage, Stock);
    }
}
