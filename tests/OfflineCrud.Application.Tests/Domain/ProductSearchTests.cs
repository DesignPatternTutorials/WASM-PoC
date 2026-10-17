using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Domain;

[TestFixture]
public class ProductSearchTests
{
    private static readonly ProductDetails Phone = ProductDetailsTests.Create(
        title: "iPhone 13 Pro", description: "A phone with a great camera.", category: "smartphones", brand: "Apple");

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void BlankQuery_MatchesEverything(string? query)
    {
        Assert.That(ProductSearch.Matches(Phone, query), Is.True);
    }

    [TestCase("iphone", TestName = "Title, ignoring case")]
    [TestCase("APPLE", TestName = "Brand")]
    [TestCase("smartph", TestName = "Category, partial")]
    [TestCase("camera", TestName = "Description")]
    [TestCase("apple  camera", TestName = "Every word, across fields")]
    public void MatchingQuery_IsFound(string query)
    {
        Assert.That(ProductSearch.Matches(Phone, query), Is.True);
    }

    [TestCase("samsung")]
    [TestCase("apple samsung")]
    public void QueryWithAWordThatDoesNotAppear_IsNotFound(string query)
    {
        Assert.That(ProductSearch.Matches(Phone, query), Is.False);
    }

    [Test]
    public void ProductWithoutBrand_StillMatchesOtherFields()
    {
        ProductDetails noBrand = ProductDetailsTests.Create(title: "Plain mug", brand: null);

        Assert.That(ProductSearch.Matches(noBrand, "mug"), Is.True);
    }
}
