using System.ComponentModel.DataAnnotations;
using OfflineCrud.Application.Products;
using OfflineCrud.Application.Tests.Domain;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Products;

/// <summary>
/// The form's data annotations and the domain's rules are written separately, so these tests check they agree:
/// anything the form accepts must convert to <see cref="ProductDetails"/>, and the form must reject what the domain rejects.
/// </summary>
[TestFixture]
public class ProductFormModelTests
{
    [Test]
    public void ValuesAtTheDomainLimits_PassValidation_AndConvert()
    {
        ProductFormModel model = new()
        {
            Title = new string('t', ProductDetails.TitleMaxLength),
            Description = new string('d', ProductDetails.DescriptionMaxLength),
            Category = new string('c', ProductDetails.CategoryMaxLength),
            Brand = new string('b', ProductDetails.BrandMaxLength),
            Price = ProductDetails.MaxPrice,
            DiscountPercentage = 100m,
            Stock = ProductDetails.MaxStock,
        };

        Assert.Multiple(() =>
        {
            Assert.That(Validate(model), Is.Empty);
            Assert.That(model.ToDetails, Throws.Nothing);
        });
    }

    [Test]
    public void ValuesJustOverTheDomainLimits_FailValidation()
    {
        ProductFormModel model = new()
        {
            Title = new string('t', ProductDetails.TitleMaxLength + 1),
            Description = new string('d', ProductDetails.DescriptionMaxLength + 1),
            Category = new string('c', ProductDetails.CategoryMaxLength + 1),
            Brand = new string('b', ProductDetails.BrandMaxLength + 1),
            Price = ProductDetails.MaxPrice + 0.01m,
            DiscountPercentage = 100.01m,
            Stock = ProductDetails.MaxStock + 1,
        };

        IEnumerable<string> failedMembers = Validate(model).SelectMany(result => result.MemberNames);

        Assert.That(failedMembers, Is.EquivalentTo(new[]
        {
            nameof(ProductFormModel.Title),
            nameof(ProductFormModel.Description),
            nameof(ProductFormModel.Category),
            nameof(ProductFormModel.Brand),
            nameof(ProductFormModel.Price),
            nameof(ProductFormModel.DiscountPercentage),
            nameof(ProductFormModel.Stock),
        }));
    }

    [Test]
    public void WhitespaceOnlyRequiredFields_FailValidation()
    {
        ProductFormModel model = new() { Title = "   ", Description = "   ", Category = "   " };

        IEnumerable<string> failedMembers = Validate(model).SelectMany(result => result.MemberNames);

        Assert.That(failedMembers, Is.EquivalentTo(new[]
        {
            nameof(ProductFormModel.Title),
            nameof(ProductFormModel.Description),
            nameof(ProductFormModel.Category),
        }));
    }

    [Test]
    public void From_ThenToDetails_RoundTrips()
    {
        ProductDetails original = ProductDetailsTests.Create();

        Assert.That(ProductFormModel.From(original).ToDetails(), Is.EqualTo(original));
    }

    private static List<ValidationResult> Validate(ProductFormModel model)
    {
        List<ValidationResult> results = [];
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
