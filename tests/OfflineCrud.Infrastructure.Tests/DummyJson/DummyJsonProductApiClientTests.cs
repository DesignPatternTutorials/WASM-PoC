using System.Text.Json;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Products;
using OfflineCrud.Domain.Products;
using OfflineCrud.Infrastructure.DummyJson;

namespace OfflineCrud.Infrastructure.Tests.DummyJson;

[TestFixture]
public class DummyJsonProductApiClientTests
{
    private const string ProductJson = """
        {"id":1,"title":"Essence Mascara Lash Princess","description":"Volumising mascara.","category":"beauty",
         "price":9.99,"discountPercentage":10.48,"rating":2.56,"stock":99,"brand":"Essence",
         "thumbnail":"https://cdn.dummyjson.com/product-images/1/thumbnail.webp",
         "meta":{"createdAt":"2025-10-09T14:47:01.588Z","updatedAt":"2026-05-23T11:27:41.868Z"}}
        """;

    private static readonly Uri BaseAddress = new("https://dummyjson.example/");

    [Test]
    public async Task GetAllAsync_RequestsEverythingWithSelectedFieldsAndMeta()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.OK, $$"""{"products":[{{ProductJson}}],"total":194,"skip":0,"limit":194}""");

        IReadOnlyList<ServerProduct> products = await CreateClient(handler).GetAllAsync(CancellationToken.None);

        string pathAndQuery = handler.Requests.Single().Uri.PathAndQuery;
        Assert.Multiple(() =>
        {
            Assert.That(pathAndQuery, Does.StartWith("/products?limit=0&select=title,"));
            Assert.That(pathAndQuery, Does.EndWith(",meta"));
            Assert.That(products, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task SearchAsync_MapsTheEnvelope()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.OK, $$"""{"products":[{{ProductJson}}],"total":23,"skip":20,"limit":1}""");

        PagedResult<ServerProduct> page = await CreateClient(handler).SearchAsync("mascara", 20, 10, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(23));
            Assert.That(page.Skip, Is.EqualTo(20));
            Assert.That(page.Take, Is.EqualTo(10));
            Assert.That(page.Items, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task GetByIdAsync_MapsEveryField()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, ProductJson);

        ServerProduct? product = await CreateClient(handler).GetByIdAsync(1, CancellationToken.None);

        Assert.That(product, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(product!.ServerId, Is.EqualTo(1));
            Assert.That(product.Details.Title, Is.EqualTo("Essence Mascara Lash Princess"));
            Assert.That(product.Details.Description, Is.EqualTo("Volumising mascara."));
            Assert.That(product.Details.Category, Is.EqualTo("beauty"));
            Assert.That(product.Details.Brand, Is.EqualTo("Essence"));
            Assert.That(product.Details.Price, Is.EqualTo(9.99m));
            Assert.That(product.Details.DiscountPercentage, Is.EqualTo(10.48m));
            Assert.That(product.Details.Stock, Is.EqualTo(99));
            Assert.That(product.Rating, Is.EqualTo(2.56));
            Assert.That(product.ThumbnailUrl, Is.EqualTo(new Uri("https://cdn.dummyjson.com/product-images/1/thumbnail.webp")));
            Assert.That(product.Version, Is.EqualTo("2026-05-23T11:27:41.868Z"), "meta.updatedAt is the version");
        });
    }

    [Test]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.NotFound, """{"message":"Product with id '999' not found"}""");

        ServerProduct? product = await CreateClient(handler).GetByIdAsync(999, CancellationToken.None);

        Assert.That(product, Is.Null);
    }

    [Test]
    public async Task SearchAsync_EscapesTheQuery()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.OK, """{"products":[],"total":0,"skip":0,"limit":0}""");

        await CreateClient(handler).SearchAsync("phone & case", 0, 10, CancellationToken.None);

        Assert.That(handler.Requests.Single().Uri.Query, Does.StartWith("?q=phone%20%26%20case&limit=10&skip=0"));
    }

    [Test]
    public async Task CreateAsync_PostsDetailsWithoutAnIdOrNullBrand()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.Created,
            """{"id":195,"title":"Widget","description":"A widget","category":"misc","price":5,"discountPercentage":0,"stock":3}""");
        ProductDetails details = new("Widget", "A widget", "misc", brand: null, price: 5m, discountPercentage: 0m, stock: 3);

        ServerProduct created = await CreateClient(handler).CreateAsync(details, CancellationToken.None);

        StubHttpMessageHandler.RecordedRequest request = handler.Requests.Single();
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Uri.AbsolutePath, Is.EqualTo("/products/add"));
            Assert.That(body.RootElement.TryGetProperty("id", out _), Is.False, "DummyJSON echoes a body id over the real one");
            Assert.That(body.RootElement.TryGetProperty("brand", out _), Is.False);
            Assert.That(body.RootElement.GetProperty("title").GetString(), Is.EqualTo("Widget"));
            Assert.That(created.ServerId, Is.EqualTo(195));
            Assert.That(created.Version, Is.Null, "write responses carry no meta");
            Assert.That(created.Rating, Is.Null);
        });
    }

    [Test]
    public async Task UpdateAsync_PutsToTheProductUri()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, ProductJson);

        await CreateClient(handler).UpdateAsync(1, new ProductDetails("T", "D", "beauty", null, 1m, 0m, 1), CancellationToken.None);

        StubHttpMessageHandler.RecordedRequest request = handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Put));
            Assert.That(request.Uri.AbsolutePath, Is.EqualTo("/products/1"));
        });
    }

    [Test]
    public void DeleteAsync_NotFound_ThrowsNotFound()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.NotFound, """{"message":"Product with id '195' not found"}""");

        Assert.That(
            () => CreateClient(handler).DeleteAsync(195, CancellationToken.None),
            Throws.InstanceOf<ApiException>()
                .With.Property(nameof(ApiException.Kind)).EqualTo(ApiFailureKind.NotFound)
                .And.Property(nameof(ApiException.StatusCode)).EqualTo(404)
                .And.Message.EqualTo("Product with id '195' not found"));
    }

    [TestCase(HttpStatusCode.NotFound, ApiFailureKind.NotFound)]
    [TestCase(HttpStatusCode.TooManyRequests, ApiFailureKind.RateLimited)]
    [TestCase(HttpStatusCode.InternalServerError, ApiFailureKind.Server)]
    [TestCase(HttpStatusCode.ServiceUnavailable, ApiFailureKind.Server)]
    [TestCase(HttpStatusCode.BadRequest, ApiFailureKind.Rejected)]
    [TestCase(HttpStatusCode.Unauthorized, ApiFailureKind.Rejected)]
    [TestCase(HttpStatusCode.Conflict, ApiFailureKind.Rejected)]
    [TestCase(HttpStatusCode.UnprocessableEntity, ApiFailureKind.Rejected)]
    public void Classify_MapsStatusCodes(HttpStatusCode statusCode, ApiFailureKind expected)
    {
        Assert.That(DummyJsonProductApiClient.Classify(statusCode), Is.EqualTo(expected));
    }

    [Test]
    public void ErrorWithoutJsonBody_UsesTheStatusAsTheMessage()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            ReasonPhrase = "Bad Gateway",
            Content = new StringContent("<html>upstream error</html>", System.Text.Encoding.UTF8, "text/html"),
        }));

        Assert.That(
            () => CreateClient(handler).GetAllAsync(CancellationToken.None),
            Throws.InstanceOf<ApiException>()
                .With.Property(nameof(ApiException.Kind)).EqualTo(ApiFailureKind.Server)
                .And.Message.EqualTo("The server returned 502 Bad Gateway."));
    }

    [Test]
    public void HttpRequestException_BecomesNetworkFailure()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Throwing(new HttpRequestException("TypeError: Failed to fetch"));

        Assert.That(
            () => CreateClient(handler).GetAllAsync(CancellationToken.None),
            Throws.InstanceOf<ApiException>()
                .With.Property(nameof(ApiException.Kind)).EqualTo(ApiFailureKind.Network)
                .And.Property(nameof(ApiException.IsTransient)).True
                .And.Property(nameof(ApiException.StatusCode)).Null);
    }

    [Test]
    public void Timeout_BecomesNetworkFailure()
    {
        StubHttpMessageHandler handler = new(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        DummyJsonProductApiClient client = CreateClient(handler, timeout: TimeSpan.FromMilliseconds(50));

        Assert.That(
            () => client.GetAllAsync(CancellationToken.None),
            Throws.InstanceOf<ApiException>().With.Property(nameof(ApiException.Kind)).EqualTo(ApiFailureKind.Network));
    }

    [Test]
    public void CallerCancellation_IsNotDisguisedAsAFailure()
    {
        StubHttpMessageHandler handler = new(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(50));

        Assert.That(
            () => CreateClient(handler).GetAllAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void MalformedJson_BecomesInvalidResponse()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "{not json");

        Assert.That(
            () => CreateClient(handler).GetAllAsync(CancellationToken.None),
            Throws.InstanceOf<ApiException>()
                .With.Property(nameof(ApiException.Kind)).EqualTo(ApiFailureKind.InvalidResponse)
                .And.Property(nameof(ApiException.IsTransient)).False);
    }

    [Test]
    public void ProductThatBreaksDomainRules_BecomesInvalidResponse()
    {
        // DummyJSON accepts and echoes anything, e.g. POST {} returns {"id":195} with no title.
        StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"id":195}""");

        Assert.That(
            () => CreateClient(handler).GetByIdAsync(195, CancellationToken.None),
            Throws.InstanceOf<ApiException>().With.Property(nameof(ApiException.Kind)).EqualTo(ApiFailureKind.InvalidResponse));
    }

    private static DummyJsonProductApiClient CreateClient(StubHttpMessageHandler handler, TimeSpan? timeout = null)
    {
        HttpClient httpClient = new(handler) { BaseAddress = BaseAddress };
        if (timeout is { } value)
        {
            httpClient.Timeout = value;
        }

        return new DummyJsonProductApiClient(httpClient);
    }
}
