# Operations System v1.0.0 Rewrite Rules

## 1. Project Objective

Build a clean v1.0.0 rewrite of Operations System from scratch.

The goal is not to refactor the old project in place. The goal is to create a new, maintainable, API-first, production-ready system that preserves the correct business behavior while removing accumulated technical debt.

## 2. Old Project Reference Policy

The old OperationsSystem project is a business reference, not an architectural source. The old Blazor solution is copied into `legacy/` in this repository as read-only reference. Rewrite progress is tracked in `docs/architecture/migration-inventory.md`.

Use the old project to understand:

- Business fields and relationships
- Domain rules and invariants
- Validation requirements
- Workflow behavior
- Edge cases
- Existing mobile/API expectations
- Database constraints that represent real business rules

Do not blindly copy:

- Old architecture
- The legacy's specific Blazor/Radzen UI patterns and component conventions (v1.0.0 is also Blazor/Radzen, but follows the new `frontend-blazor.mdc` shells, tokens, and recipes)
- Technical debt
- Naming mistakes
- Overcomplicated abstractions
- Inconsistent endpoint/error patterns
- Client-specific business logic

For every rewritten feature, first extract the business facts from the old project, then design the clean v1.0.0 implementation using the new architecture.

## 3. Technology Stack

### Backend

- .NET 10
- ASP.NET Core Web API
- Minimal APIs
- Entity Framework Core
- SQL Server for v1.0.0
- FluentValidation
- MediatR (pinned to the last free/MIT version, 12.4.x) for commands, queries, and pipeline behaviors
- Custom DDD Identity (User/Role/UserSession); passwords hashed with ASP.NET Core `PasswordHasher` (PBKDF2)
- Manual DTO mapping (no mapping library)
- Tests: xUnit + Shouldly/FluentAssertions + NSubstitute + Testcontainers
- ProblemDetails for API errors
- Serilog for structured logging
- OpenTelemetry for traces and metrics
- Health checks
- HybridCache/in-memory caching for frequently requested data
- Central Package Management with Directory.Packages.props
- SignalR for web real-time updates (notifications and live data)
- A storage abstraction for binary files/attachments (local filesystem in v1.0.0, designed to swap to S3/Azure Blob later)
- A notification delivery abstraction (composite pusher) so a mobile push provider (FCM) can be added later without rework
- Redis is not part of v1.0.0 unless a measured distributed-caching need appears
- Mobile push (FCM), the mobile BFF, and the mobile offline-sync protocol are deferred with the mobile client (see Section 26)

### Frontend

- Blazor Web App (.NET 10) with the Interactive Auto render mode (server prerender + WebAssembly)
- Radzen.Blazor as the only UI component library, using the Material3 theme
- Design tokens (`wwwroot/tokens.css`, `--os-*` CSS variables); no hardcoded colors in components
- A typed API client layered over `BrowserApiClient` (JS `fetch` proxied through the host to `/api/v1`); DTOs mirror the application contracts
- Built-in localization (`IStringLocalizer`/resource-based) for Arabic/English with full RTL
- No React, Vite, Tailwind, shadcn/ui, TanStack Query, or Orval

### Mobile

- Existing Android app remains an important API consumer
- The Blazor web portal and Android must consume the same backend API
- Avoid web-only or mobile-only business rules in the backend

## 4. Architecture Direction

- Modular monolith
- API-first backend
- Single deployable backend application
- Clear module boundaries
- Domain-driven design where real behavior exists
- SOLID principles as design guardrails
- Rich domain models for behavior-heavy areas
- Vertical slices inside modules
- CQRS used pragmatically: commands mutate, queries read
- No microservices unless a real business need appears later

## 5. Solution Structure Rules

Recommended solution layout:

```text
src/
  BuildingBlocks/
    BuildingBlocks.Domain/
    BuildingBlocks.Application/
    BuildingBlocks.Infrastructure/
    BuildingBlocks.Api/

  Modules/
    Identity/
      Identity.Domain/
      Identity.Application/
      Identity.Infrastructure/
      Identity.Contracts/
      Identity.Api/
    MasterData/
      MasterData.Domain/
      MasterData.Application/
      MasterData.Infrastructure/
      MasterData.Contracts/
      MasterData.Api/
    Contracts/
    Operations/
    Notifications/
    Audit/

  Host/
    OperationsSystem.Api/
    OperationsSystem.Blazor/
      OperationsSystem.Blazor/         (server host)
      OperationsSystem.Blazor.Client/  (WASM + shared interactive components)
tests/
  Identity.UnitTests/
  Identity.IntegrationTests/
  ArchitectureTests/
```

