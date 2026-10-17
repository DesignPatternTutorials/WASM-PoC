#:sdk Microsoft.NET.Sdk.Web

// Serves a published Blazor WebAssembly app as plain static files, so the real (caching) service worker can be tested,
// including starting the app with the network switched off.
//
//   dotnet run tools/serve.cs -- [wwwroot folder] [port]
//
// Defaults: artifacts/publish/wwwroot on port 5200. Relative paths are relative to the repo root. A .NET 10
// file-based app: no project file needed.
//
// LEARN: Why not `dotnet run` on the client project? Its dev server hands the browser the no-op service-worker.js,
// so nothing is cached. Only `dotnet publish` swaps in service-worker.published.js. Any static file host works for the
// output; this one exists so the repo needs nothing installed beyond the .NET SDK.
using System.Globalization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

// `dotnet run tools/serve.cs` runs with tools/ as the working directory, so relative paths are taken relative to the
// repo root (its parent), which is where they were most likely typed from.
string repoRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".."));
string requested = args.Length > 0 ? args[0] : Path.Combine("artifacts", "publish", "wwwroot");
string root = Path.IsPathRooted(requested) ? requested : Path.GetFullPath(Path.Combine(repoRoot, requested));
int port = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5200;

if (!File.Exists(Path.Combine(root, "index.html")))
{
    Console.Error.WriteLine($"No index.html in {root}. Publish first: dotnet publish src/OfflineCrud.Client -c Release -o artifacts/publish");
    return 1;
}

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseUrls(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}"));
builder.Logging.SetMinimumLevel(LogLevel.Warning);
WebApplication app = builder.Build();

PhysicalFileProvider files = new(root);

// The defaults already know .wasm, .js and .json. Blazor also publishes ICU globalisation data as .dat.
FileExtensionContentTypeProvider contentTypes = new();
contentTypes.Mappings[".dat"] = "application/octet-stream";
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";

StaticFileOptions staticFiles = new()
{
    FileProvider = files,
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = context =>
    {
        // The files that decide which version runs must always be revalidated, or the browser could keep using a
        // stale copy from its HTTP cache and never notice a new publish.
        if (context.File.Name is "index.html" or "service-worker.js" or "service-worker-assets.js")
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
};

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
app.UseStaticFiles(staticFiles);

// Deep links such as /products/<id> aren't files: serve index.html and let Blazor's router take over. (Offline, the
// service worker does the same from its cache.)
app.MapFallbackToFile("index.html", staticFiles);

// localhost counts as a secure context, so service workers work over plain HTTP here. Any other host needs HTTPS.
Console.WriteLine($"Serving {root}");
Console.WriteLine($"Open http://localhost:{port}  (Ctrl+C to stop)");
await app.RunAsync();
return 0;
