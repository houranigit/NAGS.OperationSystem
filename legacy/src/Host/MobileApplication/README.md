# NAGS Operations — Mobile

Clean rewrite of the operations Android client.

## What ships today

* **Sign-in** — username/password, JWT pair persisted across launches, bearer auth + automatic refresh.
* **Branded home shell** — `Hello, {employee}` header with avatar, logout button, and an entry to the Sync Center.
* **Local-first cache** — Room database mirrors eight server-of-record tables (services, tools, materials, general supports, customers, station employees, my flights, AOG flights). Every screen reads from Room; nothing reads the network directly.
* **Near-real-time sync** — on sign-in, on network restore, and every 30 s while the app is foregrounded the [`SyncCoordinator`](app/src/main/java/com/nags/operations/data/sync/SyncCoordinator.kt) fans out parallel GETs to `/api/mobile/v2/*` and atomically replaces each cache table.
* **Sync Center** — a diagnostics screen showing per-table row counts, last sync timestamps, last sync durations, and any sticky errors, plus a "Refresh now" button.

Next slices (intentionally not in this drop) — SignalR push for closed-app updates, write-side outbox for mutations, FCM notifications.

## Module layout

```
app/src/main/java/com/nags/operations/
  AppGraph.kt                       — hand-rolled DI: TokenStore, Db, repos, sync, scheduler
  MainActivity.kt                   — single Compose activity, hosts the NavHost
  data/
    api/HttpClientFactory.kt        — Ktor client + bearer auth + refresh hook
    api/AuthApi.kt                  — POST /api/identity/auth/login + /refresh
    api/MobileApi.kt                — GET /api/mobile/v2/{me,catalogs,employees,flights}
    repo/AuthRepository.kt          — login() persists the JWT pair via TokenStore
    repo/CatalogsRepository.kt      — Flow<List<…>> reads for the 5 catalog tables
    repo/EmployeesRepository.kt     — Flow<List<EmployeeEntity>>
    repo/FlightsRepository.kt       — Flow<List<…>> for my-flights and AOG-flights
    AuthModels.kt + MobileModels.kt — wire DTOs (kotlinx.serialization)
    ApiException.kt + ApiProblem.kt — error wrapping + user-friendly messages
    TokenStore.kt                   — DataStore-backed JWT + identity cache
    db/
      AppDatabase.kt                — Room database (version 1, exports schema)
      entities/*Entity.kt           — one entity per cached table + SyncStateEntity
      dao/*Dao.kt                   — Flow reads + transactional replaceAll writes
    network/NetworkMonitor.kt       — coarse "are we online?" StateFlow
    sync/SyncTable.kt               — enum of every synced table
    sync/SyncCoordinator.kt         — parallel fan-out + transactional cache write
    sync/SyncScheduler.kt           — sign-in / online-flip / 30s periodic trigger
  ui/
    OperationsApp.kt                — NavHost: login -> home -> sync-center
    AppViewModelFactory.kt          — resolves ViewModels from AppGraph
    theme/                          — Material3 color scheme (BrandRed) + Typography
    components/AppHeader.kt         — red-gradient greeting header with logout button
    components/Brand.kt             — NagsLogo() used on the login masthead
    components/Feedback.kt          — InlineErrorBanner() shown on failed login
    login/                          — branded sign-in screen + ViewModel
    home/HomeScreen.kt              — placeholder body + Sync Center entry card
    sync/SyncCenterScreen.kt        — per-table diagnostics + Refresh now button
    sync/SyncCenterViewModel.kt     — live counts + sync_state + connectivity
```

## How sync works

1. `AppGraph` constructs a single `SyncCoordinator` and a `SyncScheduler` at process start.
2. The scheduler watches `(TokenStore.accessTokenFlow, NetworkMonitor.isOnline)`. When both are true it triggers an immediate sync and then loops every 30 s.
3. `SyncCoordinator.refreshAll()` launches all five GETs in parallel. Each table's "delete-then-insert" runs inside a Room `@Transaction` so readers never observe an empty list mid-sync.
4. Per-table outcomes are recorded in the `sync_state` table; the Sync Center reads from there.
5. Logout calls `coordinator.clearForLogout()` *before* clearing the JWT so an in-flight sync can't repopulate the cache after the token is gone.

## Build

```bash
./gradlew :app:assembleDebug
```

## API base URL

Defaults to the deployed host. Override per-machine by adding
`operations.api.base.url=http://10.0.2.2:5119` to `local.properties`
(the value `10.0.2.2` is the Android emulator's loopback to the host).