Recommended backend feature layout inside each module application layer:

```text
Features/
  ManpowerTypes/
    CreateManpowerType/
    UpdateManpowerType/
    GetManpowerTypeById/
    SearchManpowerTypes/
```

Recommended frontend layout (in `OperationsSystem.Blazor.Client`):

```text
Api/            (BrowserApiClient + typed feature clients + client DTOs)
Auth/           (AuthSession, AuthenticationStateProvider, token store)
Shared/         (AuthLayout, PageHeader, LoadingCard, EmptyState, DetailField, RequireAuth, RequirePermission)
Features/
  Users/
    Pages/
    Components/
  Roles/
    Pages/
    Components/
```

- The API host composes modules but must not contain business logic.
- Module Api projects own endpoint mapping for their module.
- Contracts projects contain cross-module DTOs, read models, events, or public contracts only.
- Blazor feature folders own feature-specific pages, components, and any feature-only client helpers.
- Shared client folders are for truly reusable primitives (shells, layouts, guards) and app-wide services only.

### MasterData Boundary

- Detailed feature decisions and open questions live in `docs/modules/master-data-foundation.md`; MasterData implementation work must follow that living foundation.
- `MasterData` owns shared business catalogs and master records: customers, staff members, stations, countries, currencies, aircraft types, operation types, services, manpower types, licenses, units, tools, materials, general-support items, and their catalog price plans.
- The legacy `Core` and `Store` modules are business references for this single v1.0.0 module; their names and project boundaries are not carried into the rewrite.
- Catalog records may have lifecycle and validation behavior, but `MasterData` must not become a home for unrelated workflows merely because other modules consume their data.
- Stock quantities, warehouses, receipts, issues, transfers, and replenishment are not master data. Introduce an `Inventory` module if those behaviors enter scope.
- Flights and work orders remain in `Operations`; customer agreements and negotiated pricing remain in `Contracts`.

## 6. Module Dependency Rules

- Modules must not reference another module's Domain, Application, Infrastructure, or Presentation internals.
- Cross-module access must happen through one of these mechanisms:
  - Contracts/read-model assemblies
  - Integration events
  - Explicit application interfaces owned by the consuming module only when truly necessary
- A module may own its internal persistence model and schema.
- Shared building blocks must stay generic and business-neutral.
- Modules must not communicate by referencing each other's internals or DbContexts.
- Modules communicate through contracts, read models, integration events, or the API surface.
- If two modules need the same business concept, prefer an explicit contract or snapshot instead of sharing the internal entity.
- Circular dependencies between modules are forbidden.

## 7. Eventing and Transactional Messaging Rules

- Use domain events for important business events inside a module boundary.
- Use integration events for communication between modules.
- Domain events should describe something that already happened in business language.
- Integration events should be stable contracts and must not expose internal entities.
- Transactional integration events must be written to the outbox in the same database transaction as the state change that caused them.
- Outbox processors publish pending integration events after the transaction commits.
- Inbox processors record received integration events and prevent duplicate processing.
- Integration event handlers must be idempotent.
- Do not perform cross-module side effects directly before the originating transaction commits.
- Event names should be past tense, for example ContractActivated, WorkOrderApproved, UserInvited.
- Keep event payloads minimal but sufficient; include ids and business facts, not full aggregate graphs.

## 8. Backend Rules

- Business rules belong in domain models, value objects, or domain services.
- Handlers orchestrate; they do not hide business rules.
- Minimal API endpoints should stay thin.
- Validation must run before handlers execute.
- Use EF Core directly for normal persistence needs.
- Use global NuGet package versioning through Directory.Packages.props.
- Project files should reference packages without inline versions unless there is a documented exception.
- Avoid generic repositories and generic unit-of-work abstractions.
- Repositories are allowed only when they add real aggregate or business value.
- Queries may use projection/read models directly.
- Return consistent API errors using ProblemDetails.
- One command should represent one business transaction.
- Transaction boundaries must be explicit and predictable.
- Do not call SaveChanges from random helper methods or deep domain logic.
- Use TimeProvider for current time access in application/infrastructure code.
- Avoid raw DateTime.UtcNow/DateTime.Now inside domain logic; pass time in from the application layer when needed.
- Use Guid identifiers by default for public entity ids and database primary keys.
- API DTOs and frontend types expose ids as Guid-compatible strings.
- Simple reference/master-data entities use plain Guid ids.
- Rich domain aggregates may use strongly typed IDs internally when they improve safety without adding noise.
- Do not expose strongly typed ID wrapper types through API contracts.
- Simple reference/master-data entities do not need heavy DDD patterns unless they have real behavior.

