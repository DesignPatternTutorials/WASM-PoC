# Learning notes

One section per phase: what was built, the key concepts, the gotchas, and things to try.
Find every in-code lesson with `grep -rn "LEARN:" src`.

## Phase 0: Scaffold

### What was built

- `OfflineCrud.slnx`, the XML solution format that `dotnet new sln` creates on the .NET 10 SDK. It's much
  shorter and more diff-friendly than the old `.sln` format.
- `global.json` pins SDK 10.0.402 (the .NET 10 LTS band) with `rollForward: latestFeature`, so a
  newer 10.0.x patch or feature band is fine but .NET 11 isn't picked up by accident.
- `Directory.Build.props` holds the shared settings: target framework, nullable, warnings as errors,
  code-style enforcement from `.editorconfig`, and XML docs for the class libraries.
- Five projects:

  ```
  Client ──► Application ──► Domain
     │            ▲
     └──► Infrastructure
  ```

- `ArchitectureTests` fail if Domain or Application ever picks up a reference to an outer layer.
- `docs/api-notes.md` records how DummyJSON really behaves.

### Key concepts

**Where the composition root lives.** In a hosted or Blazor Server app the composition root is the
ASP.NET Core server. A standalone WASM app has no server of its own. `Program.cs` in the Client runs
*in the browser* and is the only place that knows about both the interfaces and their implementations.
So the Client references Infrastructure, but only `Program.cs` should use it.

**Why Infrastructure is a Razor class library.** The IndexedDB access will be a hand-written JS
module. Putting it in Infrastructure's `wwwroot` keeps the JS next to the C# that calls it. The RCL
SDK publishes it as a static web asset at `_content/OfflineCrud.Infrastructure/js/...`, and the
published service worker caches it along with the rest of the app.

**Two service workers.** The PWA template ships `service-worker.js` (development: does nothing) and
`service-worker.published.js` (publish: caches the app shell). Under `dotnet run` nothing is
cached, which is why offline start-up can only be tested on a published build (Phase 5).

**What `dotnet publish` produces.** Everything under `artifacts/publish/wwwroot` is static files:
`index.html`, the `_framework` folder (the .NET runtime compiled to WebAssembly, plus your
assemblies), CSS, and the service worker. Any static file host can serve it. There's no .NET on the
server.

### Gotchas

- **DummyJSON gives every created product the same id (195).** See `api-notes.md`. The local GUID
  must stay the real identity.
- **DummyJSON doesn't validate anything.** A `200` with `"price":"abc"` is a normal response.
- **Unused references disappear from compiled assemblies.** The architecture tests check
  compiled references, so a forbidden `<ProjectReference>` only fails them once code actually uses a
  type from it. Review csproj diffs too.
- **`wasm-tools` workload.** `dotnet publish` warns that it's publishing without it. It's optional:
  installing it (`dotnet workload install wasm-tools`) enables relinking and smaller downloads, and is
  needed for AOT. Not required for this project.

### Things to try

- `dotnet run --project src/OfflineCrud.Client`, then open DevTools → Application → Service workers.
  A worker is registered, but Cache Storage stays empty: that's the development worker.
- `dotnet publish src/OfflineCrud.Client -c Release -o artifacts/publish` and look at
  `artifacts/publish/wwwroot/service-worker-assets.js`. It's the list of files the published worker
  will cache, each with a hash so changed files are re-fetched.
- `curl -i "https://dummyjson.com/products/add" -X POST -H "Content-Type: application/json" -d "{}"`
  twice. Both calls return the same id.

## Phase 1: Online-only CRUD

### What was built

- **Domain:** `ProductDetails` (a value object that holds the editable fields and enforces their rules) and
  `Product` (identity + details + server-maintained values like rating and thumbnail, plus `DiscountedPrice`).
