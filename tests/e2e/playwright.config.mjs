import { defineConfig } from '@playwright/test';

// Two projects, because the app behaves differently depending on how it's served:
//   dev-server - `dotnet run`: the no-op service worker, nothing cached. Start-up and the in-app offline flow.
//   published  - the `dotnet publish` output: the caching service worker. Offline START-UP can only be tested here.
// Each test starts (and stops) its own server on its own port, so a Visual Studio session on 5121/7227 isn't affected.
export default defineConfig({
  testDir: './specs',
  timeout: 180_000,
  expect: { timeout: 30_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    // The installed Microsoft Edge: nothing to download. Change to 'chrome' to use Google Chrome instead.
    channel: 'msedge',
    headless: true,
    viewport: { width: 1280, height: 900 },
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'dev-server', testMatch: /dev-server\/.*\.spec\.mjs/ },
    { name: 'published', testMatch: /published\/.*\.spec\.mjs/ },
  ],
});
