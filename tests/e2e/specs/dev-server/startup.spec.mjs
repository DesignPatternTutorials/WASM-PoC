// The app starts reliably under the dev server, with no errors on the way.
import { test, expect, waitForAppStart } from '../../support/fixtures.mjs';
import { startDevServer } from '../../support/servers.mjs';

const PORT = 5310;
let server;

test.beforeAll(async () => { server = await startDevServer(PORT); });
test.afterAll(async () => { await server?.stop(); });

for (const attempt of [1, 2, 3]) {
  test(`starts cleanly in a fresh browser profile (attempt ${attempt})`, async ({ page, pageErrors }) => {
    const failedRequests = [];
    page.on('requestfailed', request => failedRequests.push(`${request.url()} (${request.failure()?.errorText})`));
    page.on('response', response => { if (response.status() >= 400) failedRequests.push(`${response.status()} ${response.url()}`); });

    await page.goto(server.url);
    await waitForAppStart(page);

    await expect(page.getByRole('heading', { level: 1 })).toHaveText(/keeps working when the network doesn't/);
    expect(failedRequests).toEqual([]);
    expect(pageErrors).toEqual([]);
  });
}
