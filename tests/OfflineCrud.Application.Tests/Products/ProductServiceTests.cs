using Moq;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Products;
using OfflineCrud.Application.Tests.Domain;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Products;

[TestFixture]
public class ProductServiceTests
{
    private static readonly PagedResult<Product> EmptyPage = new([], 0, 0, 10);

    private Mock<IProductApiClient> _apiClient = null!;
    private ProductService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _apiClient = new Mock<IProductApiClient>(MockBehavior.Strict);
        _service = new ProductService(_apiClient.Object);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task GetPageAsync_BlankSearch_ListsAllProducts(string? search)
    {
        _apiClient.Setup(api => api.GetPageAsync(20, 10, It.IsAny<CancellationToken>())).ReturnsAsync(EmptyPage);

        PagedResult<Product> result = await _service.GetPageAsync(search, 20, 10, CancellationToken.None);

        Assert.That(result, Is.SameAs(EmptyPage));
    }

    [Test]
    public async Task GetPageAsync_WithSearch_SearchesWithTrimmedQuery()
    {
        _apiClient.Setup(api => api.SearchAsync("phone", 0, 10, It.IsAny<CancellationToken>())).ReturnsAsync(EmptyPage);

        PagedResult<Product> result = await _service.GetPageAsync("  phone ", 0, 10, CancellationToken.None);

        Assert.That(result, Is.SameAs(EmptyPage));
    }

    [TestCase(-1, 10)]
    [TestCase(0, IProductService.MinPageSize - 1)]
    [TestCase(0, IProductService.MaxPageSize + 1)]
    public void GetPageAsync_InvalidPaging_Throws(int skip, int take)
    {
        Assert.That(
            () => _service.GetPageAsync(null, skip, take, CancellationToken.None),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public async Task GetCategoriesAsync_SortsAndCachesForTheSession()
    {
        _apiClient.Setup(api => api.GetCategoriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["tablets", "Beauty", "laptops"]);

        IReadOnlyList<string> first = await _service.GetCategoriesAsync(CancellationToken.None);
        IReadOnlyList<string> second = await _service.GetCategoriesAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new[] { "Beauty", "laptops", "tablets" }));
            Assert.That(second, Is.SameAs(first));
        });
        _apiClient.Verify(api => api.GetCategoriesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetCategoriesAsync_AfterFailure_TriesAgain()
    {
        _apiClient.SetupSequence(api => api.GetCategoriesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(ApiFailureKind.Network, "offline"))
            .ReturnsAsync(["beauty"]);

        Assert.That(() => _service.GetCategoriesAsync(CancellationToken.None), Throws.InstanceOf<ApiException>());
        IReadOnlyList<string> categories = await _service.GetCategoriesAsync(CancellationToken.None);

        Assert.That(categories, Is.EqualTo(new[] { "beauty" }));
    }

    [Test]
    public async Task CreateUpdateDelete_PassThroughToTheApi()
    {
        ProductDetails details = ProductDetailsTests.Create();
        Product created = new(195, details);
        Product updated = new(5, details);
        using CancellationTokenSource cts = new();

        _apiClient.Setup(api => api.CreateAsync(details, cts.Token)).ReturnsAsync(created);
        _apiClient.Setup(api => api.UpdateAsync(5, details, cts.Token)).ReturnsAsync(updated);
        _apiClient.Setup(api => api.DeleteAsync(5, cts.Token)).Returns(Task.CompletedTask);

        Product createResult = await _service.CreateAsync(details, cts.Token);
        Product updateResult = await _service.UpdateAsync(5, details, cts.Token);
        await _service.DeleteAsync(5, cts.Token);

        Assert.Multiple(() =>
        {
            Assert.That(createResult, Is.SameAs(created));
            Assert.That(updateResult, Is.SameAs(updated));
        });
        _apiClient.Verify(api => api.DeleteAsync(5, cts.Token), Times.Once);
    }
}