## 9. Result and Error Mapping

- Domain and application code express expected failures with a `Result`/`Result<T>` type carrying a typed `Error` (with an error code and a category such as Validation, NotFound, Conflict, Unauthorized, Failure). Preserve this pattern from the legacy `BuildingBlocks.Domain.Results`.
- Do not throw exceptions for expected business/validation failures. Reserve exceptions for truly exceptional conditions and programmer errors.
- A single centralized mapper at the API edge translates `Error` categories to ProblemDetails/HTTP status codes. Endpoints and handlers must not hand-map errors ad hoc.
- Domain and application failures must map to consistent ProblemDetails responses.
- Validation errors map to 400 Bad Request with validation details.
- Authentication failures map to 401 Unauthorized.
- Authorization failures map to 403 Forbidden.
- Missing resources map to 404 Not Found.
- Business conflicts map to 409 Conflict.
- Unexpected exceptions map to 500 Internal Server Error through centralized exception handling.
- ProblemDetails should include type, title, status, detail, instance, traceId, and an application error code when available.
- Validation ProblemDetails should include field-level validation details.
- Endpoints should not invent custom error formats.

## 10. API Versioning Rules

- All public API routes start with /api/v1.
- Do not expose unversioned business endpoints.
- Version changes should be intentional and documented.
- Mobile and web clients consume the same versioned API unless a later version is explicitly introduced.

## 11. Frontend Rules

- The Blazor portal is an operational product UI, not a marketing site.
- Prioritize clarity, density, speed, and repeatable workflows.
- Use feature-based organization under `OperationsSystem.Blazor.Client/Features`.
- Use shared shells/components intentionally (`Shared/`), not as a dumping ground.
- Use Radzen.Blazor (Material3) as the only component library; do not add Bootstrap, Tailwind, or another library.
- Styling uses design tokens (`--os-*`) in `tokens.css` and Radzen layout primitives; no hardcoded hex/rgb in `.razor`/`.razor.css`.
- Every data view handles loading, empty, and error states explicitly via the shared shells.
- The detailed Blazor/Radzen UI conventions live in `.cursor/rules/frontend-blazor.mdc`.
- Avoid hand-written anonymous API response shapes in components; use typed DTOs.
- Avoid raw `HttpClient`/`fetch` scattered through components; go through the typed API client over `BrowserApiClient`.
- Features should have typed API client methods owned by a feature or shared API layer.

## 12. Database Rules

- Use EF Core migrations.
- Never make manual schema changes outside migrations.
- Development may apply migrations directly.
- Production must use reviewed SQL migration scripts and backups.
- Database constraints should enforce real invariants where possible.
- Each module owns its schema/tables.
- Prefer deactivate/archive/status-based lifecycle behavior for business records that must remain historically visible.
- Master/reference data usually uses IsActive.
- Users use a status-based lifecycle.
- Contracts, flights, and work orders use explicit business statuses.
- Hard delete only when the data is temporary, incorrect before use, a draft that has no business impact, or explicitly safe to remove.
- Do not add a generic IsDeleted column to every table by default.
- Soft delete/deactivation rules must be visible in domain/application behavior, not hidden only in queries.

## 13. Authentication and Authorization

