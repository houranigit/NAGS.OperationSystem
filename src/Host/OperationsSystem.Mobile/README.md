# NAGS Operations — Android

The native Kotlin/Compose client supports the current [flight baseline](../../../docs/modules/operations-foundation.md): view flights, open basic planning details, invite employees, and synchronize shared MasterData catalogs. Flight creation, single/batch scheduling, and planning edits remain in the web portal.

## Current journey

Sign in, browse the virtualized Flights list, search, refresh, and open a flight's details. Scrolling near the end automatically loads the next page; a failed page can be retried in place. Compact cards keep the flight number, customer, aircraft, and operation on single lines, with N/A for a missing aircraft model. The card avatar shows the current customer logo, with the flight icon while the logo is missing, loading, or unavailable. The footer shows one local date or date range and times-only STA/STD values. Dates and times are displayed in the device time zone. Details show customer, station, operation type, scheduled arrival/departure, optional aircraft type, planned services, and assigned employees.

The floating search capsule stays above the navigation bar and keyboard. Focusing it animates the field inward to reveal a separate close button; the inner clear button keeps editing active, while close resets the query and dismisses the keyboard. The microphone opens the device's speech-input activity when available; recognized text uses the same flight search.

An account with `operations.flights.invite` can invite active, unassigned employees at the flight's station from the details sheet. Invitations are online, add only to the existing roster, and require its current rowversion. They are unavailable for Per Landing flights. A stale response retains the selection for review against refreshed options/version; it does not silently resend the request.

The server enforces the same account/station scope as the portal. Administrators and Viewers can read within their granted scope; station staff see assigned flights and station-wide Per Landing flights, with wider station visibility when separately granted. Viewers cannot invite. A successful sign-in alone does not grant flight access.

The flight list and previously opened basic details can be read from the current account's local cache when disconnected. Live reads remain the first choice while online. Downloaded customer logos are saved as private image files and reused across flights and app restarts. Customer synchronization and scoped flight refreshes check their versions; ordinary list refreshes and searches reuse unchanged images. Employee invitations require a live connection and a fresh detail/eligibility check; no flight mutation is queued. Work orders, status actions, drafts, scratch creation, return-to-ramp, and flight printing remain outside this client baseline.

## Flight synchronization

The **Flights** card in Sync Center reports the cached scoped register's count, last successful refresh, duration, and errors. Synchronization reads every page of the current `/api/v1/operations/flights` register using its existing visibility rules. Details are cached when opened online; full flight refreshes revalidate previously cached details and remove flights no longer returned or accessible. Flights never opened online require a connection to load their details. Offline search and paging use the last successful register snapshot, and the screen identifies cached data.

A successful schedule, planning edit, number change, roster change, or invitation addition publishes a best-effort flight refresh hint. Flight hints refresh flights only and refresh an open flight view; catalog hints remain catalog-only. Staff station/active changes and station active changes also invalidate flights because they can change the caller's visibility. Full sign-in/token, foreground, reconnect, periodic, and **Refresh now** checks cover both flights and the eight reference catalogs. A failed connection retains the last successful flight snapshot; an authorization denial evicts denied flight data. Offline access reflects the last successful authorization check and cannot observe server access changes until reconnecting. Account/server ownership and session-generation guards prevent another session's cache or late replies from being reused.

## Shared catalog synchronization

The app caches eight shared reference catalogs: customers, aircraft types, services, tools, materials, general supports, ATA chapters, and station staff. `SharedCatalogsApi` reads their existing `/api/v1/masterdata/{catalog}/options` endpoints through the same authenticated API client and configured branch URL as flight requests. These are active lookup records, not work-order forms or personalized performed-service allowances.

Each endpoint requires `masterdata.reference.view-options` or its catalog's `.view` permission. Flight viewing does not grant catalog access. Administrators, station staff, and Viewers use their configured grants; a Customer Contact with customer-view permission can receive only their own customer options. Synchronization does not broaden those scopes or add permissions. Station staff use the existing `/api/v1/masterdata/staff-members/options` route: station-staff accounts receive only active members of their own station, global readers retain their existing scope, and customer contacts have no staff access. The **Station staff** cache and status card are separate from the flight-specific invitation options, which remain live and enforce invitation eligibility.

Customer options include an opaque `logoVersion`; `none` means the customer has no logo, while a missing/null version is older or unknown metadata. The scoped flight register supplies `customerLogoVersion` for customers visible through Operations, including accounts without customer-catalog access. One account/API-scoped logo store reconciles these versions and saves images on demand under the app's private no-backup directory, outside the Room catalog payload. Unchanged versions reuse the existing file; a changed version replaces only that image after a successful download, and `none` removes only that customer's image. Failed downloads retain a previous good file for retry. Signing out, changing accounts, or losing flight access removes the affected saved images. The full scoped flight register governs visibility cleanup; a denied customer catalog does not remove logos still authorized through flights.

