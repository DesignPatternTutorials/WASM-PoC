using Microsoft.Extensions.DependencyInjection;
using OfflineCrud.Application.Products;
using OfflineCrud.Infrastructure.DummyJson;
using OfflineCrud.Infrastructure.IndexedDb;

namespace OfflineCrud.Infrastructure;

/// <summary>Registers the Infrastructure implementations of the Application ports.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the browser implementations of the Application ports.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="productApiBaseAddress">Base address of the DummyJSON API, ending in a slash.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Uri productApiBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(productApiBaseAddress);

        // LEARN: On the server, IHttpClientFactory exists mainly to pool sockets and avoid exhausting them.
        // In the browser there are no sockets to manage, because every request is a fetch() call. It's used
        // here for central configuration and the handler pipeline, so a later phase can slot in a
        // DelegatingHandler (for example, one that simulates being offline) without touching the client.
        services.AddHttpClient<IProductApiClient, DummyJsonProductApiClient>(client =>
        {
            client.BaseAddress = productApiBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // One instance for the app's lifetime, so the JS module is imported once.
        services.AddScoped<IProductRepository, IndexedDbProductRepository>();

        return services;
    }
}
