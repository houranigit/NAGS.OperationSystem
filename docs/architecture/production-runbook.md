# Production Configuration Runbook

This runbook lists the configuration and operational steps required to deploy the Operations System
backend (Identity + MasterData + Audit) to a production environment. The application validates the
security-critical settings below at startup and refuses to boot when they are missing or weak.

## Required configuration

Provide via environment variables or a secrets store (never in source control):

| Setting | Notes |
|---|---|
| `OpenTelemetry:Enabled` | Defaults to `true`. Set `false` to disable tracing/metrics export wiring. |
| `OpenTelemetry:ServiceName` | Service name on exported spans/metrics (default `operations-system-api`). |
| `OpenTelemetry:OtlpEndpoint` | Optional OTLP endpoint URL. When unset, the SDK honors standard `OTEL_EXPORTER_OTLP_*` environment variables. |
| `ConnectionStrings:Default` (or per-module `Identity`/`MasterData`/`Audit`) | SQL Server connection string. |
| `Database:ApplyMigrationsOnStartup` | Set `false` for production and production-like remote testing. Apply reviewed migrations separately. |
| `Messaging:OutboxDispatchEnabled` | Set `false` for smoke tests where background outbox polling should not touch the remote database. Keep enabled in normal deployments. |
| `Identity:Jwt:SigningKey` | >= 32 chars of entropy. Startup fails otherwise. |
| `Identity:Jwt:Issuer`, `Identity:Jwt:Audience` | Required. |
| `Identity:Admin:Email`, `Identity:Admin:DisplayName` | Bootstrap administrator. **Leave `Identity:Admin:Password` unset in production** — the admin is then created as an emailed invitation (no default password). |
| `EmailSettings:*` with `EnableEmailNotifications=true` | SMTP host/port/credentials/from. Invitations, password reset, email verification, and MFA flows depend on durable email delivery. |
| `Security:RateLimit:AnonymousAuthPermitLimit` / `...WindowSeconds` | Defaults 10/60. Tune per environment. |
| `FileStorage:RootPath` | Persistent volume for customer logos (object/file storage, not the served path). |
| Data Protection key ring | Persisted to a shared, backed-up location (see below). Encrypts MFA secrets and durable email bodies. |

## Data Protection

MFA secrets and queued email bodies are encrypted with ASP.NET Core Data Protection. In production,
persist the key ring to a durable, access-controlled location (e.g. a mounted volume or a key vault)
so keys survive restarts and are shared across instances. With ephemeral keys, undelivered encrypted
emails and stored MFA secrets become unreadable after a restart.

## Migrations

Apply database migrations in dependency order: **Audit -> Identity -> MasterData**. For local
development, `Database:ApplyMigrationsOnStartup` may be enabled to apply them automatically. For
production and production-like remote testing, keep it disabled, use reviewed SQL scripts, and take a
backup first.

## SQL performance settings

- Keep SQL Server connection pooling enabled. Do not use `Pooling=False` for remote or production-like databases; each EF command would pay for a new physical SQL connection.
- Use bounded connection and command timeouts so slow or blocked SQL is visible instead of hanging indefinitely.
- Keep `Identity:DemoData:Enabled=false` when pointing at real shared data.
- For smoke testing startup only, `Messaging:OutboxDispatchEnabled=false` prevents background outbox polling from blocking or masking the request path.

## Health, readiness, and observability

- `GET /health/live` — process liveness (no dependency checks).
- `GET /health/ready` — readiness; verifies the Identity, MasterData, and Audit databases are reachable. Wire this to the orchestrator's readiness probe.
- Every response carries an `X-Correlation-ID` header (echoed from the request when supplied); logs are enriched with `CorrelationId`.
- **OpenTelemetry** (packages pinned at **1.15.3**, patched for CVE-2026-40894): ASP.NET Core + HTTP client tracing and metrics are enabled by default. Configure `OpenTelemetry:OtlpEndpoint` or standard `OTEL_EXPORTER_OTLP_ENDPOINT` to export to a collector. Health endpoints are excluded from trace instrumentation.
- The transactional outbox retries failed integration events; after `OutboxProcessor.MaxAttempts` (10) a message is **dead-lettered** (logged at Critical, left in the outbox with its `Error` for inspection) and no longer retried.

## Backup / restore

- Back up the SQL database(s) and the Data Protection key ring together; a restore without matching keys cannot decrypt MFA secrets or queued emails.
- Audit trails are append-only and retained indefinitely; include the `audit` schema in backups.
- Customer logo storage (file storage root) should be backed up alongside the database.

## Security checklist

- HTTPS only; secure/SameSite cookies for the refresh-token cookie behind the reverse proxy.
- Rate limiting enabled on anonymous auth endpoints.
- No default/fallback admin password; bootstrap admin activates via emailed invitation.
- Mandatory TOTP MFA for System Administrators (enrolled during activation).
