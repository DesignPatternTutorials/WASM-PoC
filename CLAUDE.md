# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

A teaching project: an offline-first Blazor WebAssembly CRUD app (standalone PWA, no server of its own) against the DummyJSON `products` API, using IndexedDB plus an outbox for local-first writes. The full brief is in `docs/offline-crud-spec.md`, and the work runs in phases (0–6).

- **Done:** all phases, 0–6 (scaffold, online-only CRUD, IndexedDB store, outbox + sync, connectivity + UX, PWA app shell, polish).
- **Not started:** the spec's "ask before doing" items (a real ASP.NET Core API in place of DummyJSON; a GitHub Actions workflow). Ask before starting either.

Work one phase per feature branch. Stop at the end of each phase, summarise what was built, and wait for the user to review and merge. When a phase is finished, also mark it done in the `Milestones` array in `src/OfflineCrud.Client/Pages/About.razor` and update the phase note on `Pages/Home.razor`.

## Commands

The SDK is pinned in `global.json` to 10.0.402 (.NET 10 LTS). Run all commands from the repo root.

```sh
dotnet build                                   # whole solution (OfflineCrud.slnx); warnings are errors
dotnet test                                    # all tests
dotnet test --filter "FullyQualifiedName~ArchitectureTests.Domain_DoesNotReferenceAnyOtherSolutionProject"   # one test
dotnet run --project src/OfflineCrud.Client    # dev server on http://localhost:5121 (https profile: 7227)
dotnet publish src/OfflineCrud.Client -c Release -o artifacts/publish   # static site in artifacts/publish/wwwroot
./scripts/publish-and-serve.ps1                # publish + external-reference check + serve on http://localhost:5200
./scripts/publish-and-serve.ps1 -NoServe       # publish + check only
dotnet run tools/serve.cs -- artifacts/publish/wwwroot 5200   # serve an existing publish (paths relative to repo root)
```

Offline behaviour (the caching service worker) only exists in a **published** build; `dotnet run` uses the no-op `service-worker.js`. Test offline start-up against the publish output (steps in `docs/LEARNING.md`, Phase 5). `tools/serve.cs` is a .NET 10 file-based app with no project file. It runs with `tools/` as its working directory, and `Directory.Build.props` applies to it.

## Architecture

Clean Architecture. Dependencies point inward only:

- `OfflineCrud.Domain`: entities, value objects, domain logic. No references to other projects or packages.
- `OfflineCrud.Application`: use cases, ports (`IProductRepository`, `IOutboxStore`, `IProductApiClient`, `IConnectivityService`, `ISyncService`), DTOs. References Domain only.
- `OfflineCrud.Infrastructure`: browser implementations of the Application ports: IndexedDB via a hand-rolled JS module, `HttpClient` for DummyJSON, connectivity via JS events. It's a Razor class library so `wwwroot/js/*.js` ships as static web assets at `_content/OfflineCrud.Infrastructure/js/...`. Don't replace the JS interop with a third-party IndexedDB package; keeping the interop visible is the point.
- `OfflineCrud.Client`: Blazor WASM (PWA template). UI plus the DI **composition root**. It references Infrastructure *only* so `Program.cs` can register implementations. Pages and components depend on Application interfaces, never on Infrastructure types.
- `tests/OfflineCrud.Application.Tests`: NUnit + Moq. Covers Domain and Application. `ArchitectureTests` enforce the layering on compiled assemblies.
- `tests/OfflineCrud.Infrastructure.Tests`: NUnit. Tests `DummyJsonProductApiClient` against `StubHttpMessageHandler` (no network). Reaches internals via `InternalsVisibleTo`.
- `tests/OfflineCrud.Client.Tests`: bUnit 2 (`BunitContext`, `Render<T>`) + NUnit + Moq. Derive from `ComponentTestBase` (Loose JS interop, disposed per test). Use `AppServices` to register mocked `IProductService`/`ISyncService`, `FakeConnectivityService`, and the real `SyncScheduler`/`ToastService`. Dispose the scheduler in `[TearDown]`. Use `WaitForAssertion` for anything rendered after an `await`.

How a request flows: a page (inheriting `CancellableComponentBase`, which cancels on dispose) calls `IProductService`. That calls `IProductRepository` (IndexedDB) for reads and `IProductApiClient` (DummyJSON) for refreshes and writes.

