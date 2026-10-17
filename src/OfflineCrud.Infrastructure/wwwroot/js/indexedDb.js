// IndexedDB access for the Offline CRUD app, loaded from C# as an ES module:
//   IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/OfflineCrud.Infrastructure/js/indexedDb.js")
//
// LEARN: Why hand-written JS? .NET in WebAssembly can't call browser APIs such as IndexedDB directly. Every call
// crosses the JS interop boundary, where arguments and results are serialised to JSON. Keeping this module small and
// visible shows exactly what crosses that boundary. Each exported function is one round trip from C#.
//
// LEARN: IndexedDB is the browser's transactional object database. It's asynchronous, per origin, survives reloads and
// restarts, and can hold far more than localStorage. Data is stored as structured clones of plain JS objects, so no
// schema is needed for record *fields*. Only object stores and indexes are declared, and changing them requires a
// version upgrade (see `migrations`).

const DB_NAME = 'offline-crud';
const PRODUCTS = 'products';
const META = 'meta';
const OUTBOX = 'outbox';

// LEARN: Schema versioning. indexedDB.open(name, version) fires `upgradeneeded` when the stored version is lower than
// the requested one, and only that event may create or change object stores and indexes. Each entry below upgrades from
// version N to N + 1. A user who skipped versions runs every step in order. Never edit a step that has shipped: add one.
const migrations = [
    // Version 0 → 1: products and a small key/value store for metadata.
    (db) => {
        const products = db.createObjectStore(PRODUCTS, { keyPath: 'id' });

        // Not unique: DummyJSON gives every created product the same server id. Records with no serverId
        // (null) are simply left out of the index.
        products.createIndex('byServerId', 'serverId', { unique: false });

        // A compound index gives "not deleted, in title order" in one ordered range scan. IndexedDB keys can't be
        // booleans, so `deleted` is stored as 0 or 1.
        products.createIndex('byListOrder', ['deleted', 'titleKey'], { unique: false });

        db.createObjectStore(META, { keyPath: 'key' });
    },

    // Version 1 → 2: the outbox of changes waiting to be sent. Existing products and metadata are untouched; only the
    // new store is created. A user upgrading from version 1 keeps all their data.
    (db) => {
        const outbox = db.createObjectStore(OUTBOX, { keyPath: 'id' });
        outbox.createIndex('byEntity', 'entityId', { unique: false });

        // createdAt is an ISO 8601 UTC string, which sorts correctly as text.
        outbox.createIndex('byCreated', 'createdAt', { unique: false });
    },
];

const DB_VERSION = migrations.length;

// Every key in byListOrder that starts with 0 (not deleted). Arrays compare element by element, and a shorter array
// sorts before a longer one with the same prefix, so [0] <= [0, 'anything'] < [1].
const ACTIVE = IDBKeyRange.bound([0], [1], false, true);

let databasePromise = null;

function openDatabase() {
    if (databasePromise) {
        return databasePromise;
    }

    databasePromise = new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, DB_VERSION);

        request.onupgradeneeded = (event) => {
            for (let version = event.oldVersion; version < event.newVersion; version++) {
                migrations[version](request.result, request.transaction);
            }
        };

        request.onsuccess = () => {
            const db = request.result;

            // LEARN: If a newer version of the app opens the database in another tab, this connection blocks its
            // upgrade until it closes. Closing here lets the upgrade proceed; the next call reopens the database.
            db.onversionchange = () => {
                db.close();
                databasePromise = null;
            };
            resolve(db);
        };

        request.onerror = () => {
            databasePromise = null;
            reject(request.error);
        };

        request.onblocked = () => {
            console.warn('IndexedDB upgrade is waiting for another tab of this app to close.');
        };
    });

    return databasePromise;
}

