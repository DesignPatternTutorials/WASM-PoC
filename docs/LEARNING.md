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

## Phase 2: IndexedDB local store

### What was built

- **`wwwroot/js/indexedDb.js`** (in Infrastructure): a small, hand-written ES module. Versioned schema with a
  migrations list; a `products` store with `byServerId` and `byListOrder` (`[deleted, titleKey]`) indexes; a `meta`
  key/value store; paging with a cursor and `advance()`; all-or-nothing batch writes.
- **`IndexedDbProductRepository`** implements the new `IProductRepository` port by importing that module through
  `IJSRuntime` and calling its functions. `ProductRecord` is the shape that crosses the interop boundary.
- **Local identity:** `Product.Id` is now a `Guid` generated on this device. `ServerId` and `ServerVersion` are
  bookkeeping, and routes are `/products/{guid}`.
- **`ServerCopyMerger`** (pure, unit tested): decides how server data combines with what's stored.
- **`ProductService`**: reads from local storage, refreshes the whole catalogue in the background (at most once a minute
  unless forced), searches the server with a fallback to the device, and still writes server-first.
- **UI:** a freshness line on the list ("Saved on this device, updated 2 minutes ago" / "Offline…"), a source badge
  on search results, and a sync note plus local/server ids on the detail page.

### Key concepts

**Stale-while-revalidate.** Show what's stored *now*, then ask the server for anything newer and re-render if it
changed. The user never waits on the network to see data, and the network failing just means "no newer data". See
`ProductList.OnParametersSetAsync` and `ProductDetail.OnParametersSetAsync`.

**Local identity vs server identity.** A local-first app can't key its data by the server's id: the record may exist
before the server has seen it (Phase 3), and here DummyJSON gives every create id 195. So the local GUID is the key and
the server id is just a field. In interviews this is the "client-generated ids" point. (A GUID also doubles as an
idempotency key; see Phase 3.)

**Versions decide whether the server wins.** A refresh only replaces a local copy when the server's version token
differs from the one the copy was based on (`Product.ShouldAcceptServerCopy`). The token is opaque and only compared for
equality, like an ETag. That one rule is why:
- an edit survives a refresh even though DummyJSON forgot it (same version → nothing new to apply);
- a delete sticks (the tombstone never accepts server copies);
- Phase 3's pending changes will be safe (only `Synced` copies accept server data).

Against a real API, a successful PUT returns a *new* version, so the rule still holds, and a change made elsewhere
arrives because its version differs. What it can't do on its own is detect a *conflict* (both sides changed). That
needs the server to check the version on write (`If-Match` → `412 Precondition Failed`).

**Tombstones.** Deleting marks the record `deleted` instead of removing it, so a refresh can't resurrect it. A real
system eventually purges tombstones once the server confirms the delete and stops returning the item.

**The JS interop boundary** (`IndexedDbProductRepository`, `ProductRecord`):
- Arguments and results are serialised with System.Text.Json (camelCase). JS sees plain objects. `Guid` becomes a
  string, `decimal` becomes a double, and enums are stored by name so they read well in DevTools.
- Everything is async. The `CancellationToken` only stops .NET waiting, and the JS work carries on.
- `import()` returns an `IJSObjectReference` to the module. It's created lazily, once, and disposed with the repository.
- `IJSInProcessRuntime` (WASM-only) allows synchronous calls, but IndexedDB is asynchronous anyway.

**IndexedDB essentials** (all in `indexedDb.js`):
- Records are schemaless, but **stores and indexes are versioned**. Only `upgradeneeded` can change them. The
  migrations list runs every step from the user's old version to the current one.
- **Indexes skip invalid keys.** Records with `serverId: null` are left out of `byServerId`. Booleans aren't valid keys,
  so `deleted` is 0/1.
- **Transactions auto-commit** when they have no pending requests. Awaiting anything else mid-transaction lets them
  commit early.
- **A synchronous `put()` error doesn't abort the transaction.** See the gotcha below.

