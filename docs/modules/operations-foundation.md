# Operations Module Foundation

This is the entry point for the current Operations module. This branch starts from `main` and reduces Operations to a flight scheduling baseline: maintain a flight register, create one flight, and schedule the same flight on selected dates. HandOver implementation is excluded. Operational workflows will be designed and implemented individually later.

## Flight data and ownership

`Flight` owns customer, station, operation type, flight number, scheduled arrival/departure, optional aircraft type, planned services, and assigned employees. MasterData remains authoritative for reference records; Operations validates selections through its existing readers and stores snapshots. Planned services and assigned employees are direct flight collections.

Creation/update timestamps, creator ID, and SQL rowversion support persistence and concurrency. There is one current scheduled flight number. Flight status, original-number history, contract placeholders, merge metadata, actual work, work orders, handover, review, approval, timeline, attachments, reminders, and automatic work creation are outside this baseline. Platform audit capture remains enabled independently of a flight history feature.

## Scheduling and validation

The existing flight grid and scheduling dialog support a single flight, multiple selected dates, and basic schedule edits. Required fields are customer, station, operation type, flight number, arrival/departure, and at least one planned service. Aircraft type and assigned staff remain optional. Staff assignments require the existing assignment permission and active staff at the selected station.

Aircraft Per Landing remains an exclusive planned designation and cannot be combined with other planned services or assigned staff. Ad Hoc is unavailable for scheduling. Batch scheduling retains the supplied time-zone conversion and overnight departure behavior; invalid or ambiguous local times fail validation rather than silently shifting the schedule.

## Authorization and API

Retained permissions are `operations.flights.view`, `view-station`, `schedule`, `update`, and `assign`. Existing account/station scope remains enforced on the backend. Viewer accounts are read-only; station staff see assigned flights and station-wide Per Landing flights unless granted station-wide visibility. Customer contacts have no Operations access.

All flight routes are under `/api/v1/operations/flights`:

| Method | Route | Behavior |
|---|---|---|
| GET | `/` | Filtered, sorted, paged flight register |
| GET | `/{id}` | Flight planning details and rowversion |
| POST | `/` | Schedule one flight |
| POST | `/bulk` | Schedule selected dates |
| PUT | `/{id}` | Update scheduled details |
| POST | `/{id}/change-number` | Change the current number |
| POST | `/{id}/assign` | Replace assigned staff |

Edits retain the existing `If-Match` rowversion precondition. Operations work-order, mobile, timeline, lifecycle, reporting, and dashboard endpoints are removed. The native Android client remains at the main API contract and is incompatible with this scheduling-only backend. The shared catalog-change hub remains owned by the API host because MasterData still publishes catalog changes.

## Database change

Keep the historical `main` EF migrations unchanged. `Operations_FlightSchedulingBaseline` preserves the existing flights, direct `flight_planned_services`, and `flight_assigned_employees` rows in place, and removes retired work-order, mobile, reminder, history, status, contract, and merge storage plus pending event envelopes from the retired Operations contract assembly. The migration does not require a HandOver operational graph. It does not alter another module's schema or delete stored file blobs.

This migration intentionally discards work-order/workflow data. Development startup migrations are disabled. Review the SQL and back up an existing database before applying the reset and running the API against that database. Restoring the previous application requires restoring its database backup; the reset migration cannot reconstruct the removed operational history. Verification uses disposable local SQL databases, never a configured remote database.

The isolated current-branch Docker target is `OperationsSystemOperationModuleMain`, initialized from a main-compatible database copy. The earlier HandOver-derived `OperationsSystemOperationModule` database remains preserved under its original name and is not overwritten or reused for this migration chain. Main/release remains a separate stable environment; see the [local environment runbook](../architecture/production-runbook.md#concurrent-local-environments).
