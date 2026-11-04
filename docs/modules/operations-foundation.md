# Operations Module Foundation

This is the entry point for the current Operations module. This branch starts from `main` and reduces Operations to a flight scheduling baseline: maintain a flight register, create one flight, schedule the same flight on selected dates, and invite employees onto an existing flight. The Android client supports flight viewing, basic details, and employee invitations; scheduling remains in the web portal. HandOver implementation is excluded. Operational workflows will be designed and implemented individually later.

## Flight data and ownership

`Flight` owns customer, station, operation type, flight number, scheduled arrival/departure, optional aircraft type, planned services, and assigned employees. MasterData remains authoritative for reference records; Operations validates selections through its existing readers and stores snapshots. Planned services and assigned employees are direct flight collections.

Creation/update timestamps, creator ID, and SQL rowversion support persistence and concurrency. There is one current scheduled flight number. Flight status, original-number history, contract placeholders, merge metadata, actual work, work orders, handover, review, approval, timeline, attachments, reminders, and automatic work creation are outside this baseline. Platform audit capture remains enabled independently of a flight history feature.

## Scheduling and validation

The existing flight grid and scheduling dialog support a single flight, multiple selected dates, and basic schedule edits. Required fields are customer, station, operation type, flight number, arrival/departure, and at least one planned service. Aircraft type and assigned staff remain optional. Staff assignments require the existing assignment permission and active staff at the selected station.

Aircraft Per Landing remains an exclusive planned designation and cannot be combined with other planned services or assigned staff. Ad Hoc is unavailable for scheduling. Batch scheduling retains the supplied time-zone conversion and overnight departure behavior; invalid or ambiguous local times fail validation rather than silently shifting the schedule.

## Authorization and API

Retained permissions are `operations.flights.view`, `view-station`, `schedule`, `update`, `assign`, and `invite`. Existing account/station scope remains enforced on the backend. Viewer accounts are read-only; station staff see assigned flights and station-wide Per Landing flights unless granted station-wide visibility. Customer contacts have no Operations access.

All flight routes are under `/api/v1/operations/flights`:

| Method | Route | Behavior |
|---|---|---|
| GET | `/` | Filtered, sorted, paged flight register |
| GET | `/{id}` | Flight planning details and rowversion |
| GET | `/customer-logos/{customerId}` | Customer logo when at least one flight for that customer is visible to the caller |
| POST | `/` | Schedule one flight |
| POST | `/bulk` | Schedule selected dates |
| PUT | `/{id}` | Update scheduled details |
| POST | `/{id}/change-number` | Change the current number |
| POST | `/{id}/assign` | Replace assigned staff |
| GET | `/{id}/invitation-options` | Active, unassigned staff at the flight station |
| POST | `/{id}/invite` | Add selected employees without replacing the roster |

Edits and invitations retain the existing `If-Match` rowversion precondition. Invitations require `operations.flights.invite`, an administrator or station-staff account, and access to the flight. They accept only active staff at that flight's station and are unavailable for Per Landing flights. Invitations add distinct employees immediately to the existing roster; repeated selections do not replace assignments or create duplicate invitation events. A stale version that would add employees fails without changing the roster. The retained invitation event queues the existing bilingual inbox/push notification for linked recipients, suppressing self-notifications; transport delivery remains separate from the accepted flight change.

Customer logo reads require `operations.flights.view` and the existing flight visibility scope. Operations checks for a visible flight before reading logo content through the MasterData-owned reader contract. MasterData retains logo storage and customer-management permissions. Missing logos or files return `404` for the card's flight-icon fallback.

Operations work-order, legacy mobile BFF, timeline, lifecycle, reporting, and dashboard endpoints are removed. The shared change hub remains owned by the API host because MasterData publishes catalog hints and Operations publishes basic-flight hints.

## Android baseline

