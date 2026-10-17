using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Connectivity;
using OfflineCrud.Application.Products;
using OfflineCrud.Application.Sync;
using OfflineCrud.Client;
using OfflineCrud.Client.Services;
using OfflineCrud.Infrastructure;

// LEARN: This is the composition root, and it runs inside the browser. There's no server-side Program.cs:
// the host here is the browser tab, and `dotnet publish` produces only static files.
WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// LEARN: wwwroot/appsettings.json is downloaded by the browser like any other static file, so anyone can
// read it. Configuration in a WASM app must never contain secrets.
string apiBaseAddress = builder.Configuration["DummyJson:BaseAddress"]
    ?? throw new InvalidOperationException("Configuration value 'DummyJson:BaseAddress' is missing from wwwroot/appsettings.json.");

builder.Services.AddInfrastructure(new Uri(apiBaseAddress));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(Random.Shared);

// LEARN: Blazor WebAssembly renders inside one DI scope that lasts until the tab closes, so "scoped" behaves like
// "singleton" for components. But that scope isn't the root provider. The sync scheduler is started from the root
// (below), so anything it shares with the pages (the store lock above all) must be a real singleton. A scoped one
// would quietly be two instances, one for the root and one for the components, and the lock wouldn't lock anything.
builder.Services.AddSingleton<LocalStoreLock>();
builder.Services.AddSingleton<IProductService, ProductService>();
builder.Services.AddSingleton<ISyncService, SyncService>();
builder.Services.AddSingleton<SyncScheduler>();
builder.Services.AddSingleton<ISyncTrigger>(services => services.GetRequiredService<SyncScheduler>());
builder.Services.AddSingleton<ToastService>();

WebAssemblyHost host = builder.Build();

// LEARN: Don't make start-up wait on anything optional. Awaiting a JS module import here, before RunAsync, means that
// if the import is slow or fails (a 404 for a _content/ file after an incremental rebuild, a blocked script), the app
// never renders and the loading screen spins forever. Background services start alongside the UI instead, and a
// failure is logged to the browser console rather than stopping the app.
_ = StartBackgroundServicesAsync(host.Services);

await host.RunAsync();

static async Task StartBackgroundServicesAsync(IServiceProvider services)
{
    ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("OfflineCrud.Startup");
    try
    {
        // Connectivity first, so the first sync run knows whether to bother. JS interop works before RunAsync: the .NET
        // runtime was started by blazor.webassembly.js, so the browser's APIs are already there.
        await services.GetRequiredService<IConnectivityService>().StartAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        // Without browser events the app still works: request failures still mark it offline.
        logger.LogError(ex, "Connectivity monitoring couldn't start; relying on request failures only.");
    }

    // Start syncing as soon as the app starts: send anything left over from the last session. The scheduler's loop
    // catches and logs its own errors, so a broken IndexedDB can't take the UI down either.
    services.GetRequiredService<SyncScheduler>().Start();
}
