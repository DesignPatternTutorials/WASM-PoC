# DummyJSON API notes

Verified with `curl` against `https://dummyjson.com` on 2026-10-17. Where this differs from the spec,
this file is what the code should follow.

## Summary of surprises

| Spec assumption | What the API actually does | Consequence for the client |
| --- | --- | --- |
| A create returns a server id | It does, but it is **always `total + 1` (currently 195)**, for every POST | Server ids from creates are **not unique**. Never key local data by them. The local GUID stays the identity; the server id is informational only. |
| Created items can't be fetched later | Confirmed: `GET /products/195` → 404 | Expected. See "Non-persistence" below. |
| Writes are simulated | Confirmed: `PUT /products/1` returns the merged product, but a later `GET /products/1` is unchanged | IndexedDB must remain the UI's source of truth. Never re-read after a write to "confirm" it. |
| The API validates input | **It doesn't.** `{"price":"abc"}` comes back as `"price":"abc"` with 200; `{}` creates a product with only an `id` | All validation is client-side (`EditForm` + Domain rules). A 200 does not mean the data was valid. |
| — | `PUT` echoes an `id` from the body, overriding the URL id in the response | Don't send `id` in write bodies. |
| — | Unknown fields are silently dropped from write responses | Harmless; don't rely on round-tripping extra fields. |

## Endpoints

All responses are `application/json; charset=utf-8`. Errors are `{"message": "..."}`.

### Read

| Request | Result |
| --- | --- |
| `GET /products?limit=10&skip=0` | `200` `{"products":[...],"total":194,"skip":0,"limit":10}` |
| `GET /products?select=title,price` | Only `id` plus the selected fields. Useful for a lightweight list. |
| `GET /products?sortBy=title&order=asc` | Sorted. |
| `GET /products?limit=0` | All products (`limit` in the response is 194). Same for `limit` larger than `total`. |
| `GET /products?skip=500` | `200`, empty `products`, `limit: 0`. |
| `GET /products?limit=-1` | `400`. |
| `GET /products/{id}` | `200` product, or `404` `{"message":"Product with id '999999' not found"}`. A non-numeric id also returns `404`, not `400`. |
| `GET /products/search?q=phone` | `200`, same paged envelope (`total: 23`). Supports `limit` / `skip` / `select`. |
| `GET /products/search?q=zzzz` | `200`, `products: []`, `total: 0`. |
| `GET /products/search` (no `q`) | `200`, behaves like the unfiltered list. |
| `GET /products/category-list` | `200`, array of category slugs, e.g. `["beauty","fragrances",...]`. Handy for a create/edit dropdown. |

Product shape (fields worth modelling; the rest can be ignored):

```json
{
  "id": 1,
  "title": "Essence Mascara Lash Princess",
  "description": "...",
  "category": "beauty",
  "price": 9.99,
  "discountPercentage": 10.48,
  "rating": 2.56,
  "stock": 99,
  "brand": "Essence",
  "sku": "BEA-ESS-ESS-001",
  "tags": ["beauty", "mascara"],
  "thumbnail": "https://cdn.dummyjson.com/product-images/.../thumbnail.webp",
  "images": ["https://cdn.dummyjson.com/..."],
  "meta": { "createdAt": "2025-10-09T14:47:01.588Z", "updatedAt": "2026-05-23T11:27:41.868Z" }
}
```

Not every product has `brand`. Treat it as optional.

### Write (simulated, not persisted)

| Request | Result |
| --- | --- |
| `POST /products/add` with JSON body | `201 Created`, body is the input plus `"id": 195`. No `Location` header. |
| `POST /products/add` with `{}` | `201`, `{"id":195}`. No validation. |
| `POST /products/add` with malformed JSON | `400`, `{"message":"Expected property name or '}' in JSON at position 1 ..."}` |
| `PUT /products/1` | `200`, the full existing product merged with the body (a merge, not a replace). |
| `PATCH /products/1` | `200`, same merge behaviour as `PUT`. |
| `PUT /products/195` (a "created" id) | `404` |
| `DELETE /products/1` | `200`, the product plus `"isDeleted": true, "deletedOn": "<ISO timestamp>"`. |
| `DELETE /products/195` | `404` |

## Non-persistence: what the sync engine will see

- **Create, then later update or delete that item.** The update or delete targets id 195 (or whatever
  `total + 1` was), which the server doesn't have, so it gets `404`. Coalescing (create then delete →
  drop both; create then update → send one create with the latest data) avoids this when both happen
  before a sync. If the create was already sent, the follow-up gets a 404 and goes to `Failed` with a
  readable reason. That is the deliberate, non-crashing outcome the spec asks for.
- **Update an existing product (ids 1–194).** Succeeds every time, but a refresh from the API brings
  back the original values. The stale-while-revalidate refresh must not overwrite items that have
  pending changes. Items marked `Synced` *will* revert on refresh, and that is DummyJSON's behaviour,
  not a bug.
- **Delete an existing product.** Succeeds, but the product reappears in the next list refresh. The
  local store should keep a tombstone or simply drop items the user deleted (decided in Phase 2/3).

## Versions (`meta.updatedAt`)

- `GET` responses include `meta.updatedAt` when `meta` is in `select` (`select=title,...,meta`). It's an ISO
  timestamp and different for every product. The app uses it as an opaque version token, comparing for equality only.
- `PUT` / `PATCH` responses **omit `meta` entirely**, and `POST /products/add` echoes only the fields you sent plus `id`.
  So a write never returns a version. Keep the version you already had (see `ProductService.UpdateAsync`).
- `meta` also carries `createdAt`, `barcode` and `qrCode`. `select` can't pick nested fields, so these come along.

## CORS

- Requests with an `Origin` header get `access-control-allow-origin: <that origin>` and
  `access-control-allow-credentials: true`.
- The preflight (`OPTIONS`) returns `204` with `access-control-allow-methods: GET,HEAD,PUT,PATCH,POST,DELETE`,
  and **echoes any requested headers** in `access-control-allow-headers`. A custom `Idempotency-Key`
  header is therefore allowed, even though DummyJSON ignores it.

## Rate limiting

Responses carry `x-ratelimit-limit: 100`, `x-ratelimit-remaining` and `x-ratelimit-reset`. I didn't
deliberately trigger a `429`. The sync engine already treats `429` as retryable with backoff.
Keep the periodic sync interval and page sizes modest so a demo session stays well under the limit.

## Useful for demos

- `?delay=<ms>` (e.g. `/products/1?delay=1500`) makes the response slow. Good for showing loading
  states, the disabled submit button, and an in-flight outbox entry.
- `ETag` headers (weak, e.g. `W/"aa-..."`) are present on GETs, but they are generic HTTP caching
  from the host and writes don't check them. They can't be used for real conflict detection.

## Images

`thumbnail` and `images` point at `cdn.dummyjson.com`. They won't load offline unless they were cached.
The "no CDNs" rule applies to the app shell (CSS, JS, fonts, icons), not to product data. The UI
should show a local placeholder when an image fails to load.
