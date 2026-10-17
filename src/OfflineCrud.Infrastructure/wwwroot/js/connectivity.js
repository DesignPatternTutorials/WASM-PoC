// Browser connectivity for the Offline CRUD app, loaded from C# as an ES module.
//
// LEARN: This is the other direction of JS interop: JavaScript calling .NET. The C# side passes in a
// DotNetObjectReference, and the browser's `online` / `offline` events call one of its [JSInvokable] methods through
// invokeMethodAsync. The call is asynchronous and its arguments are serialised to JSON, exactly like .NET → JS calls.

let listener = null;

/** Starts reporting connectivity changes to .NET. Returns the current navigator.onLine value. */
export function start(dotNetReference) {
    stop();
    listener = () => dotNetReference.invokeMethodAsync('OnBrowserConnectivityChanged', navigator.onLine);
    window.addEventListener('online', listener);
    window.addEventListener('offline', listener);
    return navigator.onLine;
}

/** Stops reporting. After this, .NET can safely dispose its DotNetObjectReference. */
export function stop() {
    if (listener) {
        window.removeEventListener('online', listener);
        window.removeEventListener('offline', listener);
        listener = null;
    }
}