- **API errors:** `DummyJsonProductApiClient` turns every HTTP or JSON failure into an `ApiException` with an `ApiFailureKind`, and pages show it with `ApiErrorAlert`. `IsTransient` (network, 5xx, 429) is the retry signal for the sync engine. Don't let `HttpRequestException` escape Infrastructure.
- **Refresh results:** refresh methods return a `RefreshResult` instead of throwing.

Local storage rules (Phase 2):

- **Identity:** `Product.Id` is a local `Guid`. `ServerId` is not unique, because every DummyJSON create returns 195. Routes use the GUID.
- **Merging server data:** goes through `ServerCopyMerger` and `Product.ShouldAcceptServerCopy`. A server copy replaces a local one only if the local copy is `Synced`, isn't a tombstone, and the version token (`meta.updatedAt`) differs.
- **Deletes:** are tombstones (`IsDeleted`), never removals.
- **Writes after reads:** read-merge-write sequences take `ProductService._storeLock`.
- **The JS module (`src/OfflineCrud.Infrastructure/wwwroot/js/indexedDb.js`):** the schema is versioned by the length of its `migrations` array. Add a step; never edit a shipped one. Multi-record writes must go through `writeAll()`, which aborts on synchronous errors so batches stay all-or-nothing. Field names in `ProductRecord` must match the index key paths in the JS (`ProductRecordMapperTests` checks them).
- **Testing:** the JS module isn't covered by `dotnet test`. Check changes to it with a browser (DevTools → Application → IndexedDB) or a Node run against `fake-indexeddb`.

Outbox and sync rules (Phase 3):

- **Writes are local-first.** `ProductService` writes never call the API. They take the store lock, apply the change to the product, run `OutboxPlanner.Plan`, set the product's state from `plan.SyncState`, and write everything with one `IOutboxStore.CommitAsync` (one IndexedDB transaction across `products` and `outbox`). Then they call `ISyncTrigger.RequestSync()`.
- **`SyncState` is always derived from the queued outbox entries** (`OutboxPlanner.DeriveSyncState`). Never set `Synced` directly after a send.
- **Only `SyncService` calls the API's write methods.** It's FIFO per product, re-reads each entry under the lock before marking it `InFlight`, and passes the entry id as the idempotency key. It classifies failures with `ApiException.IsTransient`: retry with `RetryPolicy` backoff, otherwise `Failed`. Delete + 404 counts as success.
- **`LocalStoreLock`** is shared by `ProductService` and `SyncService`. Every read-decide-write sequence takes it.
- **All app-lifetime services are singletons in `Program.cs`.** `SyncScheduler` is started from the root provider, and a scoped registration would give it a different instance from the components.
- **`SyncScheduler`** (Client) is the only thing that runs sync. Its `Changed` event drives the UI; subscribers must unsubscribe on dispose and re-render via `InvokeAsync`.
- **Testing:** `tests/.../Fakes/InMemoryLocalStore.cs` implements both store ports (and `ILocalDatabase`) and stores copies (like IndexedDB). Use it for stateful sync and service tests.

Connectivity and UX rules (Phase 4):

- **Connectivity has one source of truth:** `IConnectivityService` (`BrowserConnectivityService`). It combines `navigator.onLine` events (`wwwroot/js/connectivity.js`, calling a `[JSInvokable]`), request outcomes reported by `ConnectivityHandler` (in the `IProductApiClient` HttpClient pipeline), and "Simulate offline". The handler implements simulated offline by throwing `HttpRequestException` before sending, so nothing else special-cases it.
- **`SyncScheduler` pauses while certainly offline** (`Offline` or `SimulatedOffline`, but not `Unreachable`) and requests a run when the status returns to `Online`.
- **Background events reach the user through `BackgroundNotifier`**, which turns `SyncScheduler.RunCompleted` and connectivity changes into toasts via `ToastService`. Keep toasts for background outcomes; actions the user just took get inline confirmation on the page.
- **Outbox management (Retry, Discard, Clear) lives on `ISyncService`.** Discard lets the server win (see `DiscardAsync`). Clear waits for a running sync.
- **Components that subscribe to singleton events** (`Changed`, `RunCompleted`) must unsubscribe in `Dispose` and re-render via `InvokeAsync(StateHasChanged)`.

