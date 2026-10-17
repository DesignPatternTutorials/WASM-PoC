using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Domain;

[TestFixture]
public class ProductDetailsTests
{
    [Test]
    public void Constructor_TrimsTextValues()
    {
        ProductDetails details = Create(title: "  Phone  ", description: " A phone ", category: " smartphones ", brand: " Acme ");

        Assert.Multiple(() =>
        {
            Assert.That(details.Title, Is.EqualTo("Phone"));
            Assert.That(details.Description, Is.EqualTo("A phone"));
            Assert.That(details.Category, Is.EqualTo("smartphones"));
            Assert.That(details.Brand, Is.EqualTo("Acme"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Constructor_BlankBrand_BecomesNull(string? brand)
    {
        ProductDetails details = Create(brand: brand);

        Assert.That(details.Brand, Is.Null);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Constructor_BlankTitle_Throws(string title)
    {
        Assert.That(() => Create(title: title), Throws.ArgumentException.With.Property("ParamName").EqualTo("title"));
    }

    [Test]
    public void Constructor_TitleAtMaxLength_IsAccepted()
    {
        ProductDetails details = Create(title: new string('a', ProductDetails.TitleMaxLength));

        Assert.That(details.Title, Has.Length.EqualTo(ProductDetails.TitleMaxLength));
    }

    [Test]
    public void Constructor_TitleOverMaxLength_Throws()
    {
        Assert.That(
            () => Create(title: new string('a', ProductDetails.TitleMaxLength + 1)),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("title"));
    }

    [Test]
    public void Constructor_LengthIsCheckedAfterTrimming()
    {
        string padded = "  " + new string('a', ProductDetails.TitleMaxLength) + "  ";

        Assert.That(() => Create(title: padded), Throws.Nothing);
    }

    [TestCase(-0.01)]
    [TestCase(1_000_000.01)]
    public void Constructor_PriceOutOfRange_Throws(decimal price)
    {
        Assert.That(() => Create(price: price), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [TestCase(-0.01)]
    [TestCase(100.01)]
    public void Constructor_DiscountOutOfRange_Throws(decimal discount)
    {
        Assert.That(() => Create(discountPercentage: discount), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [TestCase(-1)]
    [TestCase(ProductDetails.MaxStock + 1)]
    public void Constructor_StockOutOfRange_Throws(int stock)
    {
        Assert.That(() => Create(stock: stock), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Constructor_BoundaryValues_AreAccepted()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Create(price: 0m, discountPercentage: 0m, stock: 0), Throws.Nothing);
            Assert.That(() => Create(price: ProductDetails.MaxPrice, discountPercentage: 100m, stock: ProductDetails.MaxStock), Throws.Nothing);
        });
    }

    [Test]
    public void Equality_IsByValue()
    {
        ProductDetails first = Create();
        ProductDetails second = Create();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    internal static ProductDetails Create(
        string title = "Phone",
        string description = "A phone",
        string category = "smartphones",
        string? brand = "Acme",
        decimal price = 100m,
        decimal discountPercentage = 10m,
        int stock = 5)
    {
        return new ProductDetails(title, description, category, brand, price, discountPercentage, stock);
    }
}
