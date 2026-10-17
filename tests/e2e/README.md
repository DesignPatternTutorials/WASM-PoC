# End-to-end tests

Browser tests for the Offline CRUD app, written with [Playwright](https://playwright.dev) and run in the
**Microsoft Edge already installed** on the machine. Nothing extra is downloaded: `channel: 'msedge'` in
`playwright.config.mjs` (change it to `'chrome'` for Google Chrome).

They cover what `dotnet test` can't: the app running in a real browser, IndexedDB, the service worker, and real network
loss. Each test starts its own server on its own port (5310, 5311, 5320), so a Visual Studio session on 5121/7227 isn't
affected.

## Running

Needs Node.js 20+ and the .NET SDK. From this folder:

```powershell
npm install               # once
npm test                  # publishes a Release build, then runs everything (about a minute after the first build)
npm run test:dev          # dev-server tests only (no publish needed)
npm run test:published    # publishes, then runs the offline start-up test
npm run report            # open the HTML report from the last run (screenshots and traces of failures)
```

Run one test by name: `npx playwright test -g "starts offline"`. Watch the browser: add `--headed`.

## What's covered

| Project | Spec | What it proves |
| --- | --- | --- |
| `dev-server` | `startup.spec.mjs` | The app starts in a fresh profile three times running, with no failed requests or console errors. |
| `dev-server` | `offline-flow.spec.mjs` | The local-first pattern through the UI: data downloads, "Simulate offline", an edit is saved and queued, the Sync page shows it paused, going online syncs it, and the edit survives a catalogue refresh. |
| `published` | `offline-startup.spec.mjs` | The spec's definition of done. After one online visit, with the **server stopped and the network off**: the app starts from the service worker cache, icons load, deep links work, saved products list, a create, an edit and a delete are accepted and queued, nothing is sent, and when the network returns exactly one POST, one PUT and one DELETE reach DummyJSON. |

## Notes

- **The tests talk to the real DummyJSON.** They need internet access (except while a test deliberately cuts it). DummyJSON
  doesn't persist writes, so the tests can't damage anything, but they can fail if it's down or rate-limits.
- **Why two projects.** Only the published build has the caching service worker, so offline *start-up* can't be tested
  against `dotnet run`. `npm run publish` writes the build to `artifacts/publish`.
- **Offline, the strict way.** The published test stops the static server (nothing answers on the app's origin), turns
  the browser's network off (`context.setOffline`) and blocks DummyJSON. The app can only come from the cache.
- **Keep Playwright reasonably current.** Playwright 1.48 hung silently on Node 24, before running a single test (even
  `--list` never returned). If a run prints nothing at all, check that the Playwright version supports your Node.
- **Server logs.** Each server's output goes to `test-results/server-<port>.log`, which is the first place to look if a
  test times out waiting for a server.
- **Expected console noise offline.** Product images (DummyJSON's CDN) and API calls fail with `ERR_INTERNET_DISCONNECTED`;
  `withoutOfflineNoise` filters those out before asserting there were no errors.