- Use JWT access tokens and refresh tokens.
- Support web, mobile, and future clients through the same auth model.
- Use permission-based authorization.
- Roles are collections of permissions.
- Avoid role-only authorization checks.
- Permission names use lowercase dot format: module.resource.action.
- Examples: masterdata.manpower-types.view, masterdata.manpower-types.create, operations.work-orders.approve, identity.users.assign-role.
- Prefer consistent action names: view, create, update, delete, deactivate, approve, reject, assign, export, manage.
- Each module contributes its own permission catalog and UserType compatibility metadata; Identity composes the registered catalogs when validating Role permissions.
- The first created user is the System Admin bootstrap account.
- System Admin can create roles and assign permissions to roles.
- Each user has exactly one role for v1.0.0.
- Each non-administrator Role is compatible with exactly one of the fixed `StationStaff` or `CustomerContact` UserTypes; permission compatibility is enforced when roles are edited and assigned.
- Users cannot have multiple roles unless this rule is explicitly changed in the decisions log.
- Keep future SSO/external-provider support in mind, but do not overbuild it early.

### Authentication Schemes

- The web app uses an in-memory access token plus an httpOnly, secure, rotated refresh-token cookie.
- The backend auth foundation must support Bearer tokens as a first-class scheme so the deferred mobile client can authenticate without redesign.
- Endpoints select their scheme/policy explicitly. Do not assume a single global scheme.

### Identity And StaffMember Linkage

- Identity owns authentication users, roles, and permissions.
- The operational `StaffMember` concept (named `Employee` in the legacy project) represents a person who performs work and is scoped to one station; it is a MasterData concern.
- A User may be linked to a StaffMember. This link drives data scoping and, later, the mobile client's "current staff member" context.
- Keep the link explicit and cross-module-safe (contracts/read models), not a hidden foreign key into another module's internals.

### Data Scoping (Row-Level Authorization)

- Authorization has two axes: permission checks (can the user perform this action?) and data scope (which records may the user see/act on?).
- v1.0.0 scopes each StationStaff User through one linked StaffMember to exactly one Station, and each CustomerContact User to exactly one Customer.
- Data scoping is enforced server-side in queries and command guards, not only hidden in the UI.
- Scope rules must be explicit, testable, and consistent across endpoints; do not scatter ad-hoc station filters through random handlers.
- A System Admin (or an explicit all-stations scope) may bypass station scoping where the business requires it.

## 14. API Contract Rules

- The API is the primary contract.
- The Blazor web portal, Android, and future integrations consume the same API.
- Do not create special business behavior for a specific client.
- Endpoints must use stable request/response DTOs.
- Errors must be consistent and documented.
- Use OpenAPI as a first-class artifact.
- Pagination, filtering, and sorting conventions must be consistent across list endpoints.
- Request/response DTOs should be explicit and stable; do not leak EF entities.
- Do not wrap every successful response in a generic envelope.
- Single-item responses return the DTO directly.
- List endpoints return PagedResult<T>.
- Create endpoints return 201 Created with the created DTO or created id.
- Update endpoints return 204 No Content unless the UI needs the updated DTO.
- Delete/deactivate endpoints return 204 No Content.

List endpoint query conventions:

```text
GET /api/v1/master-data/manpower-types?page=1&pageSize=20&search=engineer&sort=name:asc&isActive=true
```

- page is 1-based.
- Default pageSize is 20.
- Maximum pageSize is 100 unless a specific endpoint documents a smaller maximum.
- search is free text.
- sort uses field:direction, for example name:asc or createdAt:desc.
- Filtering uses explicit typed query parameters, not generic dynamic filter strings.
- Avoid OData and generic dynamic query languages for v1.0.0.

## 15. Caching Rules

- Use in-memory caching/HybridCache for frequently requested, low-volatility data.
- Good cache candidates include reference data, configuration, permission catalogs, and small lookup lists.
- Do not cache everything by default.
- Cache only when the data is read frequently, changes rarely, or a measured bottleneck exists.
- Every cache entry must have an explicit expiration or invalidation strategy.
- Cached data must never become the only source of truth.
- Be careful caching authorization-sensitive data; permissions and roles must be invalidated or refreshed when changed.
- Redis is not part of v1.0.0 unless distributed caching becomes a measured need.

## 16. Audit Rules

- Audit business-critical changes: identity, permissions, customers, contracts, flights, work orders, billing/pricing-related data, and destructive/deactivation actions.
- Use automatic persistence-level audit for important entity field changes where practical.
- Use explicit business audit/events for meaningful workflow actions such as work-order approval, contract termination, role/permission changes, and destructive/deactivation actions.
- Audit records should capture actor user id, timestamp, entity name/id, action, correlation id, and relevant before/after values where practical.
- Do not audit sensitive secrets such as passwords, refresh tokens, access tokens, private keys, or raw credentials.
- Audit behavior should be consistent and testable, not scattered manually through random handlers.