Open **Sync Center** from the sync-status button in the home header or the Sync Center action in **My profile**. The restored page shows network online/offline status, live-channel connection state, the last accepted catalog event, and each catalog's cached count, last successful refresh, measured duration, and current error or permission denial. **Refresh now** refreshes all eight catalogs and flights. Connection status is independent of cache freshness: a connected channel can be awaiting its first event, and a failed channel does not stop HTTP refreshes. The last accepted event remains visible while offline and clears when the session ends. Full refreshes also run after sign-in/token refresh, reconnecting to the network or catalog hub, returning to the app, and every five minutes while the app process is active and connected. A live catalog-change hint refreshes only the affected catalog; ordinary staff changes refresh the staff cache without changing other catalogs' sync metadata. Staff/station access changes also trigger flight revalidation. **Last sync** records the last successful fetch, rather than the last data change: another catalog's event leaves that timestamp unchanged, while a full refresh updates it for every successful fetch. Creating a record, changing active lookup fields, or activating/deactivating a record publishes a refresh hint after persistence succeeds. Deactivation therefore removes it from the next cached active-options snapshot. Customer logo replacements also publish customer hints, including for inactive customers; byte-identical uploads keep the existing file/version and publish no hint. Other changes outside the lookup payload, inactive-only edits, repeated state transitions, and rejected writes do not publish hints. Hints are best-effort; periodic and foreground refreshes also cover missed delivery. No background WorkManager catalog job is installed.

`CatalogDatabase` version 3 stores catalog snapshots and separate flight-register/detail tables keyed by API base URL and authenticated account in `shared-catalogs.db`, separately from the notification cache and unopened legacy database. Copied account IDs in the main, HandOver, and Operation Module APIs cannot expose another environment's cached catalog snapshot. The explicit version 1 → 2 migration preserves existing catalog snapshots and adds duration metadata; version 2 → 3 adds flight tables without replacing those snapshots. A network or parsing failure retains the last successful catalog snapshot and its timestamp. A `403` clears that catalog's cached records and records the denial; `401` clears the catalog cache. Signing out or changing accounts clears shared catalog snapshots. Catalog permissions do not grant flight access. Flight caching uses only the scoped Operations API; invitation eligibility and writes continue to require that API online.

## Identity and Notifications

Existing email/password sign-in, enrolled TOTP MFA, secure bearer/refresh-token storage, session refresh, basic profile, and sign-out remain. Native authentication uses `/api/v1/identity/auth/mobile/*`; profile uses `/api/v1/identity/me`.

The existing bilingual notification inbox, read/archive actions, unread count, and configured FCM registration remain. Flight links open current basic details through the scoped flight API; schedule-level links open the list. The server queues the retained employee-invitation notification for linked recipients after an accepted invitation. Notification transport and device registration are separate from flight reads.

Notifications use Room `NotificationsDatabase` version 1 in `operations-baseline.db`. The previous `operations.db` and its dormant drafts/queued work remain unopened. Startup cancels the old WorkManager chain `operations-work-order-outbox` and tag `operations-outbox`; the retained legacy worker entry point performs no upload. This baseline does not replay or convert the old queue.

## API and code ownership

| Area | Current implementation |
|---|---|
| Flight HTTP client | `data/api/OperationsApi.kt` |
| Online flight repository | `data/repo/FlightsRepository.kt` |
| Persistent customer logo store | `data/repo/CustomerLogoStore.kt` |
| Shared catalog HTTP client and cache | `data/api/SharedCatalogsApi.kt`, `data/repo/SharedCatalogsRepository.kt`, `data/db/CatalogDatabase.kt` |
| Flight models | `data/FlightModels.kt` |
| Identity | Existing `AuthApi`, `AuthRepository`, and `TokenStore` |
| Notification cache | `data/db/NotificationsDatabase.kt` |
| Process/session dependencies | `AppGraph.kt` |
| Compose navigation and screens | `ui/` |

Flight calls use the existing shared API:

- `GET /api/v1/operations/flights?page=1&pageSize=20&search=...`
- `GET /api/v1/operations/flights/{id}`
- `GET /api/v1/operations/flights/customer-logos/{customerId}?expectedVersion={logoVersion}` for customers on a currently visible flight; the response ETag identifies the returned version and `412` rejects a version mismatch
- `GET /api/v1/operations/flights/{id}/invitation-options`
- `POST /api/v1/operations/flights/{id}/invite` with `{ "staffMemberIds": [...] }` and `If-Match`.

Shared catalog calls use the `customers`, `aircraft-types`, `services`, `tools`, `materials`, `general-supports`, `ata-chapters`, and `staff-members` options routes under `/api/v1/masterdata`. Catalog change hints use the retained `/hubs/mobile-sync` hub. The flight cache uses the same scoped Operations routes above. There are no calls to the retired `/api/v1/mobile` Operations BFF, legacy flight-sync endpoints, or WO mutation APIs.

## Local development

