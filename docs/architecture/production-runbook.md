# Production Configuration Runbook

This runbook lists the configuration and operational steps required to deploy the Operations System
backend (Audit + Identity + MasterData + Operations + Notifications) to a production environment. The application validates the
security-critical settings below at startup and refuses to boot when they are missing or weak.

## Required configuration

Provide via environment variables or a secrets store (never in source control):

| Setting | Notes |
|---|---|
| `OpenTelemetry:Enabled` | Defaults to `true`. Set `false` to disable tracing/metrics export wiring. |
| `OpenTelemetry:ServiceName` | Service name on exported spans/metrics (default `operations-system-api`). |
| `OpenTelemetry:OtlpEndpoint` | Optional OTLP endpoint URL. When unset, the SDK honors standard `OTEL_EXPORTER_OTLP_*` environment variables. |
| `ConnectionStrings:Default` (or per-module `Identity`/`MasterData`/`Operations`/`Notifications`/`Audit`) | SQL Server connection string. |
| `Database:ApplyMigrationsOnStartup` | Set `false` for production and production-like remote testing. Apply reviewed migrations separately. |
| `Messaging:OutboxDispatchEnabled` | Set `false` for smoke tests where background outbox polling should not touch the remote database. Keep enabled in normal deployments. |
| `Operations:FlightReminders:Enabled` | Defaults to `true`; set `false` alongside outbox dispatch for smoke tests. Normal delivery requires both. `PollIntervalSeconds` defaults to 60, `EnrollmentLookaheadHours` to 13, and `EnrollmentLatenessToleranceMinutes` to 5. |
| `Identity:Jwt:SigningKey` | >= 32 chars of entropy. Startup fails otherwise. |
| `Identity:Jwt:Issuer`, `Identity:Jwt:Audience` | Required. |
| `Identity:Admin:Email`, `Identity:Admin:DisplayName` | Bootstrap administrator. **Leave `Identity:Admin:Password` unset in production** — the admin is then created as an emailed invitation (no default password). |
| `Identity:ActivationUrlBase`, `Identity:PasswordResetUrlBase`, `Identity:EmailChangeConfirmUrlBase` | Public portal URLs for emailed activation, reset-password, and linked-email verification links. |
| `EmailSettings:*` with `EnableEmailNotifications=true` | SMTP host/port/credentials/from. Invitations, password reset, email verification, and MFA depend on durable email delivery. |
| `Security:RateLimit:AnonymousAuthPermitLimit` / `...WindowSeconds` | Defaults 10/60. Tune per environment. |
| `FileStorage:RootPath` | Persistent volume for customer logos (object/file storage, not the served path). |
| `Notifications:Fcm:Enabled` | Set `true` in environments that deliver Android alerts. The persisted inbox and SignalR remain active when false. |
| `Notifications:Fcm:Required` | Production defaults this to `true`, making startup fail until FCM is enabled and configured. Keep `false` only in local/test environments. |
| `Notifications:Fcm:ProjectId` | Firebase project id; required when FCM is enabled. |
| `Notifications:Fcm:ServiceAccountJsonPath` or `...ServiceAccountJson` | Optional explicit Firebase Admin credential. Configure exactly one through a mounted secret or secrets store. When neither is supplied the service uses Application Default Credentials/workload identity. Never commit service-account JSON. |
| Data Protection key ring | Persisted to a shared, backed-up location (see below). Encrypts MFA secrets and durable email bodies and attachments. |

For local developer overrides, use `appsettings.{Environment}.local.json`, .NET user-secrets, or
environment variables. The `.local.json` pattern is intentionally ignored by Git and has lower
precedence than user-secrets, environment variables, and command-line arguments.

The Android `google-services.json` is not an Admin credential. This repository enables FCM in
Development and Production and resolves `App_Data/firebase-admin.json` relative to the API content
root. Provision that ignored file before local startup or publishing. The API publish target fails
when it is absent and copies it to `App_Data/firebase-admin.json` in the publish output. It is never
placed under `wwwroot` or exposed as static content. Deployments can still override the path or use
Application Default Credentials/workload identity.

