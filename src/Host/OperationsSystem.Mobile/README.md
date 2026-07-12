# NAGS Operations — Mobile

Native Android client for Operations System v1.0.0. A rewrite of the legacy app that keeps its
proven offline-first engine and screens while consuming the new backend's flight and work-order
shape and business rules.

## What ships

* **Sign-in** — email/password with optional TOTP MFA. Bearer JWT + refresh token exchanged in the
  JSON body (`/api/v1/identity/auth/mobile/*`), persisted across launches with automatic refresh.
* **Local-first cache** — Room mirrors ten server tables (services, tools, materials, general
  supports, customers, aircraft types, station staff, my flights, Per Landing flights, Ad Hoc
  flights). Every screen reads from Room; only the sync coordinator writes the synced tables.
* **Real-time sync** — SignalR channel on `/hubs/mobile-sync` applies `change` envelopes
  (upsert / delete / refresh per logical table); a REST catch-up (`GET /api/v1/mobile/sync/changes`)
  reconciles after every reconnect, and a 5-minute foreground poll is the safety net.
* **Offline writes (outbox)** — work-order create/update, ad-hoc scratch create, return-to-ramp,
  and flight cancellation queue locally (attachments on disk) and drain FIFO with exponential
  backoff when connectivity returns. Every mutation carries a `clientMutationId` so server retries
  are idempotent; the SignalR echo (`originMutationId`) clears the optimistic chip.
* **Screens** — My Flights / Per Landing / Ad Hoc tabs, create/update work order (planned services
  seed the form as service lines to complete or remove — never Per Landing), return-to-ramp,
  invite teammates (online-only), cancel flight (time + reason), local drafts, and the Sync Center
  diagnostics screen.

Deferred: FCM push notifications (arrives with the backend Notifications module).

## Business rules mirrored from the backend

* Planned services are copied into the create-work-order form; the user completes each line's
  performer or removes it. Per Landing is a flight designation, never a performable service line.
* One active work order per user per flight; approval locks the work order and settles the flight.
* Service lines are clear-and-rebuild on update; tasks keep their stable server ids so uploaded
  attachments survive edits.
* Statuses are the server enum names: flights `Scheduled/InProgress/Completed/Canceled/Merged`,
  work orders `Submitted/Returned/Approved/Merged` (drafts are local-only).

## Module layout

```
app/src/main/java/com/nags/operations/
  AppGraph.kt                       — hand-rolled DI: TokenStore, Db, repos, sync, outbox, realtime
  MainActivity.kt                   — single Compose activity, hosts the NavHost
  data/api/                         — Ktor HTTP: AuthApi (mobile auth), MobileApi (/api/v1/mobile)
  data/db/                          — Room database (v11), entities, DAOs, converters
  data/outbox/                      — offline write queue + worker (clientMutationId idempotency)
  data/realtime/                    — SignalR channel + change envelope
  data/repo/                        — read-only Flow repositories over Room
  data/sync/                        — SyncCoordinator (single writer), SyncScheduler, sync tables
  ui/                               — Compose screens, ViewModels, components
```

## Local development

Point the app at a local API by adding to `local.properties`:

```
operations.api.base.url=http://10.0.2.2:5119
```

(`10.0.2.2` is the Android emulator's loopback alias for the host machine.)

Build: `./gradlew :app:assembleDebug`