## 17. Background Job Rules

- Use Quartz.NET for v1.0.0 scheduled/background jobs.
- Background jobs may run in-process for v1.0.0 when the system is deployed as a single backend application.
- Use jobs for outbox processing, status sync, expiry notifications, cleanup, and retryable scheduled tasks.
- Jobs must be idempotent where possible.
- Jobs must log failures with enough context to diagnose them.
- Retry behavior must be explicit.
- Long-running or business-critical jobs should record execution status.
- Do not hide essential user-facing workflows only inside background jobs.
- If persistent ad-hoc job queues, dashboard management, or distributed workers become necessary later, evaluate Hangfire or a dedicated worker model then.
- If scaling requires distributed job execution later, revisit the hosting model before adding complexity.

## 18. Testing Rules

- Domain unit tests are required for business rules.
- Validators should be tested when rules are non-trivial.
- API integration tests are required for important workflows.
- Authentication and authorization flows need integration coverage.
- Database behavior should be tested where persistence rules matter.
- Use Testcontainers where practical for integration tests.
- Every rewritten feature must include a short business behavior checklist extracted from the old project.
- Tests should prove the new behavior, not merely mirror old implementation details.

## 19. Observability and Logging

- Use structured logs.
- Include correlation IDs.
- Trace important requests and background jobs.
- Log exceptions centrally.
- Never log sensitive information.
- Expose health checks.
- Add metrics/traces with OpenTelemetry.

## 20. Performance and Scalability Rules

- The application must be designed to serve a large number of users without unnecessary lag.
- All list endpoints must use server-side pagination.
- Avoid loading full tables into memory for filtering, sorting, or paging.
- Avoid N+1 query patterns; use projections, joins, includes, or separate optimized queries intentionally.
- Add database indexes for common filters, lookups, foreign keys, and uniqueness rules.
- Use async I/O and cancellation tokens for request, database, and external operations.
- Keep API hosts stateless where practical so horizontal scaling remains possible.
- Move slow or retryable work to background jobs when the user does not need to wait for it.
- Do not block request threads with CPU-heavy or synchronous I/O work.
- Use caching for hot reference data, but never as a substitute for correct query design.
- Measure before optimizing deeply; prioritize obvious scalability hygiene from the start.

## 21. Feature Rewrite Workflow

For every feature rewritten from the old project:

1. Locate the old feature files.
2. Extract business facts: fields, rules, relationships, constraints, permissions, workflows, and edge cases.
3. Decide the clean v1.0.0 API and domain model.
4. Implement the new feature using the new architecture.
5. Add focused domain tests.
6. Add API/integration tests for important workflows.
7. Compare behavior against the old project where necessary.
8. Complete the feature checklist before considering the feature done.

Feature completion checklist:

- Old-project reference notes created
- Domain model/value objects updated where needed
- Business rules captured and tested
- Domain events/integration events identified where needed
- Caching needs reviewed for frequently requested data
- Request/response DTOs created
- Validator added for write requests
- EF configuration and migration added
- Minimal API endpoint added under /api/v1
- ProblemDetails/error mapping verified
- Permission requirements applied
- OpenAPI contract visible
- Blazor typed API client method/DTO added where applicable
- Blazor page/component added where applicable
- Domain tests added
- API/integration tests added for important workflows
- Old-project reference files documented in the work notes

Example agent instruction:

```text
Create the new v1.0.0 ManpowerType feature from scratch.
Use the old ManpowerType implementation only as business reference.
Extract fields, invariants, duplicate rules, active/inactive behavior, EF constraints, permissions, and API/query needs.
Do not copy the old architecture blindly.
Implement using the new v1.0.0 architecture rules in this document.
```

## 22. Cross-Cutting Conventions

### Tenancy

- v1.0.0 is single-tenant. Do not build tenant isolation now.
- Do not design schema, keys, or auth in a way that would make adding tenancy impossible later.

### Localization And RTL

