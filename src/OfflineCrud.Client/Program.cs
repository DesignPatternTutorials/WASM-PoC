using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OfflineCrud.Application.Common;
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

WebAssemblyHost host = builder.Build();

// Start syncing as soon as the app starts: send anything left over from the last session.
host.Services.GetRequiredService<SyncScheduler>().Start();

await host.RunAsync();
