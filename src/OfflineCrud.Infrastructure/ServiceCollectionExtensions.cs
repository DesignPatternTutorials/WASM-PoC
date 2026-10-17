using Microsoft.Extensions.DependencyInjection;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Connectivity;
using OfflineCrud.Application.Products;
using OfflineCrud.Application.Sync;
using OfflineCrud.Infrastructure.Connectivity;
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

        services.AddSingleton<BrowserConnectivityService>();
        services.AddSingleton<IConnectivityService>(provider => provider.GetRequiredService<BrowserConnectivityService>());
        services.AddTransient<ConnectivityHandler>();

        // LEARN: On the server, IHttpClientFactory exists mainly to pool sockets and avoid exhausting them.
        // In the browser there are no sockets to manage, because every request is a fetch() call. It's used here for
        // central configuration and the handler pipeline: ConnectivityHandler sits in front of every DummyJSON request
        // without the API client knowing about it.
        services.AddHttpClient<IProductApiClient, DummyJsonProductApiClient>(client =>
            {
                client.BaseAddress = productApiBaseAddress;
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<ConnectivityHandler>();

        // Singletons: one JS module import for the app's lifetime, shared by the products and the outbox.
        services.AddSingleton<IndexedDbModule>();
        services.AddSingleton<IProductRepository, IndexedDbProductRepository>();
        services.AddSingleton<IOutboxStore, IndexedDbOutboxStore>();
        services.AddSingleton<ILocalDatabase, IndexedDbLocalDatabase>();

        return services;
    }
}
