# Application Engineering Instructions

Use this file as the root `AGENTS.md` when building a new application with the architecture and conventions described below. Replace `AppName` with the new application's name and choose modules from its business domain. These instructions are self-contained; they do not require the source OperationsSystem repository.

## Scope and decision making

- Build an API-first modular monolith with a Blazor web portal. Add native Android only when required.
- Preserve these technology, architecture, and style defaults unless the user explicitly requests a different approach. Document significant departures in an architecture decision record.
- Inspect existing code, configuration, tests, and module documentation before editing. Follow the nearest established feature pattern.
- Prefer current implementation and recorded decisions over outdated planning documents. Do not copy legacy technical debt or assume that a planned feature already exists.
- Implement the new application's business rules. Do not import aviation workflows, company branding, user categories, or other source-project domain assumptions.
- Keep changes focused. Avoid speculative frameworks, generic CRUD engines, and abstractions without a concrete need.

## Technology baseline

The versions below are a snapshot of the source project, not a claim that they are the latest available versions. Pin dependencies centrally and review compatibility, security, and licensing before upgrades.

| Concern | Default |
| --- | --- |
| Runtime | .NET 10, C#, ASP.NET Core |
| SDK | `global.json`; source baseline `10.0.101`, `rollForward: latestFeature` |
| HTTP API | Minimal APIs, OpenAPI, Scalar development UI |
| Persistence | EF Core 10 with SQL Server |
| CQRS | MediatR `12.4.1`; preserve this pin unless a licensing/version decision is recorded |
| Validation | FluentValidation `12.1.1` and a MediatR validation pipeline |
| Web | Blazor Web App with a separate `.Client` project, Interactive Auto |
| Components | Radzen.Blazor `11.1.2`, `RadzenTheme Theme="default"` |
| Identity | Custom User/Role/UserSession domain models, ASP.NET Core `PasswordHasher`, JWT access tokens and rotated refresh tokens |
| Observability | Serilog, OpenTelemetry, ASP.NET Core health checks |
| Background work | Hosted services; Quartz where scheduled durable-message dispatch is needed |
| Live updates | SignalR; Firebase Cloud Messaging for Android alerts when required |
| Tests | xUnit, Shouldly, Microsoft.AspNetCore.Mvc.Testing, SQL Server Testcontainers, coverlet |
| Optional exports | ClosedXML for spreadsheets; PDFsharp-MigraDoc for PDFs |
| Optional Android | Kotlin, Jetpack Compose, Room, WorkManager, SignalR, FCM |

- Keep package versions in `Directory.Packages.props`; use versionless `PackageReference` entries in projects.
- Set `TargetFramework=net10.0`, `LangVersion=latest`, nullable reference types, implicit usings, warnings as errors, and build-time code-style enforcement in `Directory.Build.props`.
- Map DTOs manually. Do not add AutoMapper or another mapping library.
- Do not add another web component framework, Bootstrap, Tailwind, or a React frontend to the product portal. A separate documentation tool does not establish the application's frontend stack.
- Use in-memory caching for appropriate measured needs. Do not introduce Redis, a message broker, microservices, or distributed infrastructure by default.

## Solution structure

```text
AppName.slnx
global.json
Directory.Build.props
Directory.Packages.props
.editorconfig
src/
  BuildingBlocks/
    BuildingBlocks.Domain/
    BuildingBlocks.Application/
    BuildingBlocks.Contracts/
    BuildingBlocks.Infrastructure/
    BuildingBlocks.Api/
  Modules/
    <Module>/
      <Module>.Domain/
      <Module>.Application/
        Abstractions/
        Authorization/
        Contracts/
        Features/<Resource>/
      <Module>.Contracts/
      <Module>.Infrastructure/
        Persistence/
      <Module>.Api/
  Host/
    AppName.Api/
    AppName.Blazor/
      AppName.Blazor/
      AppName.Blazor.Client/
        Api/
        Auth/
        Localization/
        Shared/
        Features/<Feature>/Pages/
        Features/<Feature>/Components/
    AppName.Mobile/                 # Only when Android is in scope
tests/
  <Module>.Domain.UnitTests/
  <Module>.Application.UnitTests/
  <Module>.Infrastructure.UnitTests/
  <Module>.IntegrationTests/
docs/
  architecture/
  modules/
```