- The product is bilingual: Arabic and English, with full right-to-left support.
- No hardcoded user-facing strings. The Blazor portal uses a localization layer (`IStringLocalizer`/resources, or a centralized `UiStrings` until fully wired); backend keeps validation/error messages localizable and honors `Accept-Language`.
- Decide per data field whether it is localized (stored per language) or language-neutral, and keep that consistent within a module.
- The frontend uses logical CSS properties (`margin-inline`, `padding-inline`) and direction-aware components so LTR and RTL both render correctly.

### Time, Money, And Units

- Store and exchange time in UTC ISO-8601; render in the user/operation time zone at the edges.
- Standardize on `DateTimeOffset` for instants across domain, persistence, and contracts to avoid the legacy `DateTime`/`DateTimeOffset` mix. Use `DateOnly`/`TimeOnly` where a full timestamp is not meaningful.
- Money uses `decimal` with explicit precision and an explicit currency, with defined rounding. Never use floating point for money.
- Reuse a single `Money` value object across modules rather than re-implementing money handling per feature.

### Concurrency

- Records that multiple users can edit use optimistic concurrency: a rowversion token in the database and ETag/`If-Match` at the API. Conflicts return 409.

### Secrets And Configuration

- No secrets in source. Configuration is environment-driven via user-secrets locally and environment variables/secret stores when deployed.
- Never log secrets or credentials.

### Attachments

- Binary files (work-order attachments, contract documents, customer signatures) are stored in object/file storage through a storage abstraction: local filesystem in v1.0.0, designed to swap to S3/Azure Blob later without touching callers.
- The database stores only attachment metadata (id, content type, size, storage key/path, owning entity, uploaded-by, timestamps), never the bytes. This is a deliberate change from the legacy `varbinary(max)` blobs.
- Attachments validate content type and size at the boundary, are stored outside the executable/served path, and are access-controlled by permission and data scope.

## 23. Data Migration And Cutover

- The rewrite is gradual; the old and new systems may run side by side during transition.
- For each migrated area, define: source-of-truth ownership during transition, a data backfill/migration plan, and a verification step against old behavior.
- Prefer migrating data with reviewed, repeatable scripts; never hand-edit production data.
- Keep `docs/architecture/migration-inventory.md` current as the single view of what is migrated, in progress, or pending.

## 24. Agent Instructions

- For architecture-level changes, read this document first.
- When using the old project, state which files were used as reference.
- Do not introduce new technologies without updating this file and recording the decision.
- Prefer simple, explicit code over clever abstractions.
- Keep changes aligned with the module and feature being worked on.
- Add or update tests with meaningful business coverage.

## 25. Foundational Patterns To Preserve From The Legacy

The legacy is already a DDD modular monolith with strong patterns. Preserve these deliberately; do not lose them in the rewrite.

### Result / Error

- See Section 9. Domain and application return `Result`/`Error`; one central mapper converts to ProblemDetails.

### Snapshots And Cross-Module Denormalization

- When an aggregate references data owned by another module (customer, currency, station, operation type, aircraft type), capture an immutable point-in-time snapshot value object inside the aggregate instead of a live foreign key into the other module.
- Snapshots preserve historical correctness: editing a customer in MasterData must not retroactively change the customer details printed on an old contract or work order.
- A snapshot contains the source id plus the fields the owning aggregate needs (e.g. names, codes). For bilingual fields, capture both languages (or a language-neutral key), consistent with the localization rules.
- Snapshots are normally immutable for the life of the record. If a business case requires refreshing a snapshot, that refresh must be an explicit, intentional operation, never an implicit live join.
- Reference-by-id is still correct for same-module relationships and for cheap, non-historical lookups; snapshots are for cross-module, historically significant data.

### Business Number Sequences

- Human-readable business identifiers (contract numbers, work order numbers) are generated through an explicit numbering mechanism, separate from the surrogate `Guid` primary key.
- Define each sequence's scope (e.g. per-station, optionally per-year), format, and reset behavior explicitly.
- Allocation must be concurrency-safe and gap-aware as the business requires; this is an explicit exception to the default optimistic-concurrency rule (a serializable transaction or database sequence is acceptable for allocation).
- The numbering scheme is a documented business rule, not an incidental implementation detail.

### Aggregate Update Semantics

- For aggregates with large child graphs (e.g. Contract pricing lines, Work Order tasks/service lines), the update operation may clear and rebuild child collections from validated input/draft DTOs.
- When rebuilding, deliberately preserve derived/consumed state that must survive an edit (e.g. consumed advance-payment balances, package remaining balances) by re-applying it by stable id.
- Reuse existing child entity ids on rebuild where history or downstream foreign keys depend on them; do not orphan rows with a blind clear-and-insert.
- These preservation rules are business rules and must be covered by tests.