// Wraps a single IDBRequest in a promise.
function promisify(request) {
    return new Promise((resolve, reject) => {
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

// Resolves when a transaction commits and rejects if it fails or is aborted. A write is only durable once its
// transaction completes, not when the individual put() succeeds.
function completion(transaction) {
    return new Promise((resolve, reject) => {
        transaction.oncomplete = () => resolve();
        transaction.onerror = () => reject(transaction.error);
        transaction.onabort = () => reject(transaction.error ?? new DOMException('Transaction aborted', 'AbortError'));
    });
}

// LEARN: A transaction commits automatically as soon as it has no pending requests and control returns to the event
// loop. Awaiting anything that isn't an IndexedDB request in the middle (a fetch, a timer, a call back into .NET) lets
// it commit early, and the next request then fails with TransactionInactiveError. That's why each function below issues
// all its requests up front and only then awaits them.

/** Returns one page of non-deleted products in title order, and how many there are in total. */
export async function getProductPage(skip, take) {
    const db = await openDatabase();
    const index = db.transaction(PRODUCTS, 'readonly').objectStore(PRODUCTS).index('byListOrder');

    const total = promisify(index.count(ACTIVE));
    const items = new Promise((resolve, reject) => {
        const results = [];
        let skipped = skip === 0;
        const request = index.openCursor(ACTIVE);

        request.onsuccess = () => {
            const cursor = request.result;
            if (!cursor) {
                resolve(results);
                return;
            }

            // advance() jumps over `skip` records without loading them.
            if (!skipped) {
                skipped = true;
                cursor.advance(skip);
                return;
            }

            results.push(cursor.value);
            if (results.length >= take) {
                resolve(results);
                return;
            }

            cursor.continue();
        };
        request.onerror = () => reject(request.error);
    });

    return { items: await items, total: await total };
}

/** Returns every non-deleted product in title order. */
export async function getAllProducts() {
    const db = await openDatabase();
    const index = db.transaction(PRODUCTS, 'readonly').objectStore(PRODUCTS).index('byListOrder');
    return promisify(index.getAll(ACTIVE));
}

/** Returns the product with this local id (tombstones included), or null. */
export async function getProduct(id) {
    const db = await openDatabase();
    const store = db.transaction(PRODUCTS, 'readonly').objectStore(PRODUCTS);
    return (await promisify(store.get(id))) ?? null;
}

/** Returns every product (tombstones included) whose serverId is in the given list. */
export async function getProductsByServerIds(serverIds) {
    const db = await openDatabase();
    const index = db.transaction(PRODUCTS, 'readonly').objectStore(PRODUCTS).index('byServerId');
    const lookups = serverIds.map((serverId) => promisify(index.getAll(serverId)));
    return (await Promise.all(lookups)).flat();
}

/** Inserts or replaces products, all in one transaction: either every record is written or none is. */
export async function putProducts(records) {
    const db = await openDatabase();
    const transaction = db.transaction(PRODUCTS, 'readwrite');
    const store = transaction.objectStore(PRODUCTS);
    await writeAll(transaction, () => {
        for (const record of records) {
            store.put(record);
        }
    });
}

// Runs the writes, then waits for the transaction to commit.
// LEARN: put() can throw synchronously (e.g. a DataError for a record without a valid key). That exception does NOT
// abort the transaction: the puts that already ran would still commit when it goes idle. So all-or-nothing needs an
// explicit abort() here, not just a try/catch.
async function writeAll(transaction, write) {
    const done = completion(transaction);
    try {
        write();
    } catch (error) {
        transaction.abort();
        await done.catch(() => { });
        throw error;
    }
    await done;
}

/** Returns every outbox entry, oldest first. */
export async function getOutboxEntries() {
    const db = await openDatabase();
    const index = db.transaction(OUTBOX, 'readonly').objectStore(OUTBOX).index('byCreated');
    return promisify(index.getAll());
}

/** Returns the outbox entries for one product, oldest first. */
export async function getOutboxEntriesForEntity(entityId) {
    const db = await openDatabase();
    const index = db.transaction(OUTBOX, 'readonly').objectStore(OUTBOX).index('byEntity');
    const entries = await promisify(index.getAll(entityId));
    return entries.sort((a, b) => (a.createdAt < b.createdAt ? -1 : a.createdAt > b.createdAt ? 1 : 0));
}

/**
 * Writes products and outbox changes in ONE transaction spanning both stores: every put and delete is applied, or none.
 * LEARN: This is the transactional outbox. A transaction can span several object stores, as long as they're all named
 * when it's opened, so a product change and the entry that will send it are committed together.
 */
export async function commit(products, outboxPuts, outboxDeletes) {
    const db = await openDatabase();
    const transaction = db.transaction([PRODUCTS, OUTBOX], 'readwrite');
    const productStore = transaction.objectStore(PRODUCTS);
    const outboxStore = transaction.objectStore(OUTBOX);
    await writeAll(transaction, () => {
        for (const product of products) {
            productStore.put(product);
        }
        for (const entry of outboxPuts) {
            outboxStore.put(entry);
        }
        for (const id of outboxDeletes) {
            outboxStore.delete(id);
        }
    });
}

/** Returns a metadata value, or null if it isn't set. */
export async function getMeta(key) {
    const db = await openDatabase();
    const store = db.transaction(META, 'readonly').objectStore(META);
    const entry = await promisify(store.get(key));
    return entry ? entry.value : null;
}

/** Sets a metadata value. */
export async function setMeta(key, value) {
    const db = await openDatabase();
    const transaction = db.transaction(META, 'readwrite');
    await writeAll(transaction, () => transaction.objectStore(META).put({ key, value }));
}
