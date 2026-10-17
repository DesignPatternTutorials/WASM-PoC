// Shared test helpers: collect page errors so every test can assert there were none.
import { test as base, expect } from '@playwright/test';

export const test = base.extend({
  // Page errors and console errors, minus the ones a test declares expected (e.g. failed image loads while offline).
  pageErrors: async ({ page }, use) => {
    const errors = [];
    page.on('pageerror', error => errors.push(`page error: ${error.message}`));
    page.on('console', message => { if (message.type() === 'error') errors.push(`console.error: ${message.text()}`); });
    await use(errors);
  },
});

export { expect };

/** Waits until Blazor has replaced the loading screen with the app's layout. */
export async function waitForAppStart(page) {
  await expect(page.locator('.app-shell')).toBeVisible({ timeout: 60_000 });
}

/** Errors that are expected while the network is off: product images and DummyJSON calls can't load. */
export function withoutOfflineNoise(errors) {
  return errors.filter(error => !/ERR_INTERNET_DISCONNECTED|Failed to load resource/.test(error));
}