### Seed Data, Well-Known Ids, And The System Actor

- Reference/master data that the system always needs is seeded deterministically.
- Well-known seed entities (currencies, default country, the Ad Hoc operation type, AOG/On-Call services, etc.) use fixed, stable GUIDs that are identical across all environments and never regenerated. Domain rules may depend on these ids.
- A fixed `SystemUserId` represents the system as the actor for automated/background actions.
- Automated/system-performed actions (background status transitions, scheduled jobs, seeders) record the `SystemUserId` as the actor in audit, since there is no human user.

## 26. Real-Time And Notifications

- v1.0.0 delivers in-app and live updates to the web client via SignalR.
- Notifications persist to a store (so users have an inbox/history with read/archive state) and are pushed live through a delivery abstraction.
- The delivery abstraction is a composite pusher: each transport (SignalR now; a mobile push provider such as FCM later) implements a common interface, and a failure in one transport must not block the others.
- When a notification is raised as a consequence of a business event, prefer raising it off domain/integration events (and the outbox where the event is transactional) rather than inline side effects.
- Device-token registration and mobile push (FCM/APNS) are deferred with the mobile client, but the pusher abstraction must allow adding them without reworking notification producers.
- Real-time delivery is best-effort and must never be the only path to a state change; the authoritative state is always the persisted data, re-fetchable via the API.

## 27. Mobile Strategy (Deferred For v1.0.0)

- v1.0.0 focuses on the web client and the shared backend API. The mobile client is deferred.
- Do not build the mobile BFF, the mobile offline-sync protocol, mobile-specific JWT policies, or FCM push in v1.0.0.
- Do not design them out either: keep business rules in the domain/application (never web-only), keep a Bearer auth scheme possible, keep the notification pusher abstraction, and keep the Identity-User-to-Employee link in mind.
- When mobile returns, it gets a dedicated BFF/read + offline-sync surface that reuses the same domain and business rules; it does not get its own business logic. Record that decision and its API/versioning shape here when it happens.

## 28. Decisions Log

Use this section to record decisions as they become final.

