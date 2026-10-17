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
