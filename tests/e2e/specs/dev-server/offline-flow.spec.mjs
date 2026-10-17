// The local-first pattern, driven through the UI with the "Simulate offline" switch:
// load data → go offline → edit → see it queued → go online → see it sync → the edit survives a refresh.
import { test, expect, waitForAppStart } from '../../support/fixtures.mjs';
import { startDevServer } from '../../support/servers.mjs';

const PORT = 5311;
let server;

test.beforeAll(async () => { server = await startDevServer(PORT); });
test.afterAll(async () => { await server?.stop(); });

test('an edit made offline is queued, then synced when back online', async ({ page, pageErrors }) => {
  await test.step('the catalogue downloads into IndexedDB', async () => {
    await page.goto(`${server.url}products`);
    await waitForAppStart(page);
    await expect(page.getByText(/Saved on this device, updated/)).toBeVisible();
    await expect(page.locator('.page-header p')).toHaveText(/194 products/);
  });

  await test.step('simulate offline shows the banner and a toast', async () => {
    await page.locator('#simulate-offline').check();
    await expect(page.getByText('Simulated offline: the app is blocking its own requests.')).toBeVisible();
    await expect(page.getByText(/Simulating offline/)).toBeVisible();
  });

  let title;
  await test.step('an edit made offline is saved locally and queued', async () => {
    const firstRow = page.locator('tbody tr').first();
    title = (await firstRow.locator('a.fw-semibold').innerText()).trim();
    await firstRow.locator('a[title=Edit]').click();
    await page.locator('#title').fill(`${title} (edited offline)`);
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.getByText('Saved on this device').first()).toBeVisible();
    await expect(page.locator('a[href=sync] .badge')).toHaveText(/^1/);
  });

  await test.step('the sync page shows it pending, with syncing paused', async () => {
    await page.locator('a[href=sync]').click();
    await expect(page.getByText("Syncing is paused while you're offline")).toBeVisible();
    await expect(page.locator('tbody tr')).toHaveCount(1);
    await expect(page.locator('tbody tr')).toContainText('Update');
    await expect(page.locator('tbody tr')).toContainText('Pending');
  });

  await test.step('back online, the outbox drains', async () => {
    await page.locator('#simulate-offline').uncheck();
    await expect(page.getByText(/Synced 1 change to DummyJSON/)).toBeVisible();
    await expect(page.getByText('Nothing waiting')).toBeVisible();
    await expect(page.locator('a[href=sync] .badge')).toHaveCount(0);
  });

  await test.step('the edit survives a catalogue refresh', async () => {
    // DummyJSON forgets the edit, but the server version hasn't changed, so the refresh leaves the local copy alone.
    await page.locator('a[href=products]').first().click();
    await page.getByRole('button', { name: 'Refresh now' }).click();
    await expect(page.getByText(/Saved on this device, updated just now/)).toBeVisible();
    await expect(page.locator('tbody tr').first()).toContainText(`${title} (edited offline)`);
  });

  expect(pageErrors).toEqual([]);
});
