using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OfflineCrud.Application.Products;
using OfflineCrud.Client;
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

// LEARN: Scoped and singleton mean the same thing here. A WebAssembly app has one DI scope that lasts
// until the tab is closed or reloaded.
builder.Services.AddScoped<IProductService, ProductService>();

await builder.Build().RunAsync();