## Local Docker Compose stack

The root `docker-compose.yml` runs SQL Server, the API, and the Blazor portal as separate
containers. This is a local development environment with automatic migrations against its own
`OperationsSystem` database, demo data disabled, and SMTP/Android push disabled. It does not use
host user-secrets or local appsettings overrides. Existing IIS deployment remains supported.

1. Copy `.env.example` to `.env` and replace the password/signing-key placeholders with strong
   generated values (JWT key at least 32 characters). `.env` is ignored by Git. The initial local
   setup may already have generated this file; preserve its values when reusing existing volumes.
2. Provision the ignored `src/Host/OperationsSystem.Api/App_Data/firebase-admin.json` required
   by the existing API publish target. Docker supplies it as a BuildKit build secret and removes
   the published copy before creating the runtime image. It is excluded from the build context.
   FCM is disabled in this stack; do not enable it without mounting runtime credentials separately.
3. Run `docker compose up -d --build` from the repository root. The first build downloads the
   .NET images and NuGet packages. SQL must pass its health check before the API applies migrations;
   API readiness must pass before the portal starts.
4. Open the portal at `http://localhost:5080`; API readiness is at
   `http://localhost:5212/health/ready`. SQL is exposed at `localhost:1433`. All host ports bind
   only to loopback. Sign in with `ADMIN_EMAIL` and `ADMIN_PASSWORD` from your local `.env`;
   the existing administrator MFA requirements still apply.

Use `docker compose ps` for status and `docker compose logs api` for API startup diagnostics.
`docker compose down` stops/removes the containers while retaining SQL data, uploaded files,
API Data Protection keys, and portal Data Protection keys in named volumes. Do not use
`docker compose down -v` unless intentionally deleting this local environment's data and keys.
Changing `ADMIN_PASSWORD` does not reset an already seeded administrator; changing the SQL
password requires updating the database login as well as `.env` when retaining its volume.

SQL Server uses `linux/amd64`, including emulation on Apple Silicon. The API and portal use
native .NET 10 images. This local HTTP configuration is not a production deployment recipe;
production still requires the configuration, migrations, TLS, and backup steps in this runbook.

### Concurrent local environments

Main/release is the stable baseline. The earlier stack on this machine is **HandOver** and
remains preserved pending a later explicit removal. The environments use
separate Compose projects, API/portal images, SQL data, file storage, key volumes, and networks:

| Environment | Compose project | Portal | API readiness | SQL | Database |
|---|---|---|---|---|---|
| HandOver (retained; removal pending) | `operationsystem` | [localhost:5080](http://localhost:5080) | [localhost:5212/health/ready](http://localhost:5212/health/ready) | `127.0.0.1:1433` | `OperationsSystemHandOver` |
| Operation module baseline | `operationssystem-operation-module` | [127.0.0.1:5081](http://127.0.0.1:5081) | [127.0.0.1:5213/health/ready](http://127.0.0.1:5213/health/ready) | `127.0.0.1:1434` | `OperationsSystemOperationModuleMain` |
| Main / release | `operationssystem-main` | [main.localhost:5082](http://main.localhost:5082) | [127.0.0.1:5214/health/ready](http://127.0.0.1:5214/health/ready) | `127.0.0.1:1435` | `OperationsSystemMain` |

Use these exact portal hostnames to keep browser cookies separate; ports alone do not isolate
cookies. With all three stacks running in an 8 GB Docker VM, finish image builds before
starting SQL Testcontainers integration suites to avoid concurrent memory pressure. The generic root Compose instructions above describe the default deployment template.
Use the named helpers below to rebuild main or the Operation module environment. Keep the
existing HandOver containers on their existing images when working in another branch.
[Docker documents project names as the environment isolation boundary](https://docs.docker.com/compose/how-tos/project-name/).

### Main / release stack

`docker-compose.main.yml` builds `operationssystem-main-api:local` and
`operationssystem-main-portal:local` from main source, independently of the HandOver and
Operation module images. Copy `.env.main.example` to the ignored `.env.main` and provide
unique SQL/JWT secrets and the required bootstrap administrator settings. The API explicitly
binds every module connection string to local `OperationsSystemMain`.

Use the helper from the repository root:

```bash
./scripts/main-docker.sh up -d --build --wait
./scripts/main-docker.sh ps
./scripts/main-docker.sh down
```

With no arguments, the helper also runs `up -d --build --wait`. If the current checkout is
`main`, it builds that working source. From another branch, it builds a detached worktree at
`artifacts/docker-main-source`, refreshed to local `main` only when that worktree is clean.
It leaves the current branch and working changes in place and does not check out the `main`
branch in the additional worktree. Update the local `main` ref through the normal Git workflow
before selecting a newer main revision; the helper does not fetch remote changes. Images carry
the selected revision, release version, and main-branch labels.

For this environment's initial data, export a fresh copy of the remote SQL database using
[SqlPackage Export](https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage-export?view=sql-server-ver17)
and import it into the separate local `OperationsSystemMain` database before starting
the API. The export reads the remote source; imports and startup migrations target only the
local copy. Main's automatic work-order processing, flight reminders, outbox dispatch, email,
and Android push are disabled to prevent copied queued work from being delivered by these
background services. The helper's `down` retains its SQL, file, and key volumes.

A SQL/BACPAC export does not include remote uploaded files or the ASP.NET Core Data Protection
key ring. Those require separate source artifacts. Any separately copied locally available
storage and keys must be verified against the remote data; they do not establish that remote
attachments are available or that stored MFA secrets can be decrypted. Existing database users
retain their stored password hashes, and bootstrap settings do not reset their passwords.

### Isolated Operation module branch stack

Use `docker-compose.operation-module.yml` for `codex/Operation-Module-ImplementationI`.
The `scripts/operation-module.sh` wrapper always selects that standalone Compose file,
`.env.operation-module`, and project `operationssystem-operation-module`; it does not merge
the default Compose files. Its API/portal images, SQL data, uploaded files, API and portal
Data Protection key volumes, and default network are separate from the HandOver and
main/release stacks.

This branch is based on `main`. Its database target is a **new**
`OperationsSystemOperationModuleMain` database initialized from a main-compatible copy:

1. Copy `.env.operation-module.example` to the ignored `.env.operation-module` and provide
   unique branch SQL and JWT secrets. Keep these local credentials out of source control.
2. Before starting the branch API, take a SQL Server `COPY_ONLY` full backup of the intended
   main-compatible local source and restore it as `OperationsSystemOperationModuleMain` on
   the branch SQL service. Preserve the previous HandOver-derived
   `OperationsSystemOperationModule` database under its existing name; do not overwrite or
   reuse it as the input to this main-based migration chain.
   [A copy-only backup leaves the existing differential backup base unchanged](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/copy-only-backups-sql-server?view=sql-server-ver17).
3. Copy the matching source file-storage and API Data Protection keys into the branch's
   separate volumes, mounting source volumes read-only during the file copy. Verify that
   the files and key ring match the chosen database source; a SQL copy alone does not prove
   uploaded-file availability or MFA-secret decryption.
4. Start the branch API and portal after the restore and volume copies. Startup migrations
   apply only to the new branch database; see the [flight scheduling reset](#flight-scheduling-baseline).

The branch stack's outbox dispatch, flight reminders, and Android push are enabled for notification testing.
The API reads its Firebase credential from the read-only `firebase_admin` runtime secret; the
credential stays out of the image. Email remains disabled. Bootstrap administrator settings do
not reset an existing copied account. Main/release and HandOver run in separate
projects; rebuilding this branch does not rebuild their images or overwrite their databases.

From the repository root, use:

```bash
./scripts/operation-module.sh up -d --build --wait
./scripts/operation-module.sh ps
./scripts/operation-module.sh down
```

Running the wrapper with no arguments also runs `up -d --build --wait`. `down` retains the
branch database, files, and keys; `down -v` deletes those branch volumes. The wrapper checks
the current branch before `up`, `build`, `run`, or `create`.

Open the branch portal at `http://127.0.0.1:5081`, with API readiness at
`http://127.0.0.1:5213/health/ready` and SQL at `127.0.0.1:1434`. Keep using
`http://localhost:5080` for the HandOver portal: the different hostnames separate browser cookies;
ports alone do not. Commit or stash working changes before switching branches. The containers
have no source bind mounts, so a checkout does not change either running application's image;
rebuild/start this branch through its wrapper when its code changes.

### Launch from Visual Studio

With Visual Studio Container Development Tools installed and Docker Desktop using Linux
containers, reload `OperationsSystem.slnx`. Select the **Docker** solution configuration, then
set **Docker > docker-compose** as the startup project (or select it in the startup-project
dropdown). Choose the **Docker Compose** launch profile and press F5 or Ctrl+F5.

The profile starts SQL Server, the API, and the portal without attaching debuggers, then opens
`http://localhost:5080`. It uses the same Compose file, `.env`, project name, and volumes as the
local CLI stack. Regular mode builds the complete images inside Docker, preserving the shared
Dockerfile targets and build-secret handling. `docker-compose.vs.release.yml` preserves the
API/portal stage selection after Visual Studio's generated overrides. Dependency-aware startup
honors SQL/API health checks. Provision the `.env` and Firebase publish credential described above on the Windows
machine as well; ignored files are not transferred through Git.

The Compose project participates in builds only in the **Docker** solution configuration.
Normal **Debug** and **Release** builds continue to work with the .NET CLI without the
Visual Studio-only Docker SDK. The Docker configuration maps the C# projects to Debug;
the current Dockerfile publishes the container applications in Release.

## IIS portal startup

Copying a new release over an existing IIS folder does not remove files absent from the new
release. An obsolete `wwwroot/OperationsSystem.Blazor.modules.json` from a development build
can therefore retain a Hot Reload initializer whose JavaScript no longer exists. Fresh browsers
then remain on the loading screen with a failed module import, while an older session may work.

The portal publish target now includes an empty initializer manifest when there are no current
initializers, so folder replacements overwrite that obsolete list. Keep this file in deployment
packages. If actual initializers are added later, the SDK-generated list is preserved. After copying
the portal files, restart its IIS application pool so startup options are read again.

Verify the deployed `/_blazor/initializers` endpoint and startup in a fresh browser session.
The current release's initializer list is `[]`; any future listed module must return a successful
JavaScript response. A successful root HTML request alone does not verify interactive startup.

Include Safari in the startup check. A fresh session without a refresh cookie receives an expected
401 and must reach Sign in. JavaScript interop appends a browser-specific stack to API errors;
the portal transport parses only the JSON envelope so Safari's stack cannot interrupt authorization.

## Data Protection

MFA secrets and queued email bodies and attachments are encrypted with ASP.NET Core Data Protection. In production,
persist the key ring to a durable, access-controlled location (e.g. a mounted volume or a key vault)
so keys survive restarts and are shared across instances. With ephemeral keys, undelivered encrypted
emails and stored MFA secrets become unreadable after a restart.

## Migrations

Apply database migrations in dependency order: **Audit -> Identity -> MasterData -> Operations -> Notifications**. For local
development, `Database:ApplyMigrationsOnStartup` may be enabled to apply them automatically. For
production and production-like remote testing, keep it disabled, use reviewed SQL scripts, and take a
backup first.

### Flight scheduling baseline

The current Operations module is scheduling-only; see [Operations Module Foundation](../modules/operations-foundation.md). This branch is based on `main`. `Operations_FlightSchedulingBaseline` preserves flights and the existing direct planned-service/staff tables in place, and removes retired work-order/workflow tables and flight columns. It changes only the Operations schema. Keep the prior main migrations for new and existing main-compatible database upgrade paths; HandOver migrations are not prerequisites.

This is a destructive workflow-data reset. Back up any existing database and review the migration SQL before deployment. Its rollback cannot recover removed work-order/history data; restore the database backup with the earlier application instead. Development startup migrations are disabled in this branch so launching the API does not apply the reset automatically. Do not enable automatic migrations against a shared or remote database. Existing Android operational clients are incompatible with the reset API. Blob storage is left intact and requires a separate retention decision.

`Operations_RestoreFlightNotifications` adds the current durable reminder schedule table after the scheduling baseline. Apply it before enabling reminder scanning; it preserves existing flights and rosters. See [user notifications](../modules/operations-foundation.md#user-notifications) for milestone and cancellation semantics. The Operation module Compose stack enables both `Messaging:OutboxDispatchEnabled` and `Operations:FlightReminders:Enabled` for testing against its migrated local copy; the main/release stack keeps background processing disabled.

### 2026-10 legacy system IDs

Apply `20261005005402_MasterData_LegacySystemIds` before deploying the v1.0.12 API and
Portal. The release includes an idempotent incremental SQL script for installations already
on v1.0.11. It adds nullable `LegacySystemId` columns to Services, Tools, Materials,
General Supports, Customers, Stations, Manpower Types, Operation Types, Aircraft Types,
and Staff Members. Existing records retain null IDs until an administrator configures them.

The optional field accepts up to 200 characters and preserves case and leading zeros.
Whitespace is trimmed; clearing the portal field removes the mapping. Older clients that
omit the field preserve any existing mapping. Protected system records allow mapping-only
edits while their names, classification, and lifecycle protections remain in force.

The current Operations baseline does not expose the former flight/billing exports, submission emails, RTR workflows, or operational ATA task selection. MasterData catalog administration and historical migration files remain available independently. Earlier rollout instructions for retired Operations features are superseded by the scheduling baseline above.

## SQL performance settings

- Keep SQL Server connection pooling enabled. Do not use `Pooling=False` for remote or production-like databases; each EF command would pay for a new physical SQL connection.
- Use bounded connection and command timeouts so slow or blocked SQL is visible instead of hanging indefinitely.
- Keep `Identity:DemoData:Enabled=false` when pointing at real shared data.
- For smoke testing startup only, `Messaging:OutboxDispatchEnabled=false` prevents background outbox polling from blocking or masking the request path.

## Health, readiness, and observability

- `GET /health/live` — process liveness (no dependency checks).
- `GET /health/ready` — readiness; verifies the Identity, MasterData, Operations, Notifications, and Audit databases are reachable. Wire this to the orchestrator's readiness probe.
- Every response carries an `X-Correlation-ID` header (echoed from the request when supplied); logs are enriched with `CorrelationId`.
- **OpenTelemetry** (packages pinned at **1.15.3**, patched for CVE-2026-40894): ASP.NET Core + HTTP client tracing and metrics are enabled by default. Configure `OpenTelemetry:OtlpEndpoint` or standard `OTEL_EXPORTER_OTLP_ENDPOINT` to export to a collector. Health endpoints are excluded from trace instrumentation.
- The transactional outbox retries failed integration events; notification transport fan-out records `DeliveredAtUtc` only after every transport attempt succeeds, so transient SignalR/FCM failures retry with the stable notification id. After `OutboxProcessor.MaxAttempts` (10) a message is **dead-lettered** (logged at Critical, left in the outbox with its `Error` for inspection) and no longer retried.

## Backup / restore

- Back up the SQL database(s) and the Data Protection key ring together; a restore without matching keys cannot decrypt MFA secrets or queued emails.
- Audit trails are append-only and retained indefinitely; include the `audit` schema in backups.
- Customer logo storage (file storage root) should be backed up alongside the database.
- Include the `notifications` schema in database backups; inbox/history is authoritative when live transports are unavailable.

## Security checklist

- HTTPS only; secure/SameSite cookies for the refresh-token cookie behind the reverse proxy.
- Rate limiting enabled on anonymous auth endpoints.
- No default/fallback admin password; bootstrap admin activates via emailed invitation.
- Mandatory TOTP MFA for System Administrators (enrolled during activation).
- Firebase service-account credentials are supplied only through workload identity, a secrets store, or a protected mounted file; rotate them without rebuilding the application.
