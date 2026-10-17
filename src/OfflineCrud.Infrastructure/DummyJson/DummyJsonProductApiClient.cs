using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Products;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Infrastructure.DummyJson;

/// <summary>
/// <see cref="IProductApiClient"/> over the DummyJSON <c>products</c> resource.
/// </summary>
internal sealed class DummyJsonProductApiClient : IProductApiClient
{
    // LEARN: Every byte of a response travels over the user's connection and is parsed on the browser's
    // single UI thread. `select` asks DummyJSON for only the fields the app shows, instead of every product
    // with all its reviews, dimensions and image lists. `meta` is included for its updatedAt, used as the version.
    private const string ProductFields = "title,description,category,brand,price,discountPercentage,rating,stock,thumbnail,meta";

    private readonly HttpClient _httpClient;

    public DummyJsonProductApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ServerProduct>> GetAllAsync(CancellationToken cancellationToken)
    {
        // limit=0 asks DummyJSON for everything in one response (about 25 KB compressed for 194 products).
        DummyJsonProductPage page = await GetAsync<DummyJsonProductPage>($"products?limit=0&select={ProductFields}", cancellationToken);
        return [.. page.Products.Select(ToServerProduct)];
    }

    public async Task<PagedResult<ServerProduct>> SearchAsync(string query, int skip, int take, CancellationToken cancellationToken)
    {
        string uri = string.Create(
            CultureInfo.InvariantCulture,
            $"products/search?q={Uri.EscapeDataString(query)}&limit={take}&skip={skip}&select={ProductFields}");
        DummyJsonProductPage page = await GetAsync<DummyJsonProductPage>(uri, cancellationToken);
        return new PagedResult<ServerProduct>([.. page.Products.Select(ToServerProduct)], page.Total, skip, take);
    }

    public async Task<ServerProduct?> GetByIdAsync(int serverId, CancellationToken cancellationToken)
    {
        string uri = string.Create(CultureInfo.InvariantCulture, $"products/{serverId}?select={ProductFields}");
        try
        {
            DummyJsonProduct product = await GetAsync<DummyJsonProduct>(uri, cancellationToken);
            return ToServerProduct(product);
        }
        catch (ApiException ex) when (ex.Kind == ApiFailureKind.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        return await GetAsync<List<string>>("products/category-list", cancellationToken);
    }

    public async Task<ServerProduct> CreateAsync(ProductDetails details, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "products/add") { Content = JsonContent.Create(ToWrite(details)) };
        DummyJsonProduct created = await SendAsync<DummyJsonProduct>(request, cancellationToken);
        return ToServerProduct(created);
    }

    public async Task<ServerProduct> UpdateAsync(int serverId, ProductDetails details, CancellationToken cancellationToken)
    {
        string uri = string.Create(CultureInfo.InvariantCulture, $"products/{serverId}");
        using HttpRequestMessage request = new(HttpMethod.Put, uri) { Content = JsonContent.Create(ToWrite(details)) };
        DummyJsonProduct updated = await SendAsync<DummyJsonProduct>(request, cancellationToken);
        return ToServerProduct(updated);
    }

    public async Task DeleteAsync(int serverId, CancellationToken cancellationToken)
    {
        string uri = string.Create(CultureInfo.InvariantCulture, $"products/{serverId}");
        using HttpRequestMessage request = new(HttpMethod.Delete, uri);
        using HttpResponseMessage response = await SendCoreAsync(request, cancellationToken);
    }

    /// <summary>Maps an unsuccessful HTTP status to a failure kind.</summary>
    internal static ApiFailureKind Classify(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return statusCode switch
        {
            HttpStatusCode.NotFound => ApiFailureKind.NotFound,
            HttpStatusCode.TooManyRequests => ApiFailureKind.RateLimited,
            _ when code >= 500 => ApiFailureKind.Server,
            _ => ApiFailureKind.Rejected,
        };
    }

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendCoreAsync(request, cancellationToken);
        int statusCode = (int)response.StatusCode;

        try
        {
            T? body = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return body ?? throw new ApiException(ApiFailureKind.InvalidResponse, "The server returned an empty response.", statusCode);
        }
        catch (JsonException ex)
        {
            throw new ApiException(ApiFailureKind.InvalidResponse, "The server returned a response that couldn't be read.", statusCode, ex);
        }
    }

    /// <summary>Sends a request and returns the response if it succeeded, otherwise throws a classified <see cref="ApiException"/>.</summary>
    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // LEARN: In WebAssembly, HttpClient is a thin wrapper over the browser's fetch(). When fetch fails
            // (offline, DNS failure, CORS rejection, connection reset) the browser reports only
            // "TypeError: Failed to fetch". It hides the reason on purpose, so a page can't probe the network.
            // That's why all of these collapse into one Network failure.
            throw new ApiException(ApiFailureKind.Network, "Couldn't reach the server.", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled without the caller asking: HttpClient.Timeout elapsed.
            throw new ApiException(ApiFailureKind.Network, "The server took too long to respond.", innerException: ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            string message = await ReadErrorMessageAsync(response, cancellationToken);
            throw new ApiException(Classify(response.StatusCode), message, (int)response.StatusCode);
        }
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string fallback = string.Create(CultureInfo.InvariantCulture, $"The server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        try
        {
            DummyJsonError? error = await response.Content.ReadFromJsonAsync<DummyJsonError>(cancellationToken);
            return string.IsNullOrWhiteSpace(error?.Message) ? fallback : error.Message;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Not JSON (or an unexpected content type), so there's no server message to show.
            return fallback;
        }
    }

    private static ServerProduct ToServerProduct(DummyJsonProduct product)
    {
        try
        {
            ProductDetails details = new(
                product.Title ?? string.Empty,
                product.Description ?? string.Empty,
                product.Category ?? string.Empty,
                product.Brand,
                product.Price ?? 0m,
                product.DiscountPercentage ?? 0m,
                product.Stock ?? 0);

            Uri? thumbnail = Uri.TryCreate(product.Thumbnail, UriKind.Absolute, out Uri? uri) ? uri : null;
            if (product.Id <= 0)
            {
                throw new ArgumentException("The product has no valid id.", nameof(product));
            }

            return new ServerProduct(product.Id, details, product.Rating, thumbnail, product.Meta?.UpdatedAt);
        }
        catch (ArgumentException ex)
        {
            // The server sent data that breaks a domain rule. Report it as a bad response rather than letting
            // a validation exception escape from the infrastructure layer.
            throw new ApiException(
                ApiFailureKind.InvalidResponse,
                string.Create(CultureInfo.InvariantCulture, $"Product {product.Id} from the server is invalid: {ex.Message}"),
                innerException: ex);
        }
    }

    private static DummyJsonProductWrite ToWrite(ProductDetails details)
    {
        return new DummyJsonProductWrite(
            details.Title,
            details.Description,
            details.Category,
            details.Brand,
            details.Price,
            details.DiscountPercentage,
            details.Stock);
    }
}
