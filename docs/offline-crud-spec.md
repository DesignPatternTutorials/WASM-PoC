# Spec: Offline-first Blazor WebAssembly CRUD app (teaching project)

## Purpose

This is a learning project. The user is an experienced .NET / Blazor Server developer with no
production Blazor WebAssembly experience, preparing for interviews. The goal is a complete,
working, heavily commented example that shows:

1. How Blazor WebAssembly differs from Blazor Server (runs in the browser, single-threaded,
   HttpClient is the browser's fetch, no server connection to lean on).
2. How the **offline problem** arises in a browser app, and how a **local-first write +
   outbox** pattern backed by **IndexedDB** solves it.
3. Clean Architecture applied to a browser-hosted client.

Explaining _why_ matters as much as working code. Treat every phase as a lesson.

## Before you start

- Read CLAUDE.md and follow it (Clean Architecture, feature branches off `dev`,
  Conventional Commits, C# / Microsoft conventions, NUnit + Moq).
- Run `dotnet --list-sdks` and use the latest LTS SDK installed. Pin it in `global.json`.
- Verify the external API's behaviour with `curl` before writing client code (see below).
  Record what you find in `docs/api-notes.md`. Don't trust this spec over the real API.
- Work in the phases below. **One feature branch per phase. Stop at the end of each phase**,
  summarise what was built and what to look at, and wait for the user to review and merge.

## External API: DummyJSON

Use **https://dummyjson.com**, `products` resource. Reasons:

- Supports GET, search (`/products/search?q=`), paging (`limit` / `skip`), and
  simulated POST (`/products/add`), PUT/PATCH (`/products/{id}`) and DELETE (`/products/{id}`).
- Writes return realistic responses and status codes but are **not persisted**.
- CORS is enabled, so the WASM client can call it directly.

(SWAPI was considered but is read-only, so it can't demonstrate writes.)

### Non-persistence is a teaching point, not just a limitation

Because writes don't persist:

- A product created via POST gets a server id back, but a later GET for that id will 404.
- Updating or deleting a locally created product on the server will likely fail.

Handle this deliberately and explain it in comments and docs:

- IndexedDB is the **source of truth for the UI**. The API is a sync target.
- Map "item exists locally but the API says 404" to a clear, non-crashing outcome
  (mark the outbox entry as failed with a readable reason; don't retry forever).
- Add a short section to `docs/LEARNING.md` explaining how this would behave against a real
  API, and what extra would be needed (conflict detection with ETags / row versions,
  idempotency keys honoured by the server).

## Solution structure

Update the Architecture section of CLAUDE.md in Phase 0 to match this, since there is no
API host project for now:

```
src/
  OfflineCrud.Domain/          Entities, value objects. No dependencies.
  OfflineCrud.Application/     Use cases, interfaces (IProductRepository, IOutboxStore,
                               IProductApiClient, IConnectivityService, ISyncService), DTOs.
  OfflineCrud.Infrastructure/  Browser implementations: IndexedDB via JS interop,
                               HttpClient for DummyJSON, connectivity via JS events.
  OfflineCrud.Client/          Blazor WASM (PWA template). UI, DI composition root.
tests/
  OfflineCrud.Application.Tests/   NUnit + Moq: sync engine, outbox logic, coalescing.
  OfflineCrud.Client.Tests/        bUnit component tests for key pages (optional, phase 6).
```

Use `dotnet new blazorwasm --pwa` for the client (standalone, no hosted server).
Choose project names sensibly if the repo name suggests better ones; keep the layering.

## The offline pattern to implement

### Writes (create / update / delete)

1. Validate the form.
2. In **one IndexedDB transaction**, write the change to the `products` store
   (with a `syncState`: `Synced`, `PendingCreate`, `PendingUpdate`, `PendingDelete`, `Failed`)
   **and** append an entry to the `outbox` store. Explain why one transaction matters
   (no state where the UI shows a change that will never be sent, or vice versa).
3. Update the UI immediately (optimistic).
4. If online, trigger the sync engine. If offline, the entry waits.

### Outbox entry

`id` (GUID, doubles as idempotency key), `operation`, `entityId` (local id),
`payload` (JSON), `createdAt`, `attempts`, `nextAttemptAt`, `lastError`, `status`
(`Pending`, `InFlight`, `Failed`).

### Sync engine (`ISyncService`)

- Process entries **in order per entity** (FIFO).
- Classify results: network error / 5xx / 429 → retry with exponential backoff and jitter;
  4xx → mark `Failed` with reason, surface it in the UI, don't retry automatically.
- On successful create, record the server id against the local entity.
- **Coalescing**: create then delete before sync → drop both; multiple updates → send latest.
  Explain the trade-offs in comments.
- Guard against overlapping runs (one sync at a time).
- Triggers: app start, coming back online, after a local write, "Sync now" button,
  and a periodic timer while the app is open.
- Explain in comments why sync only runs while the app is open: the service worker runs JS,
  not .NET, so the Background Sync API can't run this C# code. Mention what a JS-only
  background sync would involve as a stretch idea.

### Reads

- List and detail pages read from IndexedDB first, then refresh from the API when online
  (stale-while-revalidate). Don't overwrite local items that have pending changes.
- Search: when online, use `/products/search`; when offline, search the local store.
  Show which source answered.

### Connectivity (`IConnectivityService`)

- Use `navigator.onLine` plus `online` / `offline` events via JS interop.
- Explain that `navigator.onLine` can be true with no real internet, so a failed HTTP call
  must also count as offline.
- Add a **"Simulate offline" toggle** in the UI that forces the service offline, so the
  pattern can be demonstrated without DevTools. Also document using DevTools → Network → Offline.

### IndexedDB access

Write a small hand-rolled JS module (`wwwroot/js/indexedDb.js`) loaded with
`IJSRuntime.InvokeAsync<IJSObjectReference>("import", ...)`, rather than a third-party
package, so the interop is visible and learnable. Version the schema and handle upgrades.
Explain the C# ↔ JS boundary (serialisation, async, disposal).

### App shell offline (service worker)

- Use the PWA template's service worker to cache the app shell.
- Explain clearly: the caching service worker only runs in a **published** build, not under
  `dotnet run`. Add a script or documented commands to publish and serve locally to test
  true offline start-up.
- All CSS, JS, fonts and icons must be served locally (no CDNs), or the app breaks offline.

## UI

Bootstrap 5 (local copy), skinned to look polished: a custom theme via Bootstrap CSS
variables / Sass or a locally hosted Bootswatch theme, Bootstrap Icons (local), consistent
spacing, responsive layout, light/dark aware if practical.

Pages:

- **Splash / home**: hero section, what the app demonstrates, links into the app.
- **About**: explains the architecture and the offline pattern, with a diagram
  (Mermaid in docs is fine; a simple visual on the page is better).
- **Products list**: paged table/cards, search box, sync-state badge per row,
  empty and loading states.
- **Product detail**.
- **Create / Edit**: `EditForm` with validation messages; disabled submit while saving.
- **Delete**: confirmation modal.
- **Sync / Outbox**: queued entries, status, attempts, last error, "Sync now",
  "Retry" / "Discard" for failed entries, "Clear local data" (with confirmation).
- **Global**: online/offline banner, pending-changes count in the nav, toasts for outcomes.

## Comments and learning material

- XML doc comments on public types and members.
- Explanatory comments marked `// LEARN:` wherever a WASM-specific or offline-specific
  decision is made, so the user can `grep -rn "LEARN:"` to find every lesson.
- Maintain `docs/LEARNING.md` as you go: one section per phase covering what was built,
  key concepts, gotchas, and "things to try" (e.g. go offline, create two items, edit one,
  come back online, watch the outbox drain).
- Add `docs/interview-notes.md`: concise talking points on WASM vs Server, offline-first,
  outbox pattern, IndexedDB, service workers, trade-offs and what you'd do in production.

## Phases

0. **Scaffold**: solution, projects, references matching the layering, `global.json`,
   `Directory.Build.props`, update CLAUDE.md (structure, real build/run/test/publish commands,
   status). Verify DummyJSON with curl, write `docs/api-notes.md`.
1. **Online-only CRUD**: Domain, Application interfaces, HttpClient API client,
   all pages working directly against the API. Bootstrap skin, splash and about pages.
   (This is the "before" picture: show what breaks when offline.)
2. **IndexedDB local store**: JS module, repository, read-from-local, stale-while-revalidate.
3. **Outbox + sync engine**: transactional local writes, sync service, retries, coalescing,
   unit tests with NUnit + Moq.
4. **Connectivity + UX**: online/offline detection, simulate-offline toggle, banner,
   badges, sync page, toasts.
5. **PWA / app shell offline**: service worker caching, publish-and-serve instructions,
   offline start-up test.
6. **Polish**: bUnit tests for key components, accessibility pass, finish
   `LEARNING.md` and `interview-notes.md`.

## Also consider (ask before doing)

- Replacing DummyJSON with a small ASP.NET Core minimal API in the solution (SQLite or
  in-memory) so persistence, ETag-based conflict detection and server-side idempotency can be
  demonstrated for real.
- A GitHub Actions workflow running build and tests on PRs to `dev`.

## Definition of done

- `dotnet build` with no warnings, `dotnet test` passes.
- The app works online; with the network off (published build), it starts, lists cached data,
  accepts creates/edits/deletes, shows them as pending, and syncs them when back online.
- Every `// LEARN:` comment and `docs/LEARNING.md` section is accurate to the final code.