Use JDK 17 with the pinned Gradle 9.8.2 / Android Gradle Plugin 9.5.0 toolchain, Android SDK platform 37, and SDK Build Tools 36.0.0. AGP uses built-in Kotlin with an explicit Kotlin Gradle plugin 2.4.21 classpath override; the Compose and serialization compiler plugins use the same version, and Room uses KSP 2.3.13. Do not apply the separate `kotlin-android` plugin. APK naming uses the public `androidComponents` API. The app compiles and targets Android 17 (API 37), with minimum API 26 retained. See the [AGP compatibility requirements](https://developer.android.com/build/releases/agp-9-5-0-release-notes) and [built-in Kotlin migration](https://developer.android.com/build/migrate-to-built-in-kotlin).

The dependency refresh uses Compose BOM 2026.09.00 / Material3 1.4.0, Room 2.8.5, DataStore 1.3.0, WorkManager 2.12.0, and Firebase BOM 35.1.0. The HTTP/realtime libraries use Ktor 3.6.1, kotlinx.serialization 1.12.0, kotlinx.coroutines 1.11.0, and SignalR 10.0.14. Existing Room schemas and explicit migrations remain unchanged. Validate Android 16/17 back navigation, edge-to-edge insets, large-screen resizing, and notification navigation on representative devices before release; a successful build or lint run alone does not establish runtime compatibility. The API URL is selected at build time through `BuildConfig.API_BASE_URL`.

For the current branch's Docker API, set the ignored `local.properties` entry:

```properties
operations.api.debug.base.url=http://10.0.2.2:5213
```

`10.0.2.2` is the Android emulator's host-loopback alias. A physical device needs a reachable address for the same branch API. Changing the property requires rebuilding the app. On Android 17, a configured private IP or `.local` backend prompts once for Nearby devices (local-network access). If denied, cached reads remain available; enable the permission in app Settings to reconnect. Public hosted API endpoints do not prompt. Use a private IP or `.local` hostname for a local backend; other DNS names resolving to LAN addresses are not detected by this startup prompt.

Run debug checks from this directory, explicitly selecting that endpoint when desired:

```sh
export JAVA_HOME=$(/usr/libexec/java_home -v 17)
./gradlew :app:testDebugUnitTest :app:lintDebug :app:assembleDebug \
  -Poperations.api.debug.base.url=http://10.0.2.2:5213
```

These are verification commands, not a claim that a particular device journey has been observed. Report build/unit/lint results separately from emulator checks.

### Run main and Operation Module together

The local debug apps have separate Android identities and API environments:

| Launcher name | Application ID | Emulator API |
|---|---|---|
| Operations Module | `com.nags.operations` | `http://10.0.2.2:5213` |
| Operations Main | `com.nags.operations.main` | `http://10.0.2.2:5214` |

The Operation Module debug build keeps its existing application ID, login, and
cache. Its debug-only launcher label distinguishes it from main; release naming
and identity remain unchanged. Each installed app has its own login and local
storage. Main requires a separate sign-in.

From the repository root, build the main app with:

```sh
./scripts/android-main.sh
```

The helper builds local `main` in the ignored detached checkout
`artifacts/android-main-source`. An external Gradle init script supplies only
the debug application-ID suffix and label; the existing API override targets
main's Docker API. Main's tracked source and the current checkout are unchanged.
The resulting APK is copied to `artifacts/android-apps/`.

Install that APK alongside the normal Operation Module debug APK using
`adb install -r /absolute/path/to/the.apk`, then open either app from the launcher
or Recent apps. Do not uninstall the Operation Module app to switch environments.
Both Docker APIs must be running. The main debug clone does not copy another
package's Firebase configuration; its local build uses the existing debug mode
without Firebase push registration.

## Release configuration

Release does not read `local.properties`. Supply a compatible baseline backend URL with Gradle property `operations.api.release.base.url` or environment variable `OPERATIONS_API_RELEASE_BASE_URL`. The build validates an absolute HTTP/HTTPS URL, rejects emulator/loopback hosts and embedded credentials, and rejects queries/fragments.

Provision the ignored Android Firebase client file `app/google-services.json` for application ID `com.nags.operations` before release builds. Debug/unit builds can run without it; device registration then remains disabled. Every release build validates this file. It is distinct from the backend Firebase Admin credential.

For distribution, provide all four signing values through CI/environment variables or user-level Gradle properties:

| Gradle property | Environment variable |
|---|---|
| `operations.android.signing.store.file` | `OPERATIONS_ANDROID_SIGNING_STORE_FILE` |
| `operations.android.signing.store.password` | `OPERATIONS_ANDROID_SIGNING_STORE_PASSWORD` |
| `operations.android.signing.key.alias` | `OPERATIONS_ANDROID_SIGNING_KEY_ALIAS` |
| `operations.android.signing.key.password` | `OPERATIONS_ANDROID_SIGNING_KEY_PASSWORD` |

`assembleRelease` can produce an unsigned verification APK; `bundleRelease` requires the complete signing configuration. Application backup/device transfer remains disabled, with auth state, databases, and legacy outbox files excluded by the existing backup rules.