| Date | Decision | Reason |
|------|----------|--------|
| 2026-06-18 | Old project is business reference only | Preserve business knowledge without carrying old technical debt |
| 2026-06-18 | SQL Server is the v1.0.0 database | Matches current operational knowledge and reduces migration risk |
| 2026-06-18 | MediatR is the initial mediator | Familiar, supports pipeline behaviors, and matches the intended CQRS style |
| 2026-06-18 | Public API routes start with /api/v1 | Keeps API contracts explicit and future-version friendly |
| 2026-06-18 | Redis is not included by default in v1.0.0 | Avoid distributed cache complexity until a measured need exists |
| 2026-06-18 | Quartz.NET is the v1.0.0 background job scheduler | Fits scheduled jobs in a modular monolith without adding queue/dashboard complexity early |
| 2026-06-18 | List endpoints use explicit query parameters | Keeps filtering/sorting predictable and avoids dynamic query coupling |
| 2026-06-18 | Permission names use module.resource.action | Keeps authorization consistent and easy to reason about |
| 2026-06-18 | Users have one role in v1.0.0 | Keeps authorization simple and predictable while roles remain permission collections |
| 2026-06-18 | Transactional integration events use outbox/inbox processing | Keeps cross-module communication reliable and idempotent |
| 2026-06-18 | Central Package Management is required | Keeps NuGet versions consistent across the solution |
| 2026-06-18 | v1.0.0 is single-tenant, tenancy deferred but not designed out | Matches current client scope without blocking future growth |
| 2026-06-18 | Bilingual (Arabic/English) with full RTL support | Operational users need both languages; RTL is costly to retrofit |
| 2026-06-18 | Timestamps stored/exchanged in UTC, localized at edges | Avoids time-zone bugs in flights and work orders |
| 2026-06-18 | Money uses decimal with explicit currency and rounding | Prevents floating-point and currency errors in billing |
| 2026-06-18 | Optimistic concurrency (rowversion + ETag/If-Match) on editable records | Prevents lost updates from concurrent operators |
| 2026-06-18 | Web stores access token in memory with httpOnly refresh cookie | Reduces XSS token-theft risk |
| 2026-06-18 | No secrets in source; environment-driven configuration | Standard secure configuration hygiene |
| 2026-06-18 | Old Blazor solution copied under legacy/ as read-only reference | Makes business reference reliably accessible to the rewrite |
| 2026-06-18 | Migration progress tracked in migration-inventory.md | Prevents losing strong old behavior during gradual rewrite |
| 2026-06-18 | Domain/application use the Result/Error pattern; one central mapper to ProblemDetails | Preserves the legacy pattern and avoids mixing exceptions with result objects |
| 2026-06-18 | Cross-module references use immutable point-in-time snapshots, not live foreign keys | Preserves historical correctness and respects module boundaries |
| 2026-06-18 | Business numbers (contract/work-order) use an explicit, scoped, concurrency-safe sequence | Sequence allocation is the documented exception to optimistic concurrency |
| 2026-06-18 | Aggregate updates may rebuild child graphs but must preserve derived/consumed state by id | Prevents lost balances/history on edit |
| 2026-06-18 | Seed data uses fixed stable GUIDs; a fixed SystemUserId is the actor for automated actions | Domain rules depend on well-known ids; audit needs a system actor |
| 2026-06-18 | Web real-time via SignalR; notification delivery behind a composite pusher abstraction | Live web updates now, mobile push (FCM) addable later without rework |
| 2026-06-18 | Attachments stored in object/file storage (filesystem now, cloud-ready); DB holds metadata only | Replaces legacy varbinary blobs; keeps DB lean and storage swappable |
| 2026-06-18 | Standardize on DateTimeOffset for instants | Removes the legacy DateTime/DateTimeOffset inconsistency |
| 2026-06-18 | Station-based data scoping (row-level) in addition to permission checks | Operational data must be scoped per station, not only gated by permission |
| 2026-06-18 | Auth foundation supports cookie (web) and Bearer (future mobile) schemes; per-endpoint selection | Avoids a single-scheme assumption that would block mobile later |
| 2026-06-18 | Mobile client deferred for v1.0.0 but not designed out | Focus web + API first; keep domain/API mobile-ready |
| 2026-06-18 | Identity uses custom DDD aggregates, not ASP.NET Core Identity | Full control, matches the legacy model and DDD rules |
| 2026-06-18 | Passwords hashed with ASP.NET Core PasswordHasher (PBKDF2) | Battle-tested, no extra dependency |
| 2026-06-18 | Tests use xUnit + Shouldly/FluentAssertions + NSubstitute + Testcontainers | Consistent, well-supported test stack |
| 2026-06-18 | DTO mapping is manual and explicit | Avoids mapping-library magic and debugging pain |
| 2026-06-18 | MediatR pinned to last free/MIT version (12.4.x) | Avoids the commercial-license change in newer versions |
| 2026-06-19 | Web client is a Blazor Web App (Interactive Auto) with Radzen.Blazor (Material3) as the only UI library | Single .NET stack across backend and web; reuses C# domain knowledge; supersedes the React/Vite/Tailwind/shadcn/Orval frontend decision |
| 2026-06-19 | The React project (`src/Host/OperationsSystem.Web`) is retired and removed | Frontend consolidated on Blazor; avoids maintaining two parallel web clients |
| 2026-06-19 | Blazor calls the API through a hand-written typed client over `BrowserApiClient` (no Orval/TanStack Query/Zod) | Generated TS clients no longer apply; typed C# client keeps DTOs explicit and shared with the contracts |
| 2026-06-21 | v1.0.0 uses a `MasterData` module instead of legacy `Core` and `Store` modules | Names the business capability explicitly, avoids an ambiguous catch-all `Core`, and keeps catalog-only items together until real inventory behavior requires an `Inventory` module |
| 2026-06-21 | Rename the legacy individual `Employee` concept to `StaffMember`; reserve Manpower terminology for labor categories/pricing | Produces clear person-level domain language while preserving established manpower pricing concepts |
| 2026-06-21 | Users have one of three fixed types (`SystemAdministrator`, `StationStaff`, `CustomerContact`) plus one compatible permission-bearing Role | Separates immutable business identity/data scope from configurable authorization capabilities |
