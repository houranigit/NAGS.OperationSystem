# NAGS Operations — Android

The native Kotlin/Compose client supports the current [flight baseline](../../../docs/modules/operations-foundation.md): view flights, open basic planning details, and invite employees. Flight creation, single/batch scheduling, and planning edits remain in the web portal.

## Current journey

Sign in, browse the paged Flights list, search, refresh, and open a flight's details. Dates and times are displayed in the device time zone. Details show customer, station, operation type, scheduled arrival/departure, optional aircraft type, planned services, and assigned employees.

An account with `operations.flights.invite` can invite active, unassigned employees at the flight's station from the details sheet. Invitations are online, add only to the existing roster, and require its current rowversion. They are unavailable for Per Landing flights. A stale response retains the selection for review against refreshed options/version; it does not silently resend the request.

The server enforces the same account/station scope as the portal. Administrators and Viewers can read within their granted scope; station staff see assigned flights and station-wide Per Landing flights, with wider station visibility when separately granted. Viewers cannot invite. A successful sign-in alone does not grant flight access.

Flight reads and invitations require a server connection. The flight list is held in memory, with no offline flight cache or queued flight mutations. Work orders, status actions, drafts, scratch creation, return-to-ramp, flight printing, and Operations sync are outside this client baseline.

## Identity and Notifications

Existing email/password sign-in, enrolled TOTP MFA, secure bearer/refresh-token storage, session refresh, basic profile, and sign-out remain. Native authentication uses `/api/v1/identity/auth/mobile/*`; profile uses `/api/v1/identity/me`.

The existing bilingual notification inbox, read/archive actions, unread count, and configured FCM registration remain. Flight links open current basic details through the scoped flight API; schedule-level links open the list. The server queues the retained employee-invitation notification for linked recipients after an accepted invitation. Notification transport and device registration are separate from flight reads.

Notifications use Room `NotificationsDatabase` version 1 in `operations-baseline.db`. The previous `operations.db` and its dormant drafts/queued work remain unopened. Startup cancels the old WorkManager chain `operations-work-order-outbox` and tag `operations-outbox`; the retained legacy worker entry point performs no upload. This baseline does not replay or convert the old queue.

## API and code ownership

| Area | Current implementation |
|---|---|
| Flight HTTP client | `data/api/OperationsApi.kt` |
| Online flight repository | `data/repo/FlightsRepository.kt` |
| Flight models | `data/FlightModels.kt` |
| Identity | Existing `AuthApi`, `AuthRepository`, and `TokenStore` |
| Notification cache | `data/db/NotificationsDatabase.kt` |
| Process/session dependencies | `AppGraph.kt` |
| Compose navigation and screens | `ui/` |

Flight calls use the existing shared API:

- `GET /api/v1/operations/flights?page=1&pageSize=20&search=...`
- `GET /api/v1/operations/flights/{id}`
- `GET /api/v1/operations/flights/{id}/invitation-options`
- `POST /api/v1/operations/flights/{id}/invite` with `{ "staffMemberIds": [...] }` and `If-Match`.

There are no calls to the retired `/api/v1/mobile` Operations BFF or Operations realtime-sync hub.

## Local development

Use JDK 17 with the pinned Gradle 8.9 / Android Gradle Plugin 8.7.3 toolchain. The API URL is selected at build time through `BuildConfig.API_BASE_URL`.

For the current branch's Docker API, set the ignored `local.properties` entry:

```properties
operations.api.debug.base.url=http://10.0.2.2:5213
```

`10.0.2.2` is the Android emulator's host-loopback alias. A physical device needs a reachable address for the same branch API. Changing the property requires rebuilding the app.

Run debug checks from this directory, explicitly selecting that endpoint when desired:

```sh
export JAVA_HOME=$(/usr/libexec/java_home -v 17)
./gradlew :app:testDebugUnitTest :app:lintDebug :app:assembleDebug \
  -Poperations.api.debug.base.url=http://10.0.2.2:5213
```

These are verification commands, not a claim that a particular device journey has been observed. Report build/unit/lint results separately from emulator checks.

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
