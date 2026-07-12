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
  backoff when connectivity returns. A unique, network-constrained WorkManager job persists the
  wake-up across process death and device reboot, while a shared drain lock prevents the foreground
  and background uploaders from submitting the same row. Every mutation carries a
  `clientMutationId` so server retries are idempotent.
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
  data/outbox/                      — durable queue + foreground/WorkManager delivery
  data/realtime/                    — SignalR channel + change envelope
  data/repo/                        — read-only Flow repositories over Room
  data/sync/                        — SyncCoordinator (single writer), SyncScheduler, sync tables
  ui/                               — Compose screens, ViewModels, components
```

## Build prerequisites

The project is pinned to Gradle 8.9, Android Gradle Plugin 8.7.3, and JDK 17. Run Gradle with a
JDK 17 `JAVA_HOME` instead of relying on an unverified machine default. On macOS:

```sh
export JAVA_HOME=$(/usr/libexec/java_home -v 17)
```

## Switching API environments (Dev ↔ Production)

The mobile app reads `BuildConfig.API_BASE_URL`. That value is set at **build time**, not at runtime.

| Build | Where to change the URL | Current production value |
|---|---|---|
| **Debug** (Android Studio Run / `assembleDebug`) | `local.properties` → `operations.api.debug.base.url` | `http://operation.nags-ksa.com:5211` |
| **Release** (`assembleRelease`) | `gradle.properties` → `operations.api.release.base.url` (or env `OPERATIONS_API_RELEASE_BASE_URL`) | `http://operation.nags-ksa.com:5211` |

### Debug → local emulator API

In `local.properties`:

```properties
operations.api.debug.base.url=http://10.0.2.2:5211
```

`10.0.2.2` is the Android emulator’s host-loopback alias.

### Debug → production API

In `local.properties`:

```properties
operations.api.debug.base.url=http://operation.nags-ksa.com:5211
```

If DNS is not reachable from the phone/emulator, use the server IP instead:

```properties
operations.api.debug.base.url=http://80.209.234.61:5211
```

After changing `local.properties`, do a **Sync Gradle** / rebuild (clean install if the old URL was cached).

### Release → production (default)

`gradle.properties` already contains:

```properties
operations.api.release.base.url=http://operation.nags-ksa.com:5211
```

Override for one build without editing the file:

```sh
./gradlew :app:assembleRelease -Poperations.api.release.base.url=http://80.209.234.61:5211
```

Verified Debug checks:

```sh
./gradlew clean :app:testDebugUnitTest :app:lintDebug :app:assembleDebug
```

## Release configuration

Release never reads `local.properties`. Supply the production API URL with one of:

- `gradle.properties` / Gradle property `operations.api.release.base.url`
- Environment variable `OPERATIONS_API_RELEASE_BASE_URL`

The value must be an absolute `http`/`https` URL with a non-loopback host and no embedded credentials,
query string, or fragment. Release compilation and packaging fail when it is absent or invalid.
For example:

```sh
export OPERATIONS_API_RELEASE_BASE_URL=http://operation.nags-ksa.com:5211
./gradlew :app:lintRelease :app:assembleRelease
```

`assembleRelease` may produce an unsigned APK for verification. A distributable App Bundle must be
signed. Configure all four values below through CI secrets, environment variables, or a user-level
`~/.gradle/gradle.properties` file. Do not put them in this repository or pass passwords on a shared
shell command line.

| Gradle property | Environment variable |
|---|---|
| `operations.android.signing.store.file` | `OPERATIONS_ANDROID_SIGNING_STORE_FILE` |
| `operations.android.signing.store.password` | `OPERATIONS_ANDROID_SIGNING_STORE_PASSWORD` |
| `operations.android.signing.key.alias` | `OPERATIONS_ANDROID_SIGNING_KEY_ALIAS` |
| `operations.android.signing.key.password` | `OPERATIONS_ANDROID_SIGNING_KEY_PASSWORD` |

The store file may be absolute or relative to the mobile project root. `bundleRelease` fails before
distribution when any signing value is absent or the keystore does not exist.

Verified pre-release checks and signed bundle command:

```sh
./gradlew clean \
  :app:testDebugUnitTest \
  :app:lintDebug \
  :app:lintRelease \
  :app:assembleDebug \
  :app:assembleRelease

# Requires the four signing values above.
./gradlew :app:bundleRelease
```

R8/resource shrinking remains disabled until native regression tests and keep rules cover Room,
Kotlin serialization, Ktor, and SignalR. Sensitive auth state, the Room cache, and pending outbox
attachments are excluded from backup/device transfer; application backup is disabled as an
additional safeguard.
