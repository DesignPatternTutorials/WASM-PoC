// The service worker for PUBLISHED builds. `dotnet publish` copies this over service-worker.js (see the <ServiceWorker>
// item in OfflineCrud.Client.csproj). Under `dotnet run` the browser gets the no-op service-worker.js instead.
//
// LEARN: A service worker is a script the browser runs separately from the page, between the page and the network. It
// can answer the page's requests itself. This one makes the app shell work offline. On install it downloads every file
// the app needs (listed by the SDK in service-worker-assets.js) into Cache Storage. After that it serves those files
// from the cache, so the app starts with no network at all. It runs JavaScript, not .NET: the .NET runtime is just
// one of the files it caches.
//
// LEARN: This caches the app (HTML, JS, CSS, fonts, the .NET runtime and your assemblies). The app's DATA lives in
// IndexedDB and is handled by the app itself. Requests to DummyJSON pass straight through to the network.

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', onFetch);
self.addEventListener('message', onMessage);

const cacheNamePrefix = 'offline-cache-';

// The manifest's version is a hash of every asset, so each publish that changes anything gets a fresh cache.
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;

// LEARN: The Blazor template caches only an ALLOW-list of file types, and that list misses .woff2 and .svg. With it,
// the icon font and the product placeholder silently failed offline. This version caches everything the SDK lists and
// EXCLUDES what isn't needed, so a new asset type is cached by default. The trade-off is that anything put in wwwroot
// gets downloaded on install, so keep wwwroot lean (unused Bootstrap files were removed for this reason).
const offlineAssetsExclude = [
    /^service-worker\.js$/, // the worker itself is fetched by the browser's own update check, never from a cache
    /\.map$/,               // source maps are only for DevTools
    /(^|\/)LICENSE$/,
];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = '/';
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // `integrity` makes the browser verify each file against the hash the SDK computed at publish time, so a
    // half-deployed or tampered file can't end up in the cache. If any file fails, install fails and the previous
    // version keeps running.
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    const cache = await caches.open(cacheName);
    await cache.addAll(assetsRequests);
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete the caches of previous versions.
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

function onFetch(event) {
    const request = event.request;

    // Only same-origin GETs can be in the cache. Leaving everything else alone (no respondWith) lets the browser
    // handle it exactly as if there were no service worker: API calls to DummyJSON, product images from its CDN, and
    // any POST/PUT/DELETE.
    if (request.method !== 'GET' || new URL(request.url).origin !== self.origin) {
        return;
    }

    event.respondWith(respondFromCache(request));
}

async function respondFromCache(request) {
    // Navigations (typing a URL, reloading, opening a bookmark like /products/abc) get index.html: Blazor's router
    // then shows the right page. Without this, a deep link would 404 offline.
    const shouldServeIndexHtml = request.mode === 'navigate' && !manifestUrlList.some(url => url === request.url);

    const cache = await caches.open(cacheName);

    // LEARN: ignoreSearch, because Bootstrap Icons' CSS asks for fonts/bootstrap-icons.woff2?<hash>, but the cache
    // holds fonts/bootstrap-icons.woff2. An exact match would miss and the icons would vanish offline. It's safe here:
    // only the app's own fixed files are in this cache.
    const cachedResponse = await cache.match(shouldServeIndexHtml ? 'index.html' : request, { ignoreSearch: true });

    return cachedResponse || fetch(request);
}

// LEARN: Updates. When a new version is published, the browser sees a changed service-worker-assets.js, installs the
// new worker in the background, and leaves it *waiting*: the old worker keeps serving every open tab, so a page
// never mixes old and new files. The page shows "A new version is available". When the user clicks Reload, it posts
// SKIP_WAITING, the new worker takes over, and the page reloads onto the new version.
function onMessage(event) {
    if (event.data?.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
}