- **Application:** the `IProductApiClient` port, the `IProductService` use cases that pages call,
  `ApiException` / `ApiFailureKind` (a failure classification that doesn't mention HTTP), `PagedResult<T>`,
  and `ProductFormModel` for `EditForm`.
- **Infrastructure:** `DummyJsonProductApiClient` (internal) registered through `AddInfrastructure()` with
  `AddHttpClient`. It's the only code that knows about URLs, JSON shapes or status codes.
- **Client:** a Bootstrap 5.3 theme skinned with CSS variables, with light/dark following the OS, and Bootstrap Icons
  served locally. Pages: home, about (layer diagram, offline flow, roadmap), product list (paged, searchable, URL-driven),
  detail, create, edit, and delete with a confirmation dialog.
- **Tests:** domain rules, the service, form/domain agreement (`Application.Tests`) and the HTTP client's
  mapping and error classification against a stub handler (the new `Infrastructure.Tests` project).

### Key concepts

**Pages depend on a use-case interface, not on HTTP.** Every page injects `IProductService`. In this
phase it forwards to the API. In Phases 2–3 it will read from IndexedDB and queue writes in an outbox, and
the pages won't change. This is the practical payoff of Clean Architecture in a client app.

**HttpClient is fetch().** In WebAssembly, `HttpClient` sends requests through the browser's `fetch()`.
That has consequences:
- CORS applies. DummyJSON allows any origin, which is why the app can call it directly.
- When a request fails at the network level (offline, DNS, CORS, reset), the browser only reports
  `TypeError: Failed to fetch`, which .NET surfaces as `HttpRequestException`. You can't tell the causes
  apart, so they all become `ApiFailureKind.Network`.
- Cancelling the `CancellationToken` aborts the fetch. `CancellableComponentBase` cancels when a page is
  disposed, so a slow response doesn't land on a page the user already left.
- There are no sockets to exhaust. `AddHttpClient` is used for central configuration and the handler pipeline,
  not for connection pooling.

**One DI scope for the app's lifetime.** In WebAssembly, scoped and singleton behave the same. That's why
`ProductService` can cache categories in a field for the whole session.

**Validation runs in the browser, twice over.** `ProductFormModel`'s data annotations drive the form's
messages. `ProductDetails` enforces the same rules as a backstop, and `ProductFormModelTests` checks
the two agree. DummyJSON validates nothing, so nothing else protects the data.

**Failures are classified once, at the edge.** `DummyJsonProductApiClient` turns status codes and exceptions
into `ApiFailureKind`, and `ApiException.IsTransient` says whether a retry might help. Pages use the kind to choose
a message. Phase 3's sync engine will use `IsTransient` to choose between retrying and marking an entry Failed.

**Configuration is public.** `wwwroot/appsettings.json` is downloaded like any other static file. Put
only non-secret values there (here, the API base address).

### Gotchas

- **Writes don't stick.** Edits are echoed back and then lost; deleted products return on reload; created
  products get id 195 and a detail page that 404s. The UI says so wherever it happens. This is the
  "before" picture that the local store fixes.
- **`@page` is a Razor directive.** A loop variable called `page` in markup (`@page`) fails to compile with
  RZ2005. Rename the variable.
- **No `.razor.css`, no `{Project}.styles.css`.** Removing the template's scoped CSS files means the bundle
  isn't generated, so its `<link>` in `index.html` must go too. Otherwise it's a 404 now and an uncacheable
  app-shell entry in Phase 5.
- **`index.html` is processed at build time** (`OverrideHtmlAssetPlaceholders`). After editing it, rebuild
  before checking what the dev server serves.
- **Bootstrap Icons' CSS loads its fonts with `?hash` query strings.** Watch for this in Phase 5: the
  service worker's cache lookup must ignore the query or the icons will be missing offline.
- **Product images are on DummyJSON's CDN.** They aren't part of the app shell, so they fail offline.
  `ProductThumbnail` falls back to a local placeholder.

### Things to try

- **Break it on purpose.** Open `/products`, then DevTools → Network → Offline, then change page or search.
  You get the "You appear to be offline" alert and an empty page. The app shell is still running (it's
  already loaded), but it has no data of its own. Turn the network back on and press "Try again".
- **Watch an edit vanish.** Edit a product's title and save. The green alert shows the server echoing
  your change. Open the detail page and the original title is back.
- **Create, then open it.** Create a product, note id 195, and follow the link to its detail page: "Product not found".
- **Slow network.** DevTools → Network → "Slow 4G", then submit the form. The fieldset and submit button are
  disabled with a spinner until the response arrives.
- **Cancellation.** With throttling on, open a product and immediately click "Products". In the Network
  tab the first request shows as cancelled.
- **Validation.** Clear the title, or enter a discount of 150. The messages come from the data annotations,
  and nothing is sent.