Android uses the same paginated `/api/v1/operations/flights` list and basic-details endpoint as the portal, plus the two invitation routes above. It supports search, paging, refresh, device-time-zone schedule display, and authorized employee invitations. It does not create or schedule flights. Flight reads prefer the live server and fall back to the last successful scoped register and previously opened basic details when disconnected. Full flight sync re-reads every scoped list page, revalidates cached details, and removes no-longer-visible flights. Details never opened online need a connection. The Flights Sync Center card reports count, freshness, duration, and errors; cached reads are identified on screen. Invitations remain online with fresh detail/eligibility checks, and there is no offline submission queue. Identity sign-in, optional enrolled MFA, token refresh, profile, and Notifications remain existing capabilities. Administrator and Viewer accounts retain their server-granted read scope; station staff retain the visibility rules above.

Shared MasterData catalog synchronization remains a separate mobile capability. Android caches customers, aircraft types, services, tools, materials, general supports, ATA chapters, and station staff from the existing `/api/v1/masterdata/{catalog}/options` routes. Those requests retain their reference-lookup or per-catalog view permission and scope checks; flight access does not imply catalog access. Staff synchronization uses `/api/v1/masterdata/staff-members/options`: station-staff accounts receive active staff only from their own station, global readers keep their existing scope, and customer contacts have no staff access. The Station staff card reports that cache; flight invitation options remain live and independently enforce eligibility. Sync Center, opened from the home header or profile, retains the established diagnostics layout: network online/offline state, live-channel state and last catalog event, cached counts, last successful refresh, measured duration, errors, and a manual refresh action. Sign-in/token refresh, foreground entry, network or catalog-hub reconnection, a five-minute process-active poll, and manual refresh reload all catalogs. Live catalog-change hints reload only the affected catalog and leave the other catalogs' sync metadata unchanged; ordinary staff changes therefore refresh staff only. Operations changes refresh flights only; staff station/active changes and station active changes also invalidate flight visibility. Open flight views refresh after successful flight sync. MasterData publishes a hint after a successful create, active lookup-field edit, or real activation/deactivation; inactive edits, lookup-irrelevant changes, no-op transitions, and failed writes do not invalidate catalogs. Network failures retain the last successful catalog snapshot; permission denial removes the denied snapshot. Catalog access does not grant flight access or authorize offline mutations.

The former `operations.db` file and dormant drafts/queued work remain unopened. Shared catalogs and separate basic-flight snapshot/detail tables use `shared-catalogs.db` Room version 3, keyed by API environment and authenticated account and cleared on sign-out/account changes. The additive version 2 → 3 migration preserves all catalog rows. Failed connections retain good snapshots; authorization denials evict denied flights. Offline reads reflect the last successful access check until the device reconnects. Notifications use the separate `operations-baseline.db` Room database. Startup cancels the retired WorkManager outbox chain/tag, and its retained worker entry point is inert. A notification flight link resolves the current basic flight details through the scoped API. See the [Android README](../../src/Host/OperationsSystem.Mobile/README.md) for development configuration.

## Database change

Keep the historical `main` EF migrations unchanged. `Operations_FlightSchedulingBaseline` preserves the existing flights, direct `flight_planned_services`, and `flight_assigned_employees` rows in place, and removes retired work-order, mobile, reminder, history, status, contract, and merge storage plus pending event envelopes for retired Operations contracts. The retained `FlightEmployeeAssigned` invitation contract and platform audit envelopes are preserved. The migration does not require a HandOver operational graph. It does not alter another module's schema or delete stored file blobs.

This migration intentionally discards work-order/workflow data. Development startup migrations are disabled. Review the SQL and back up an existing database before applying the reset and running the API against that database. Restoring the previous application requires restoring its database backup; the reset migration cannot reconstruct the removed operational history. Verification uses disposable local SQL databases, never a configured remote database.

The isolated current-branch Docker target is `OperationsSystemOperationModuleMain`, initialized from a main-compatible database copy. The earlier HandOver-derived `OperationsSystemOperationModule` database remains preserved under its original name and is not overwritten or reused for this migration chain. Main/release remains a separate stable environment; see the [local environment runbook](../architecture/production-runbook.md#concurrent-local-environments).
