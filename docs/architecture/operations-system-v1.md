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
- Blazor/Radzen UI patterns
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
- MediatR for commands, queries, and pipeline behaviors
- ProblemDetails for API errors
- Serilog for structured logging
- OpenTelemetry for traces and metrics
- Health checks
- HybridCache/in-memory caching for frequently requested data
- Central Package Management with Directory.Packages.props
- Redis is not part of v1.0.0 unless a measured distributed-caching need appears

### Frontend

- React
- TypeScript
- Vite
- Tailwind CSS
- shadcn/ui
- TanStack Query
- React Hook Form
- Zod
- React Router

### Mobile

- Existing Android app remains an important API consumer
- React web and Android must consume the same backend API
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
    Core/
    Contracts/
    Operations/
    Store/
    Notifications/
    Audit/

  Host/
    OperationsSystem.Api/
    OperationsSystem.Web/
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

Recommended frontend layout:

```text
src/
  app/
  features/
    manpower-types/
      api/
      components/
      pages/
      hooks/
      schemas/
      types/
  shared/
    ui/
    api/
    auth/
    layout/
```

- The API host composes modules but must not contain business logic.
- Module Api projects own endpoint mapping for their module.
- Contracts projects contain cross-module DTOs, read models, events, or public contracts only.
- Frontend feature folders own feature-specific API hooks, schemas, pages, and components.
- Shared frontend folders are for truly reusable primitives and app-wide services only.

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

- The React app is an operational product UI, not a marketing site.
- Prioritize clarity, density, speed, and repeatable workflows.
- Use feature-based organization.
- Use shared UI components intentionally, not as a dumping ground.
- Forms use React Hook Form plus Zod.
- Server state uses TanStack Query.
- Styling uses Tailwind and shadcn/ui conventions.
- Avoid random one-off styling unless the feature truly requires it.
- OpenAPI is the preferred source for frontend API types and clients.
- Generate TypeScript API types/client from the backend OpenAPI contract when practical.
- TanStack Query hooks should wrap the typed API client.
- Avoid hand-written anonymous API response shapes in frontend code.
- Avoid direct fetch calls scattered through components.
- Frontend features should have typed API functions/hooks owned by the feature or a shared API layer.

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
- Examples: core.manpower-types.view, core.manpower-types.create, operations.work-orders.approve, identity.users.assign-role.
- Prefer consistent action names: view, create, update, delete, deactivate, approve, reject, assign, export, manage.
- The first created user is the System Admin bootstrap account.
- System Admin can create roles and assign permissions to roles.
- Each user has exactly one role for v1.0.0.
- Users cannot have multiple roles unless this rule is explicitly changed in the decisions log.
- Keep future SSO/external-provider support in mind, but do not overbuild it early.

## 14. API Contract Rules

- The API is the primary contract.
- React, Android, and future integrations consume the same API.
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
GET /api/v1/core/manpower-types?page=1&pageSize=20&search=engineer&sort=name:asc&isActive=true
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
- Frontend API type/function/hook added where applicable
- React page/component added where applicable
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
- No hardcoded user-facing strings. Frontend uses an i18n layer; backend keeps validation/error messages localizable and honors `Accept-Language`.
- Decide per data field whether it is localized (stored per language) or language-neutral, and keep that consistent within a module.
- Frontend uses logical CSS properties and direction-aware components so LTR and RTL both render correctly.

### Time, Money, And Units

- Store and exchange time in UTC ISO-8601; render in the user/operation time zone at the edges.
- Use date-only and time-only types where a full timestamp is not meaningful.
- Money uses `decimal` with explicit precision and an explicit currency, with defined rounding. Never use floating point for money.

### Concurrency

- Records that multiple users can edit use optimistic concurrency: a rowversion token in the database and ETag/`If-Match` at the API. Conflicts return 409.

### Secrets And Configuration

- No secrets in source. Configuration is environment-driven via user-secrets locally and environment variables/secret stores when deployed.
- Never log secrets or credentials.

### Attachments

- Work-order and similar attachments validate type and size, are stored outside the executable path, and are access-controlled by permission.

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

## 25. Decisions Log

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