- Select modules by business ownership. Identity, Notifications, and Audit are examples of reusable capabilities; do not create empty modules merely to reproduce a directory tree.
- API hosts compose services, middleware, modules, and hubs. They contain no business rules.
- Each module owns its domain, use cases, persistence, endpoint mappings, and database schema.
- Domain depends only on business-neutral domain building blocks. It must not depend on EF Core, HTTP, or UI frameworks.
- Application depends on its Domain, appropriate Contracts, and application building blocks. EF Core query APIs over a module-owned context interface are permitted.
- Infrastructure implements application abstractions and owns EF configurations, migrations, and external integrations.
- Module Api code delegates to Application. Use an endpoint-module interface such as `IEndpointModule` and module registration extensions.
- A module must never reference another module's Domain, Application, Infrastructure, Api, or DbContext. Cross-module dependencies go through Contracts, read interfaces, and integration events.
- Contracts expose stable DTOs, reader interfaces, and integration events, never internal EF entities. Application-local response DTOs may live under `Application/Contracts`.
- Keep BuildingBlocks business-neutral. Avoid circular references and shared mutable domain models.

## Features, domain behavior, and CQRS

- Organize Application by vertical feature slices: `Features/Products/CreateProduct.cs`, `UpdateProduct.cs`, and `ProductQueries.cs`, for example. A small slice may colocate its request, validator, and handler.
- Use immutable record requests implementing `ICommand`, `ICommand<T>`, or `IQuery<T>` with corresponding MediatR handler interfaces returning `Result` or `Result<T>`.
- Commands mutate; queries read. One command represents one explicit business transaction.
- Handlers validate references and permissions, load state, invoke domain behavior, persist, and map results. Keep invariants and state transitions in domain methods.
- Use rich aggregates for behavior-heavy workflows and simple entities for straightforward reference data. Do not manufacture DDD complexity.
- Encapsulate mutations with private setters, factory methods such as `Create`, and methods such as `Update`, `Activate`, or `Approve`. Use a private parameterless constructor where EF materialization requires it.
- Represent expected failures with typed `Error` values and stable codes, for example `Catalog.Product.DuplicateCode`. Throw exceptions only for exceptional conditions or programming mistakes.
- Run FluentValidation before handlers. Boundary validation does not replace domain invariants.
- Use `Guid` primary/public identifiers. Keep business numbers separate, with explicit numbering scope and concurrency rules.
- Use `TimeProvider` in application/infrastructure code and pass time into domain methods. Use UTC `DateTimeOffset` for instants, `DateOnly` for dates, and `TimeOnly` for local clock values.
- Use `decimal` for money with explicit currency, precision, and rounding rules.
- Capture cross-module facts as immutable snapshots when historical accuracy requires them. Use contract-based lookups for current data; never navigate another module's entities.
- Preserve stable child IDs and accumulated history when updating aggregate collections.
- Pass cancellation tokens through asynchronous calls. Never use `.Result`, `.Wait()`, `async void`, or unobserved background work in request handling.

## Persistence and transactions

- Use EF Core directly through a module-owned context abstraction such as `ICatalogDbContext`. Avoid generic repositories and generic unit-of-work wrappers.
- Put mapping in `IEntityTypeConfiguration<T>` classes. Configure schema, required fields, lengths, decimal precision, indexes, uniqueness, and concurrency explicitly.
- Use projections and `AsNoTracking` for read-only queries. Filter, sort, and page in SQL; avoid N+1 queries and loading entire tables.
- Keep `SaveChangesAsync` at explicit application transaction boundaries. Domain models and incidental helpers must not save independently.
- Use SQL Server rowversion for concurrently editable records. Expose an ETag and accept `If-Match` where that resource contract requires it. Translate stale-write conflicts consistently to 409, following the source project's convention.
- Database uniqueness constraints must back application duplicate checks. Translate relevant constraint/concurrency failures into meaningful results.
- Use EF migrations for every schema change. Keep migrations and model snapshots together. Production changes use reviewed migration scripts and a backup/recovery plan; do not silently migrate production at startup.
- Prefer explicit active/inactive, archive, or business-status lifecycles. Do not add universal `IsDeleted` fields or silently hide lifecycle rules in query filters.
- Use deterministic IDs for seeded reference values and a defined system actor for automated audit entries.
- Do not introduce tenancy columns or tenant abstractions unless tenancy is a requirement.

## Messaging, audit, and external effects

- Domain events describe completed business facts within a module. Integration events are stable cross-module contracts with minimal payloads.
- Persist transactional integration events in an outbox in the same database transaction as their originating state change.
- Dispatch after commit. Use inbox deduplication and idempotent consumers; assume delivery may repeat.
- Do not send email, publish notifications, or trigger cross-module writes before the originating transaction commits. Use durable delivery for effects that must survive process failure.
- Record meaningful audit events with actor, action, resource, UTC time, and correlation information. Redact secrets and sensitive fields.
- Persist notification inbox/history independently of live delivery. Compose SignalR and FCM transports so one transport failure does not prevent the others from being attempted.
- Treat pushed updates as hints to reconcile authoritative state through the API. Authorize hub connections and scope recipients; never broadcast scoped data globally.
- Keep synchronization messages separate from user-facing notifications.

