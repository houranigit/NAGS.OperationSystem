# Operations Module Plan

Status: **Implemented — phases 0–7 delivered, including the 2026-07-18 On Call correction**
Last updated: 2026-07-18

This is the phased implementation plan for the v1.0.0 `Operations` module. The domain model it implements is in `docs/modules/operations-foundation.md`; read that first. The plan follows the modular-monolith conventions in `docs/architecture/operations-system-v1.md`.

**2026-07-04 corrections applied on top of the original plan** (see the foundation doc's decisions log):
no `PendingReview` flight status (submit keeps the flight `InProgress`; opening a draft changes nothing);
the flight captures an approved work-order snapshot + reference on approval (billing loads flight + approved work order);
work orders are owner-scoped by StaffMember with one active per staff per flight;
completion approval requires actual aircraft type/flight number/ATA/ATD while actual services stay optional;
planned services are mandatory (per-landing mixing rule, ad-hoc cancellation exception);
visibility (per-landing station-wide vs assigned-only) is enforced server-side on reads and writes;
a per-flight timeline is queryable and shown in the portal; and the portal gained a calendar page and a
full work-order-first dialog (planning + actual fields).

**2026-07-18 correction:** On Call is no longer a seeded MasterData service. It is the derived state of
a Per-Landing flight that has a non-merged work order with at least one performed service line. Per-Landing
work orders start empty; staff add only the services actually performed.

## 1. Goal And Definition Of Done

Deliver a running, API-first Operations module (backend + Blazor portal) that lets NAGS:

- Schedule flights in advance and view them on a station calendar.
- Complete flights via work orders (Schedule-First and Work-Order-First), including services, tasks, resources, time, and attachments.
- Review, approve (lock + hand to billing), return, reject, cancel (via cancellation work order), and reopen.
- Handle Per-Landing/On-Call rules, assignment/visibility, station scoping, duplicate detection and merge, flight-number history, and auto-generation for overdue Per-Landing flights.

"Done" for the release means: EF migrations, `/api/v1/operations` endpoints, permission catalog, domain + integration tests, typed Blazor clients, and usable portal screens — matching the acceptance bar used for the MasterData module.

## 2. Scope

### In scope

Flight scheduling and lifecycle, work-order authoring and review workflow, Per-Landing/On-Call and mixing rules, assignment/visibility, station scoping, duplicate detection + admin merge (soft-archive), flight-number history, per-station work-order numbering, auto-generation job, dashboard/queries, attachments, billing hand-off event stub, and a MasterData read seam.

### Out of scope (this release)

Contracts module integration (design seams only), Billing module (event stub only), Inventory/stock deduction, mobile endpoints and offline sync (design-ready only), customer-side approval before billing.

## 3. Prerequisites

- **MasterData read seam.** No cross-module readers/snapshots exist yet. Phase 1 must add reader contracts (or internal query handlers) for Customer, Station, OperationType, AircraftType, Service, StaffMember, Tool, Material, GeneralSupport, plus the well-known ids for Aircraft Per Landing and Ad Hoc. On Call is derived inside Operations and has no MasterData id.
- All prior open questions are resolved (`operations-foundation.md` §19). There are **no blocking decisions**; the plan can run all phases end-to-end.

## 4. Phased Delivery

### Phase 0 — Module scaffolding
- Create projects: `Operations.Domain`, `Operations.Application`, `Operations.Infrastructure`, `Operations.Contracts`, `Operations.Api`; register in the solution and host.
- EF `operations` schema, `OperationsDbContext` with outbox/inbox, module DI extension, and the `operations.*` permission catalog (`IPermissionCatalog`).
- `IOperationsScope` (station/customer scoping) mirroring `MasterDataScope`.

### Phase 1 — MasterData read seam + snapshots
- Reader contracts/handlers and immutable snapshot value objects for all referenced MasterData records.
- Well-known id helpers for Aircraft Per Landing and Ad Hoc.

### Phase 2 — Flight aggregate (scheduling)
- `Flight` aggregate: value objects (`FlightNumber`, `OriginalFlightNumber`, `ScheduledTime`), planned services, assigned employees, statuses, snapshots, domain events.
- Commands: `ScheduleFlight`, `BatchScheduleFlights`, `UpdateScheduledFlight`, `ChangeFlightNumber`, `AssignEmployees`, `InviteEmployee`, `RemoveAssignment`.
- `PerLandingPolicy` (mixing rule + picker exclusion). Domain tests.
- Queries: scheduler calendar, flights list, flight detail. EF config + migration. Endpoints. Permissions.

### Phase 3 — Work Order aggregate (completion)
- `WorkOrder` aggregate: `WorkOrderType` (Completion/Cancellation), `Status`, `ActualTime`, tail, service lines (Planned/Extra, multi-employee), tasks (Major/Minor, multi-employee, tools/materials/general-support with `Quantity`, `TimeWindow`, attachments), optional signature, Return-to-Ramp.
- Commands: `OpenWorkOrder` (auto-copy planned services except Per-Landing, which starts empty), `UpdateWorkOrder`, `RecordReturnToRamp`, `SubmitWorkOrder`, `WithdrawWorkOrder`. A Per-Landing flight becomes On Call once any non-merged work order has a performed service line.
- Minimum-content rules; attachment policy. `StationWorkOrderSequence` + `WorkOrderNumberGenerator`.
- Work order detail query, EF config + migration, endpoints, domain tests.

### Phase 4 — Review, approval, cancellation lifecycle
- Commands: `ApproveWorkOrder` (assign number, lock, settle flight, publish billing stub event), `ReturnWorkOrderToReview`, `RejectWorkOrder`, `CancelFlight` (cancellation work order), `ReopenFlight`.
- Review queue query. `FlightSentToBilling`/`WorkOrderApproved` integration event in `Operations.Contracts`. Integration tests for the full lifecycle.

### Phase 5 — Ad-hoc + duplicate detection + merge
- `CreateAdHocFlightWithWorkOrder`, `ClaimPerLandingFlight`.
- `FlightDuplicateDetector` (customer + station + time proximity, supporting signals) with warn/link/flag UX.
- Deterministic duplicate-work-order surfacing. `MergeDuplicateFlights` / `MergeDuplicateWorkOrders` with in-place survivor update + soft-archive (`Superseded`/`Merged`) and resolution records. Merge-conflict screen. Tests.

### Phase 6 — Auto-generation job + dashboard
- `AutoWorkOrderPolicy` background job for overdue Per-Landing flights (STD + 60 min, appsettings). Dashboard/KPI query and screen.

### Phase 7 — Portal UI polish + hardening
- Complete Blazor screens per MasterData UI conventions (typed clients, Radzen, tokens, RTL, localization, permission-aware). Concurrency (RowVersion/ETag) end-to-end. Full test pass.

## 5. Cross-Cutting Requirements

- **Auth/scoping:** every endpoint enforces `operations.*` permissions and station scope; fail closed on inactive linked records.
- **Errors:** `Result`/`Error` → ProblemDetails; validation via FluentValidation pipeline.
- **Time/localization:** UTC storage, edge rendering; AR/EN, RTL-safe, no hardcoded user-facing strings.
- **Concurrency:** RowVersion + `If-Match`; per-station numbering via sequence aggregate.
- **Mobile-ready seam:** optional `clientFlightId`/`clientMutationId` accepted (unused now).
- **Tests:** domain tests for behavior-heavy rules; integration tests (Testcontainers) for workflows and scope denial.

## 6. Milestone Checklist

- [x] Phase 0 — scaffolding, schema, permissions, scope.
- [x] Phase 1 — MasterData read seam + snapshots.
- [x] Phase 2 — Flight scheduling (domain, API, calendar/list/detail).
- [x] Phase 3 — Work order authoring (domain, API, detail).
- [x] Phase 4 — Review/approval/cancellation lifecycle + billing event stub.
- [x] Phase 5 — Ad-hoc, duplicate detection, merge (soft-archive).
- [x] Phase 6 — Auto-generation job + dashboard.
- [x] Phase 7 — UI polish + hardening + full tests (calendar page, work-order-first dialog, timeline, ownership-aware editing).
- [x] `docs/architecture/migration-inventory.md` Operations rows updated as phases land.

## 7. Risks

- **MasterData read seam** is a hard dependency and net-new work; underestimating it delays every phase.
- **Merge UX** (IDE-style field-by-field) is non-trivial UI; keep the domain/API contract stable and iterate the screen.
- **Duplicate scoring** tuning (thresholds/weights) needs real data; ship configurable weights and safe defaults.
- **Contract absence** means manual service selection and the temporarily-relocated Per-Landing mixing rule; keep seams clean for the later Contracts move.
- **Post-approval cancellation** is handled as a duplicate work order resolved by admin merge (decided; §19.1) — the merge UX must cover completion-vs-cancellation conflicts.

## 8. Immediate Next Steps

1. Start Phase 0 + Phase 1 (all decisions resolved; run all phases end-to-end).
