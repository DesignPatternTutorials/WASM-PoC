# Interview notes

Short talking points, each tied to where this project shows it. The detail is in `LEARNING.md`.

## Blazor WebAssembly vs Blazor Server

| | WebAssembly | Server |
| --- | --- | --- |
| Where C# runs | In the browser tab, on the .NET runtime compiled to WebAssembly | On the server; the browser holds a SignalR connection |
| First load | Downloads the runtime and app (a few MB, cached afterwards) | Small. UI diffs stream over the connection |
| Offline | Possible: no server needed once loaded | Impossible: every click is a round trip |
| Latency | UI events are local | Every event crosses the network |
| Threads | One (the UI thread); `await` interleaves work | Real threads; renderer sync context per circuit |
| Data access | Only via HTTP APIs (CORS, auth tokens), or browser storage | Direct (EF Core, internal services) |
| Secrets | None in the client: everything shipped is public, including `appsettings.json` | Server-side config stays private |
| DI scopes | Components share one scope for the tab's lifetime; the root provider is separate | One scope per circuit (user connection) |
| Scale cost | Static hosting; work on users' devices | Server memory per connected user |

What to say:
- **Choosing.** Pick Server for internal apps that need direct data access and a tiny download. Pick WASM for offline,
  low latency, static hosting, or when the server must stay stateless. .NET 8+ "Auto" render mode uses Server until
  the WebAssembly runtime has been downloaded and cached, then uses WebAssembly on later visits.
- **`HttpClient` is `fetch()` in WASM.** So CORS applies, and every network failure looks the same
  (`TypeError: Failed to fetch` → `HttpRequestException`). Cancelling a token aborts the fetch. There are no sockets, so
  `IHttpClientFactory` is for configuration and handlers, not pooling (`ServiceCollectionExtensions`,
  `DummyJsonProductApiClient`).
- **Single-threaded isn't race-free.** Any `await` lets another operation run, so read-modify-write still needs a lock
  (`LocalStoreLock`).

## Offline-first and the outbox pattern

- **The core idea: local storage is the UI's source of truth, and the server is a sync target.** Reads come from
  IndexedDB and are revalidated in the background (stale-while-revalidate). Writes go to IndexedDB and an outbox.
- **The transactional outbox.** The change and the instruction to send it are written in **one transaction**, so
  neither can exist without the other (`IOutboxStore.CommitAsync` → `commit()` across two stores). It's the same pattern
  as the server-side "write the row and the message together, publish later".
- **Sync engine responsibilities** (`SyncService`):
  - FIFO per entity: an update can't overtake its create. Different entities are independent.
  - Classify failures: network, 5xx and 429 retry with **exponential backoff and jitter**; other 4xx fail permanently
    with a readable reason, and the user can Retry or Discard.
  - One run at a time; re-read each entry under the lock before sending; recover in-flight entries after a crash.
- **Coalescing** (`OutboxPlanner`). Create then delete → nothing sent. Many updates → the latest only. Never touch an
  entry that's in flight. The trade-off: fewer requests, but no history. Wrong for audit logs or event sourcing.
- **Derive state, don't store it twice.** A product's `SyncState` is computed from what's still queued for it, which
  avoids "Synced" showing while an edit is still waiting.
- **Client-generated ids.** The record must exist before the server knows it, so the client makes a GUID. The server
  id is just a field. The outbox entry id doubles as the **idempotency key**.
- **Idempotency.** Retrying after a lost response can apply a change twice. The fix is an `Idempotency-Key` header that
  the server remembers, returning the original result when a key repeats. Without server support, retries of
  non-idempotent POSTs are unsafe.
- **Server data vs local changes.** A refresh only replaces a local copy if it's `Synced`, isn't a tombstone, and the
  server's version token differs (`Product.ShouldAcceptServerCopy`). Tombstones stop deleted records coming back.
- **Conflicts.** Version checks detect them only if the server enforces them: `If-Match: <etag>` → `412`. Then choose:
  last-writer-wins, server-wins, or ask the user to merge. CRDTs (conflict-free replicated data types) avoid the
  question for data types that can merge automatically.

## IndexedDB

- An async, transactional, per-origin object database in the browser that survives restarts. It holds far more than
  `localStorage`, which is synchronous, string-only and small.
- **Schema = object stores + indexes, versioned.** Only `upgradeneeded` can change them. Keep an append-only list of
  migrations (`indexedDb.js`). Records themselves are schemaless.