## API conventions

- Business endpoints begin with `/api/v1`; use consistent resource routes and thin Minimal API handlers.
- Bind typed requests, enforce authorization, dispatch through MediatR, and return explicit DTOs. Never serialize domain entities or expose EF navigation graphs.
- Return a DTO directly for a single resource, `PagedResult<T>` for lists, 201 for creation, and normally 204 for successful updates/deactivation. Do not wrap all successes in a generic envelope.
- Use 1-based `page`, default `pageSize=20`, maximum `pageSize=100`, optional `search`, and explicit typed filters. Use allow-listed `sort=field:asc|desc` values and deterministic ordering.
- Centralize Result-to-ProblemDetails mapping: validation 400, unauthenticated 401, forbidden 403, not found 404, conflict 409, unexpected failure 500.
- Include trace/correlation information and stable application error codes. Validation responses include field errors. Do not leak exception details.
- Use OpenAPI metadata for public contracts. Expose Scalar and development diagnostics only in appropriate environments.
- Localize presentation text using stable codes/resources; never make clients parse English error messages to determine behavior.

## Authentication, authorization, and configuration

- Model users, roles, sessions, and permissions explicitly. Use `PasswordHasher` rather than custom password cryptography.
- Use short-lived JWT access tokens and rotated, revocable refresh tokens. Validate live user/session/security-stamp state, not only JWT signatures.
- Web access tokens stay in memory; refresh tokens use secure HttpOnly cookies with an explicit same-site/CSRF strategy. Mobile uses bearer access tokens and its dedicated refresh exchange.
- Do not store web authentication tokens in localStorage. Never log passwords, tokens, connection strings, private keys, or push device credentials.
- Roles collect permissions named `module.resource.action`. Enforce permissions and record/data scope server-side for both reads and writes. UI guards only improve presentation.
- Derive the new application's user types and scope rules from its requirements. Do not assume the source project's one-role or station/customer rules apply universally.
- Use validated options and environment-specific configuration. Keep secrets in local user-secrets or deployed secret stores/environment variables.
- Apply HTTPS, constrained CORS, security headers, and rate limits on abuse-prone endpoints. Persist Data Protection keys appropriately when encrypted durable payloads or cookies must survive restarts.
- Store uploaded bytes behind `IFileStorage`, outside executable/public paths. Persist metadata in SQL. Validate size/type, constrain storage paths, and authorize upload and download access.

## Blazor architecture and visual design

- Use a Blazor Web App server host plus a `.Client` project for interactive components. The source implementation uses `InteractiveAutoRenderMode(prerender: false)` because authentication/API access relies on browser interop. Preserve that default unless deliberately redesigning prerender-safe initialization.
- Use Radzen's `default` theme. Do not copy the older Material3 theme instruction.
- Compose an operational UI with a sidebar, top bar, page headers, dense readable tables, and grouped forms. Favor consistent workflows over decorative marketing layouts.
- Define colors, spacing, typography, radii, shadows, motion, and layout dimensions in `wwwroot/tokens.css`. Map Radzen `--rz-*` variables to the application's tokens in `app.css`.
- The source uses `--os-*` tokens and `os-` classes. Retain them or rename the prefix consistently for the new app. Brand colors and logos are replaceable, not architectural requirements.
- Match the source's visual structure: light neutral page background, white cards, muted borders, restrained shadows, clear status colors, and a strong primary accent. Source defaults include a burgundy `#722f37` accent and Inter/Segoe UI typography.
- Use a consistent spacing scale (0.25, 0.5, 0.75, 1, 1.5, 2rem), tokenized corner radii, and responsive layout widths.
- Do not hardcode colors in `.razor` or scoped CSS. Avoid inline `Style` except full-width form inputs (`width: 100%`); prefer component parameters and token-based CSS classes.
- Prefer `RadzenStack`, `RadzenRow`, `RadzenColumn`, `RadzenCard`, and labeled `RadzenFormField` inputs. Use Primary for main actions, Light for secondary actions, and Danger for destructive actions.
- Build reusable `PageHeader`, `AuthLayout`, `LoadingCard`, `EmptyState`, `DetailField`, `RequireAuth`, and `RequirePermission` components before duplicating page shells.
- Follow these page recipes:
  - List: page header, search/filter toolbar, data grid, pagination.
  - Detail: page header, card sections, labeled detail fields.
  - Form: page header, `RadzenTemplateForm` in a card, primary action at the bottom/end.
  - Authentication: centered `AuthLayout` without the application shell.