App shell rules (Phase 5):

- **What the service worker caches.** `wwwroot/service-worker.published.js` caches **everything** in the SDK's asset manifest except `offlineAssetsExclude` (the worker itself, `.map`, `LICENSE`). Anything added to `wwwroot` or an RCL's `wwwroot` is downloaded on install, so keep them free of unused files. Cache lookups use `ignoreSearch`.
- **What it answers.** It only handles same-origin GETs; navigations get `index.html`. Never route DummyJSON or other cross-origin requests through it.
- **No external URLs in the app shell.** `index.html` and the CSS must not reference other origins (`publish-and-serve.ps1` fails if they do). Vendor libraries into `wwwroot/lib`.
- **Updates.** `index.html` announces a waiting worker; `UpdateBanner` + `wwwroot/js/pwa.js` post `SKIP_WAITING`; `controllerchange` reloads the page. Keep `updateViaCache: 'none'`.
- **Checking the worker without a browser:** run it under Node with mocked `self`/`caches`/`fetch` against the publish output.

Form validation lives in two places that must agree: data annotations on `ProductFormModel` (drive `EditForm`) and guard clauses in `ProductDetails` (the domain backstop). `ProductFormModelTests` checks they match, so update both together.

If a change would add a reference that breaks this direction, stop and ask.

Key design rules from the spec:

- IndexedDB is the UI's source of truth; the API is a sync target.
- A local write and its outbox entry go in one IndexedDB transaction.
- App-shell assets (CSS, JS, fonts, icons) must be local. No CDNs, or offline start-up breaks. Bootstrap 5.3 and Bootstrap Icons 1.11.3 are vendored under `wwwroot/lib`. The theme is CSS variables in `wwwroot/css/app.css`; there's no Sass build.
- No Bootstrap JavaScript is loaded. The navbar toggle and modals are driven by Blazor state.
- `index.html` is processed at build time (`OverrideHtmlAssetPlaceholders`). Rebuild before checking what the dev server serves.

## DummyJSON behaviour

`docs/api-notes.md` records what was verified with curl, and it overrides the spec. The points most likely to matter:

- `POST /products/add` always returns `id = total + 1` (195), so server ids aren't unique. The local GUID is the identity.
- Writes aren't persisted. Follow-up writes to a created id return 404.
- There's no server-side validation.

## Teaching material

This is a learning project, so explanations are part of the deliverable:

- Mark every WASM-specific or offline-specific decision with a `// LEARN:` comment (`<!-- LEARN: -->` in XML/HTML), so `grep -rn "LEARN:"` finds every lesson.
- Use XML doc comments on public types and members. `GenerateDocumentationFile` is on for Domain, Application and Infrastructure, so missing docs fail the build.
- Add a section to `docs/LEARNING.md` for each phase: what was built, concepts, gotchas, things to try. Keep it accurate to the code. Each section describes its own phase; when a later change supersedes something, add a "Later: …" note there rather than rewriting history. The phase-commit table at the top of `LEARNING.md` lets readers check out each stage.
- `docs/interview-notes.md` holds condensed talking points. Update it when a change alters one of its claims.

## Git workflow

- Branches: `main` (releases only) and `dev` (integration). Never commit directly to either.
- Every piece of work goes on a branch from `dev`: `feature/<short-name>`, `fix/<short-name>`.
- Before starting work, check the current branch. If it's `main` or `dev`, create a feature branch first.
- Commits use Conventional Commits: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`, with optional scope, e.g. `feat(client): add login page`.
- Small, focused commits. Don't merge; the user reviews and merges to `dev` themselves.

## C# conventions

- Follow Microsoft's .NET coding conventions and the repo's `.editorconfig` (enforced in build via `EnforceCodeStyleInBuild`).
- Shared build settings (TFM, nullable, warnings as errors) live in `Directory.Build.props`. Don't repeat them in project files.
- Nullable reference types are enabled. Leave no warnings unresolved (`TreatWarningsAsErrors` is on).
- Use file-scoped namespaces. Go `async` all the way with the `Async` suffix, and pass `CancellationToken` through.
- Use constructor dependency injection; no service locator.
- Tests use NUnit + Moq. Write or update tests with each feature. Run `dotnet build` and `dotnet test` before committing.
