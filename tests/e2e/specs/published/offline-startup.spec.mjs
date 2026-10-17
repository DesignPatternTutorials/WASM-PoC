// The spec's definition of done, end to end, against the PUBLISHED build:
// with the server stopped AND the network off, the app starts, lists cached data, accepts creates/edits/deletes,
// shows them as pending, and syncs them when the network is back.
import { test, expect, waitForAppStart, withoutOfflineNoise } from '../../support/fixtures.mjs';
import { startPublishedServer } from '../../support/servers.mjs';

const PORT = 5320;

test('starts offline from the service worker cache, works offline, then syncs', async ({ page, context, pageErrors }) => {
  const server = await startPublishedServer(PORT);
  const writesSent = [];
  page.on('request', request => {
    if (request.url().includes('dummyjson.com') && request.method() !== 'GET') {
      writesSent.push(`${request.method()} ${new URL(request.url()).pathname}`);
    }
  });

  try {
    await test.step('first visit online: the service worker installs and the catalogue downloads', async () => {
      await page.goto(server.url);
      await waitForAppStart(page);
      const cachedFiles = await page.evaluate(async () => {
        await navigator.serviceWorker.ready;
        const name = (await caches.keys()).find(key => key.startsWith('offline-cache-'));
        return (await (await caches.open(name)).keys()).length;
      });
      expect(cachedFiles).toBeGreaterThan(70);

      await page.goto(`${server.url}products`);
      await expect(page.getByText(/Saved on this device, updated/)).toBeVisible();

      // Reload once so the page is controlled by the (now active) worker.
      await page.reload();
      await waitForAppStart(page);
      expect(await page.evaluate(() => !!navigator.serviceWorker.controller)).toBe(true);
    });
  } finally {
    // The strictest kind of offline: nothing is listening on the app's origin any more.
    await server.stop();
  }

  const blockDummyJson = route => route.abort('internetdisconnected');
  await context.setOffline(true);
  await context.route('**/*dummyjson.com/**', blockDummyJson);

  await test.step('starts from the cache with no server and no network', async () => {
    await page.goto(server.url);
    await waitForAppStart(page);
    await expect(page.getByText("You're offline.")).toBeVisible();
  });

  await test.step('the icon font comes from the cache despite its ?hash query string', async () => {
    expect(await page.evaluate(async () => { await document.fonts.ready; return document.fonts.check('16px bootstrap-icons'); })).toBe(true);
  });

  await test.step('a deep link loads offline', async () => {
    await page.goto(`${server.url}sync`);
    await expect(page.getByRole('heading', { name: 'Sync', level: 1 })).toBeVisible();
  });

  await test.step('products show the copy saved on this device', async () => {
    await page.goto(`${server.url}products`);
    await expect(page.locator('tbody tr').first()).toBeVisible();
    // Either the refresh was skipped (fetched under a minute ago) or it failed because we're offline.
    await expect(page.getByText(/Showing the copy saved on this device|Saved on this device, updated/).first()).toBeVisible();
  });

  await test.step('create offline (categories come from the products on this device)', async () => {
    await page.getByRole('link', { name: 'New product' }).click();
    await page.locator('#title').fill('Offline lamp');
    await page.locator('#category').selectOption({ index: 1 });
    await page.locator('#description').fill('Made with the network off.');
    await page.locator('#price').fill('12.5');
    await page.locator('#stock').fill('3');
    await page.getByRole('button', { name: 'Create product' }).click();
    await expect(page.getByText(/Saved on this device under local id/)).toBeVisible();
  });

  await test.step('delete and edit offline', async () => {
    await page.goto(`${server.url}products?page=2`);
    const rows = page.locator('tbody tr');
    await expect(rows.first()).toBeVisible();
    const toEdit = (await rows.nth(0).locator('a.fw-semibold').innerText()).trim();
    const toDelete = (await rows.nth(1).locator('a.fw-semibold').innerText()).trim();

    await rows.nth(1).locator('button[title=Delete]').click();
    await page.locator('[role=dialog] .btn-danger').click();
    await expect(page.getByText(`Deleted “${toDelete}” on this device`)).toBeVisible();

    await rows.nth(0).locator('a[title=Edit]').click();
    await page.locator('#title').fill(`${toEdit} (offline edit)`);
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.getByText('Saved on this device').first()).toBeVisible();
  });

  await test.step('three changes are queued and nothing has been sent', async () => {
    await page.goto(`${server.url}sync`);
    await expect(page.locator('tbody tr')).toHaveCount(3);
    await expect(page.locator('a[href=sync] .badge')).toHaveText(/^3/);
    expect(writesSent).toEqual([]);
  });

  await test.step('network back (server still down): the outbox drains to DummyJSON', async () => {
    await context.unroute('**/*dummyjson.com/**', blockDummyJson);
    await context.setOffline(false);
    await expect(page.getByText('Nothing waiting')).toBeVisible({ timeout: 60_000 });
    expect(writesSent.map(write => write.split(' ')[0]).sort()).toEqual(['DELETE', 'POST', 'PUT']);
  });

  expect(withoutOfflineNoise(pageErrors)).toEqual([]);
});
