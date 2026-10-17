# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

A teaching project: an offline-first Blazor WebAssembly CRUD app (standalone PWA, no server of its own) against the DummyJSON `products` API, using IndexedDB plus an outbox for local-first writes. The full brief is in `docs/offline-crud-spec.md`, and the work runs in phases (0–6).

- **Done:** Phase 0 (scaffold), Phase 1 (online-only CRUD), Phase 2 (IndexedDB local store).
- **Next:** Phase 3 (outbox + sync engine).

Work one phase per feature branch. Stop at the end of each phase, summarise what was built, and wait for the user to review and merge. When a phase is finished, also mark it done in the `Milestones` array in `src/OfflineCrud.Client/Pages/About.razor` and update the phase note on `Pages/Home.razor`.

## Commands

The SDK is pinned in `global.json` to 10.0.402 (.NET 10 LTS). Run all commands from the repo root.

```sh
dotnet build                                   # whole solution (OfflineCrud.slnx); warnings are errors
dotnet test                                    # all tests
dotnet test --filter "FullyQualifiedName~ArchitectureTests.Domain_DoesNotReferenceAnyOtherSolutionProject"   # one test
dotnet run --project src/OfflineCrud.Client    # dev server on http://localhost:5121 (https profile: 7227)
dotnet publish src/OfflineCrud.Client -c Release -o artifacts/publish   # static site in artifacts/publish/wwwroot
```

Offline behaviour (the caching service worker) only exists in a **published** build. `dotnet run` uses the no-op `service-worker.js`. A script to serve the publish output locally is planned for Phase 5.

## Architecture

Clean Architecture. Dependencies point inward only:

- `OfflineCrud.Domain`: entities, value objects, domain logic. No references to other projects or packages.
- `OfflineCrud.Application`: use cases, ports (`IProductRepository`, `IOutboxStore`, `IProductApiClient`, `IConnectivityService`, `ISyncService`), DTOs. References Domain only.
- `OfflineCrud.Infrastructure`: browser implementations of the Application ports: IndexedDB via a hand-rolled JS module, `HttpClient` for DummyJSON, connectivity via JS events. It's a Razor class library so `wwwroot/js/*.js` ships as static web assets at `_content/OfflineCrud.Infrastructure/js/...`. Don't replace the JS interop with a third-party IndexedDB package; keeping the interop visible is the point.
- `OfflineCrud.Client`: Blazor WASM (PWA template). UI plus the DI **composition root**. It references Infrastructure *only* so `Program.cs` can register implementations. Pages and components depend on Application interfaces, never on Infrastructure types.
- `tests/OfflineCrud.Application.Tests`: NUnit + Moq. Covers Domain and Application. `ArchitectureTests` enforce the layering on compiled assemblies.
- `tests/OfflineCrud.Infrastructure.Tests`: NUnit. Tests `DummyJsonProductApiClient` against `StubHttpMessageHandler` (no network). Reaches internals via `InternalsVisibleTo`.

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
- Add a section to `docs/LEARNING.md` for each phase: what was built, concepts, gotchas, things to try. Keep it accurate to the code.

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