**Concurrency without threads.** WASM is single-threaded, but every `await` lets other work run. `ProductService`
takes a `SemaphoreSlim` around each read-merge-write so a background refresh can't write back a stale copy over a
save. It only protects this tab; two tabs share the database but not the lock.

### Gotchas

- **All-or-nothing needs an explicit `abort()`.** `store.put()` can throw synchronously (e.g. a record with no valid
  key). That exception doesn't abort the transaction, so the earlier puts in the batch still commit. A Node smoke test
  against `fake-indexeddb` caught this, and `writeAll()` now aborts on any synchronous error.
- **Passing an array to `params object?[]`.** `module.InvokeVoidAsync("putProducts", token, records)` would spread a
  `ProductRecord[]` into one JS argument per record (array covariance). Wrap it as `[records]`.
- **DummyJSON's PUT response has no `meta`.** If an update stored "no version", the next refresh would see a version
  where there was none and overwrite the edit. `UpdateAsync` keeps the previous version when the response has none
  (there's a unit test for this).
- **Search differs offline.** DummyJSON's search rules aren't documented. The device search matches every word in
  title, brand, category or description, so results can differ a little. The badge says which source answered.
- **The catalogue is refreshed whole.** That's fine for 194 products (~25 KB compressed). A large dataset would need
  delta sync (`?updatedSince=`), which DummyJSON doesn't offer.

### Things to try

- **Look inside the database.** DevTools → Application → Storage → IndexedDB → `offline-crud` → `products`. Expand
  `byListOrder` to see the index order. `meta` holds `catalogueLastRefreshed`.
- **Browse offline.** Load `/products` once online, then DevTools → Network → Offline. Page through, open products,
  search: it all still works, and the freshness line says "Offline". Searching shows "Results from this device".
- **Watch the revalidation.** Throttle to Slow 4G and open `/products`. The saved list appears instantly, and the line
  says "Checking DummyJSON for updates…" until the response arrives.
- **Edit survives refresh.** Edit a title and save, then "Refresh now" on the list. The edit stays (same server version).
  Compare Phase 1, where it vanished.
- **Delete sticks.** Delete a product, then "Refresh now". It stays gone. Look for the record with `deleted: 1` in
  DevTools.
- **Create shows up.** Create a product: it's in the list under its local id. Its detail page says "Only on this device".
  Try editing it and see the 404 from DummyJSON (Phase 3 turns this into a Failed outbox entry).
- **Start from empty.** DevTools → Application → Storage → "Clear site data", then reload `/products` online. You'll
  see "Downloading the catalogue…". Do it offline instead and you'll see the empty-state message.
- **Schema upgrade.** Add a version-2 step to `migrations` (e.g. a new index), reload, and watch `oldVersion` /
  `newVersion` in a breakpoint on `onupgradeneeded`. Phase 3 does this for real when it adds the `outbox` store.

## Phase 3: Outbox + sync engine

### What was built

- **Local-first writes.** `ProductService.CreateAsync` / `UpdateAsync` / `DeleteAsync` no longer touch the network.
  Each one writes the product **and** its outbox entry in one IndexedDB transaction (`IOutboxStore.CommitAsync` →
  `commit()` in `indexedDb.js`), then calls `ISyncTrigger.RequestSync()`. Create, edit and delete work offline.
- **Schema version 2** adds the `outbox` store (indexes `byEntity`, `byCreated`). Existing products and metadata are
  untouched by the upgrade.
- **`OutboxPlanner`** (pure, tested) decides coalescing and derives each product's `SyncState` from what's queued.
- **`SyncService`** is the engine: FIFO per product, mark in flight, send with `Idempotency-Key`, then complete,
  retry or fail. **`RetryPolicy`** gives exponential backoff with jitter.
- **`SyncScheduler`** (Client) is the one loop that runs the engine: at start-up (after recovering in-flight entries),
  after each local write, every 30 s, and on "Sync now".
- **UI:** a sync status line on the list ("2 changes waiting to sync · Sync now"), sync badges on rows, and the sync
  state on the detail page. Both pages re-read local data whenever a sync run finishes.

### Key concepts

**The transactional outbox.** A change and the instruction to send it are written atomically. Without that, a crash
between the two writes leaves either a change that's never sent or a request for a change the user can't see. IndexedDB
transactions can span several stores if all are named when the transaction opens (`db.transaction([PRODUCTS, OUTBOX])`).
On the server, the same pattern is "write the row and the message in one DB transaction, publish later".

**The sync state is derived, never set by hand.** A product is `Synced` exactly when no outbox entries remain for it.
Otherwise its state is `Failed` if any entry failed, or else the pending state of its newest entry
(`OutboxPlanner.DeriveSyncState`). After every write and every sync step the state is recomputed from the queue. That's
what keeps an edit made *while a create is in flight* showing as `PendingUpdate` instead of being marked `Synced` when
the create returns.

**Coalescing** (`OutboxPlanner.Plan`). A new change merges into the newest entry for that product unless that entry is
in flight:

| Queued | New change | Result |
| --- | --- | --- |
| nothing | create / update / delete | one new entry |
| create | update | one create with the latest details |
| update | update | one update with the latest details |
| create (not sent) | delete | **both dropped**: the server never hears about it |
| update | delete | just the delete |
| in-flight anything | anything | a new entry queued behind it |
| failed | update | the failed entry, reset to pending with the new details (editing is how you fix it) |

The trade-off: fewer requests and fewer failures, but the server never sees intermediate states. That's fine when only
the latest state matters, and wrong for audit logs or event sourcing.

**Ordering.** Entries for one product are sent oldest first, and a product's later entries wait if an earlier one is
failed, backing off or retrying (an update can't go before its create). Different products are independent, so one
product's 5xx doesn't block another's. A *network* failure stops the whole run, because every request would fail the
same way and each failure would add another attempt.

**Classifying failures.** This comes from `ApiException.IsTransient`, built in Phase 1:
- Network, 5xx, 429: retry with backoff. `Attempts`, `NextAttemptAt` and `LastError` are recorded. There's no
  attempt limit, because being offline for a day is normal.
- Other 4xx, or an unreadable response: `Failed`, with a readable `LastError`. The product shows "Sync failed" and the
  entry is never retried automatically. (Retry/Discard buttons come in Phase 4.)
- **Delete + 404 counts as success.** DELETE is idempotent: the goal is "the server doesn't have it", and a 404 says
  exactly that. **Update + 404 fails** with a reason that explains DummyJSON's non-persistence.

**Exponential backoff with jitter** (`RetryPolicy`). 2 s, 4 s, 8 s … capped at 5 minutes. "Equal jitter" keeps half
of each delay fixed and randomises the other half, so a thousand clients coming back online at once don't retry in
lock-step (the "thundering herd").

**Idempotency keys.** Every write carries `Idempotency-Key: <entry id>`. If the tab closes while a request is out, the
entry is still `InFlight` on the next start. `RecoverAsync` resets it to `Pending` and it's sent again. The server may
already have applied it, and a server that honours the key recognises the repeat and returns the original result instead
of creating a duplicate. DummyJSON ignores the header (see "How this would work against a real API" below).

**One run at a time.** The scheduler is a single loop, and `SyncService.RunAsync` also refuses to overlap
(`SemaphoreSlim.WaitAsync(0)`). Inside a run, each entry is **re-read under the store lock** just before sending,
because the user may have merged a newer edit into it (or removed it) since the run took its snapshot.

**Why sync only runs while the app is open.** Background Sync lets a *service worker* run after the tab closes, but
service workers run JavaScript, and the .NET runtime only exists inside the open tab. A JS-only background sync would
mean the service worker reading the IndexedDB outbox itself and replaying it with `fetch()` in a `sync` event, which
duplicates the engine's rules in JS. Background Sync isn't available in Safari or Firefox either.

**A correction to Phase 1's "scoped = singleton".** Components resolve services from one long-lived scope, but
`Program.cs` resolves from the *root* provider. The scheduler is started from the root, so anything it shares with the
pages (above all `LocalStoreLock`) has to be a real singleton. A scoped registration would quietly produce two instances,
and the lock would lock nothing. All app-lifetime services are now registered as singletons.

### How this would work against a real API

DummyJSON doesn't persist anything, so some things can't be shown for real:

- **Creates would stick**, and the server id recorded after a create would be usable for later updates. Here,
  every create gets id 195 and later updates of it fail with 404, which is the deliberate "Failed with a readable reason"
  path.
- **Idempotency** needs the server to store `Idempotency-Key` → response for a while (typically 24 h), and to return the
  stored response when a key repeats. Without that, retrying after a lost response can create duplicates.
- **Conflict detection** needs versions on writes. The client sends `If-Match: <ServerVersion>` (an ETag or row
  version). If someone else changed the record, the server answers `412 Precondition Failed` and the client must decide:
  keep its version (re-send without the precondition), take the server's, or ask the user to merge. Here a 412 would
  land as a `Failed` entry with a readable reason. Resolving it is a UI decision.
- **Version returned on write.** A real PUT returns the new ETag. Storing it keeps the next refresh from re-applying our
  own change (DummyJSON's PUT returns no version, so the old one is kept).
- **Tombstone purge.** Once a delete is confirmed and the server stops returning the item, the local tombstone can be
  removed.

### Gotchas

- **`Assert.Multiple(async () => …)`** binds to an `async void`-style delegate, and assertions after the first `await`
  can run after the test has finished. Await first, then assert.
- **Mutating isn't saving.** `Product` is a mutable class, so code can change one and forget to commit it. The
  in-memory test store (`InMemoryLocalStore`) keeps *copies*, like IndexedDB's structured clones, so such a bug fails a
  test instead of passing by accident.
- **Commit with `CancellationToken.None`.** Once the decision to write is made, the .NET side must not stop waiting
  halfway while the JS transaction commits anyway. The caller would then wonder whether it happened.
- **A snapshot can go stale mid-run.** The engine's first draft marked entries in flight from the snapshot it took at the
  start of the run, which would have overwritten an edit merged in meanwhile. It now re-reads each entry under the lock
  (`SyncServiceTests.EntryChangedAfterTheRunStarted_IsSentWithItsLatestPayload`).

### Things to try

- **The whole point.** Go offline (DevTools → Network → Offline). Create a product, edit another, delete a third. Each
  change shows immediately with a "not synced" badge, and the status line says "3 changes waiting to sync". Look at
  DevTools → Application → IndexedDB → `offline-crud` → `outbox`. Go back online and press "Sync now" (or wait up to
  30 s): watch the outbox drain and the badges disappear.
- **Coalescing.** Offline, create a product, edit it twice, then delete it. The outbox goes 1 → 1 → 1 → 0 entries.
  Nothing is ever sent.
- **Backoff.** Offline, edit a product and keep the list open. Each time the entry comes due it's tried again, and its
  `attempts` and `nextAttemptAt` grow (about 2 s, 4 s, 8 s …, ± jitter). The status line shows "Next retry in N
  seconds". Pressing "Sync now" while it's backing off doesn't add attempts: an entry isn't sent before its time. (The
  scheduler wakes when the earliest retry is due, so short backoffs aren't stretched to the 30-second tick.)
- **A permanent failure.** Online, create a product and wait for it to sync (it gets server id 195). Then edit it. The
  update gets a 404 and the product shows "Sync failed". The entry's `lastError` explains why. Edit it again: the failed
  entry becomes pending and is tried once more.
- **Recovery.** Throttle to Slow 3G, make an edit, and close the tab while the request is out. Reopen: the in-flight
  entry is reset and sent again, with the same `Idempotency-Key` (check the request headers).
- **Schema upgrade, for real.** If you ran Phase 2, your browser has a version-1 database. Loading this build upgrades it
  to version 2. The products are still there, and there's a new `outbox` store.
