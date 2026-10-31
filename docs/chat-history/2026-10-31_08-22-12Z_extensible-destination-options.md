# Extensible destination options

- **Source session:** `2026-10-31T08-22-12-830Z_01a1910d-565e-72e7-b3c0-0c5aaa968076.jsonl`
- **Session ID:** `01a1910d-565e-72e7-b3c0-0c5aaa968076`
- **Started (UTC):** 2026-10-31T08:22:12.830Z

## You

I would like a well structured destionation options, which can be easily expanded in the future

---

## Assistant

Implemented a **catalog-based destination structure** that is easier to extend.

### What changed
- **Frontend catalog:** [`destination-options.ts`](frontend/src/app/features/destinations/destination-options.ts) centralizes channel IDs, field names, labels, input types, limits, hints, and validation.
- **Backend catalog:** [`DestinationOptions.cs`](backend/WorldEventAlerts.Api/Features/Destinations/Domain/DestinationOptions.cs) centralizes validation policies and supported subscription channels.
- Destination fields and subscription checkboxes now render from the catalog, using generic state instead of separate email/Slack handlers.
- Added focused tests and an [extension guide](docs/DESTINATIONS.md#destination-catalog-and-extension-guide).

### Compatibility
Existing API fields and database columns remain unchanged; no migration is required. Adding a future channel still requires explicit API and persistence support, but its frontend input and selector can be generated from its catalog entry.

### Verification
- Frontend: **144 tests passed**
- Backend: **208 passed, 6 skipped**
- Frontend production build and backend Release build passed.

---

## You

commit and push it

---

## Assistant

Committed and pushed to `origin/main`.

- **Commit:** `d85563c`
- **Message:** `Refactor destination options into extensible catalogs`
- Working tree is clean.
