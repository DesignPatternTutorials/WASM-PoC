using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Domain;

[TestFixture]
public class ProductTests
{
    [TestCase(100, 10, 90)]
    [TestCase(9.99, 10.48, 8.94)]
    [TestCase(10, 0, 10)]
    [TestCase(10, 100, 0)]
    [TestCase(0.05, 50, 0.03)]
    public void DiscountedPrice_AppliesDiscountAndRoundsToCents(decimal price, decimal discount, decimal expected)
    {
        Product product = new(1, ProductDetailsTests.Create(price: price, discountPercentage: discount));

        Assert.That(product.DiscountedPrice, Is.EqualTo(expected));
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    public void IsInStock_ReflectsStock(int stock, bool expected)
    {
        Product product = new(1, ProductDetailsTests.Create(stock: stock));

        Assert.That(product.IsInStock, Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Constructor_NonPositiveId_Throws(int id)
    {
        Assert.That(() => new Product(id, ProductDetailsTests.Create()), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