- Every data view handles loading, empty, error, and success states. Disable duplicate submission and make validation/retry behavior explicit.
- Call typed feature API clients over a shared `BrowserApiClient` using browser fetch through the Blazor host's API proxy. Do not scatter raw HttpClient/fetch calls through components or reference backend persistence from the UI.
- Centralize auth initialization, refresh, error parsing, and permission checks. Keep business decisions on the server.
- Use resource-based Arabic/English localization with centralized localization helpers. Support `lang`/`dir` switching and logical CSS properties such as `margin-inline` and `padding-inline`.
- Check keyboard access, labels, focus visibility, responsive layouts, and RTL behavior.
- Keep subscriptions and JS interop lifecycle-aware; release event handlers, cancellation sources, and disposable resources when components are disposed.

## C# and formatting conventions

- Use UTF-8, LF line endings, final newlines, and no trailing whitespace.
- Use four spaces for C# and two spaces for project/XML configuration, JSON, YAML, JavaScript/TypeScript, and CSS.
- Use file-scoped namespaces, using directives outside namespaces, and System directives first.
- Use PascalCase for types, methods, properties, and constants; camelCase for locals and parameters; prefix interfaces with `I`. Follow an existing class's private-field convention consistently.
- Prefer sealed concrete handlers/services and record request/response types. Use primary constructors for straightforward dependency injection when consistent with neighboring code.
- Prefer explicit built-in types when they improve clarity; use `var` when the constructed/resulting type is apparent. Modern collection expressions are appropriate.
- Use explicit accessibility modifiers, omit unnecessary `this.`, and prefer braces for new control-flow blocks.
- Name asynchronous service operations with `Async`; retain framework/interface-defined names such as MediatR `Handle`.
- Use nullable annotations honestly. Reserve `null!` for deliberate framework materialization cases; do not suppress warnings to conceal unsafe assumptions.
- Comments explain business reasons, invariants, and non-obvious tradeoffs. Avoid comments that merely repeat code.
- Treat EF-generated migrations as generated code for style enforcement. Do not disable quality checks globally to accommodate generated files.

## Verification and delivery

- Test meaningful domain invariants, state transitions, non-trivial validation, permissions, data scope, and idempotency. Test outcomes rather than implementation details.
- Use xUnit and Shouldly. Add test doubles only where needed; do not add a mocking framework just because an old planning document mentions one.
- Use `WebApplicationFactory<Program>` and SQL Server Testcontainers for important API workflows and relational behavior. Docker is required for those integration tests.
- Do not use EF InMemory as proof of SQL Server constraints, transactions, migration behavior, or rowversion semantics.
- Include concurrency/error-contract tests when relevant. Add structural checks for module boundaries as the solution grows; do not claim architecture tests exist until implemented.
- For UI changes, verify loading/error/empty states, validation, protected routes, responsive behavior, and Arabic/RTL presentation.
- Typical verification commands, after replacing `AppName`:

```sh
dotnet restore AppName.slnx
dotnet build AppName.slnx --configuration Release --no-restore
dotnet test AppName.slnx --configuration Release --no-build
dotnet list AppName.slnx package --vulnerable --include-transitive
```

- Run focused tests during development and relevant broader checks before delivery. Report what ran, failures, and unavailable prerequisites honestly.
- CI should restore, build with warnings as errors, run the intended unit/integration suites, and audit dependencies. Ensure newly added test projects are actually included.
- Keep health liveness separate from dependency readiness. Use structured logs, correlation IDs, and OpenTelemetry without exposing sensitive payloads.
- Cache only with an explicit expiry/invalidation strategy and keys that respect authorization scope.

## Optional offline Android client

Apply this section only when native Android is requested.

- Use Kotlin/Jetpack Compose; read local-first UI data from Room and centralize synchronized writes in a sync coordinator.
- Queue offline mutations in a local outbox, keep attachments on disk, and use WorkManager plus bounded retry/backoff for durable delivery.
- Assign a stable client mutation ID and implement server-side idempotency. Coordinate foreground/background uploaders to prevent duplicate submissions.
- Reconcile SignalR updates with REST catch-up after reconnect. Persisted server state remains authoritative.
- Keep FCM user alerts separate from data synchronization. Secure device tokens and revoke registration on logout.
- Share server business rules and authorization with the web portal; mobile-specific endpoints may shape payloads but must not redefine the domain.

## Feature completion workflow

1. Identify business behavior, ownership, permissions, data scope, contracts, and failure cases.
2. Implement domain behavior and meaningful tests.
3. Add application requests, validators, handlers, and explicit persistence boundaries.
4. Add EF mappings/migrations and transactional events where needed.
5. Expose thin authorized endpoints with consistent success/error contracts.
6. Add typed clients and localized UI using existing page recipes.
7. Verify relevant behavior, security boundaries, persistence, and UI states.
8. Update module/architecture documentation and report changes, checks, and remaining limitations.
