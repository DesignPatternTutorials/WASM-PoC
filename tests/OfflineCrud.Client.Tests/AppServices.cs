using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OfflineCrud.Application.Connectivity;
using OfflineCrud.Application.Products;
using OfflineCrud.Application.Sync;
using OfflineCrud.Client.Services;
using OfflineCrud.Client.Tests.Fakes;

namespace OfflineCrud.Client.Tests;

/// <summary>The app's services with every dependency on storage, the network and the browser replaced.</summary>
internal sealed class AppServices
{
    public AppServices(BunitContext context)
    {
        Products = new Mock<IProductService>();
        Sync = new Mock<ISyncService>();
        Connectivity = new FakeConnectivityService();

        Sync.Setup(sync => sync.GetOutboxAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Sync.Setup(sync => sync.GetOutboxItemsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Sync.Setup(sync => sync.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SyncRunResult(0, 0, 0, 0));

        // The real scheduler, over the mocked engine. It isn't started unless a test calls Start().
        Scheduler = new SyncScheduler(Sync.Object, Connectivity, TimeProvider.System, NullLogger<SyncScheduler>.Instance);
        Toasts = new ToastService(TimeProvider.System);

        context.Services.AddSingleton(Products.Object);
        context.Services.AddSingleton(Sync.Object);
        context.Services.AddSingleton<IConnectivityService>(Connectivity);
        context.Services.AddSingleton(Scheduler);
        context.Services.AddSingleton(Toasts);
        context.Services.AddSingleton(TimeProvider.System);
    }

    public Mock<IProductService> Products { get; }

    public Mock<ISyncService> Sync { get; }

    public FakeConnectivityService Connectivity { get; }

    public SyncScheduler Scheduler { get; }

    public ToastService Toasts { get; }
}