- **Transactions auto-commit** when idle. Awaiting non-IndexedDB work mid-transaction makes the next request fail.
- **A synchronous error in `put()` doesn't abort the transaction.** Call `abort()` yourself for all-or-nothing.
- **Indexes skip invalid keys** (`null`, booleans). Compound indexes give filtered, ordered range scans.
- **From .NET** it's always JS interop: JSON-serialised arguments, async calls, `IJSObjectReference` for modules
  (dispose it), and `DotNetObjectReference` for JS → .NET callbacks (dispose it too).
- **Multiple tabs** share the database but not in-memory locks; a newer tab's upgrade waits for older connections to
  close (`onversionchange`).

## Service workers and PWAs

- **What a service worker is.** A JS script between the page and the network. Here it caches the **app shell**, so the
  app starts offline. The **data** is a separate layer (IndexedDB).
- **Lifecycle:** install → wait → activate. A new version installs in the background and **waits** until no tab uses
  the old one, so pages never mix versions. Offer "Reload to update", which posts `SKIP_WAITING` and reloads on
  `controllerchange` (`UpdateBanner`).
- **Blazor specifics:** the SDK writes `service-worker-assets.js` (every file + SHA-256 + version). Install with
  `integrity`. The real worker only exists in a **published** build. A service worker runs JS, not .NET, so Background
  Sync can't run C# sync code after the tab closes.
- **Gotchas found here:**
  - the template's allow-list missed `.woff2` and `.svg`;
  - the icon font's `?hash` defeated exact cache matching (`ignoreSearch`);
  - don't route cross-origin requests through the worker;
  - every app-shell file must be same-origin (no CDNs).
- **Must be served over HTTPS** (or `localhost`).

## Connectivity

- `navigator.onLine === false` is reliable. `true` only means "on *a* network": captive portals, dead Wi-Fi and a down
  server all report `true`. So a failed request also counts as offline (`Unreachable`) until one succeeds.
- Don't hammer retries while certainly offline: pause and resume on the `online` event.
- Simulating offline in the HTTP handler pipeline exercises the real code paths (`ConnectivityHandler`).

## Clean Architecture in a browser app

- **Domain and Application are plain .NET** with no browser knowledge, so they're unit tested without a browser.
- **Ports** (`IProductRepository`, `IOutboxStore`, `IProductApiClient`, `IConnectivityService`) are implemented in
  Infrastructure with JS interop and `HttpClient`.
- **The composition root is the browser.** `Program.cs` in the client is the only place that sees implementations.
  Architecture tests stop inner layers referencing outer ones.
- **The payoff, demonstrated:** pages call `IProductService` throughout. Moving from online-only to local-first
  (Phases 1 → 3) didn't change what any page calls.

## Testing

- **Pure functions for the rules:** coalescing, merging, backoff and search are unit tested without mocks.
- **A stateful in-memory fake** of the stores for the sync engine. It stores *copies*, so "mutated but not saved" bugs
  fail tests.
- **bUnit** for components: rendering, validation, focus management, live updates from services (`Client.Tests`).
- **Gaps, and how they were covered:** JS modules and the service worker aren't covered by `dotnet test`. They were
  checked with Node against `fake-indexeddb` and a mocked worker environment. A production project would add Playwright
  end-to-end tests, including offline start-up.

## What I'd do differently in production

- **A real backend** with persistence, ETag/`If-Match` conflict detection, server-side idempotency, and delta sync
  (`?updatedSince=`) instead of refreshing the whole catalogue. Paged sync for large datasets.
- **Authentication:** token refresh that works offline (queue requests, refresh on reconnect), and per-user database
  names so a shared browser doesn't mix users' data.
- **Conflict UI:** show both versions and let the user choose, rather than failing the entry.
- **Multi-tab coordination:** Web Locks API or `BroadcastChannel`, so only one tab syncs and others refresh when it does.
- **Storage:** `navigator.storage.persist()` so the browser won't evict data under pressure; quota monitoring; tombstone
  purging.
- **Observability:** log sync failures to the server when back online; Playwright tests in CI, including offline
  start-up; Lighthouse PWA checks.
- **Smaller downloads:** the `wasm-tools` workload (relinking), lazy-loaded assemblies, and AOT where CPU-heavy work
  justifies the bigger download.
