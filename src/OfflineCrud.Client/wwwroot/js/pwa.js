// App-update plumbing between the service worker registration (in index.html) and Blazor.

/** Calls dotNetReference.OnUpdateAvailable() once an updated service worker is waiting (now or later). */
export function watchForUpdate(dotNetReference) {
    const notify = () => dotNetReference.invokeMethodAsync('OnUpdateAvailable');
    if (window.offlineCrudUpdateWaiting) {
        notify();
    }
    window.addEventListener('offlinecrud:update-available', notify, { once: true });
}

/** Tells the waiting service worker to take over. index.html reloads the page when it does. */
export async function applyUpdate() {
    const registration = await navigator.serviceWorker.getRegistration();
    registration?.waiting?.postMessage({ type: 'SKIP_WAITING' });
}
