# NAGS Operations — Android

The native Kotlin/Compose client supports the current [flight baseline](../../../docs/modules/operations-foundation.md): view flights, open basic planning details, invite employees, and synchronize shared MasterData catalogs. Flight creation, single/batch scheduling, and planning edits remain in the web portal.

## Current journey

Sign in, browse the virtualized Flights list, refresh, and open a flight's details. Scrolling near the end automatically loads the next page; a failed page can be retried in place. Compact cards keep the flight number, customer, aircraft, and operation on single lines, with N/A for a missing aircraft model. The card avatar shows the current customer logo, with the flight icon while the logo is missing, loading, or unavailable. The footer shows one local date or date range and times-only STA/STD values. Dates and times are displayed in the device time zone. The Scheduled badge shares the flight-number row in both cards and details, leaving the full text width below it for the customer name. The compact white details sheet uses a tight Flight details toolbar with a small Share visual inside its 48dp target and the Per Landing designation in that toolbar when applicable. The flight number and customer remain prominent, followed by the same labelled aircraft/operation row as the cards. Its schedule section uses two open groups: the station name with a separate airport-code caption on the left, and STA–STD with its local date/range directly beneath on the right. Schedule shares the STA–STD label style and baseline. The station and time values share the same bold type, with the station limited to two lines and ellipsis. Narrow layouts and large text stack the two groups to keep both readable; the arrow stays centered between the time values. Scheduled Services precedes Assigned Employees. These icon-led rows place overlapping initials stacks on the right; each visible item has a different high-contrast tint that stays consistent in its expanded row. Expanding replaces the summary stack with readable names and employee IDs. Section headings can wrap to two lines when space is limited. Empty messages occupy the right-hand summary area. Per Landing details omit both sections, including their loading placeholders when the designation is known. Loading placeholders otherwise mirror the identity, metadata, schedule, and disclosure rows. Canceled and Operations actions follow the schedule and any visible disclosure rows. They remain side by side with equal widths: danger-red Canceled on the physical left and success-green Operations on the right, including right-to-left layouts. Loading placeholders follow the same two-column layout. The smaller Per Landing badge uses the same orange treatment in cards and details. Share remains inactive. Operations opens the UI-only Flight operations form; Canceled opens Flight cancellation. Both use the accepted light contact-inspired design: compact grouped fields with a 48dp minimum row height, bold labels, muted italic hints, darker right-aligned values and left-aligned Remarks. The compact flight identity uses three text lines matched to the logo height, with Scheduled at the right edge. Arrival contains ATA defaulted from STA in the device time zone, planned aircraft editable from the cached searchable/checkmark list, tail number, optional work reference and its own Remarks. Departure contains ATD defaulted from STD, a customer signature and separate Remarks. The signature pad keeps completed strokes temporarily in form state, supports drawing and clearing, and shows an editable ink preview; it does not create files, upload bytes or expose storage metadata. Cancellation contains a current-time date/time field and optional Remarks; it does not add a reason or status selector. Receiving/handling and creation attribution remain reserved for the signed-in user, with no employee dropdown. All date/time fields reuse the rounded, dimming, curved and fading year/month/day and 24-hour time wheels; the intrinsic groups remain compact, preserve the current-year±1 range, respond to native fling/snapping and flatten decorative curvature when system animations are disabled. There is no Done action. Temporary edits, separate remarks and completed signature strokes survive configuration recreation, and leaving either edited form asks for discard confirmation. Opening a date or aircraft/signature picker does not discard edits. Both Save controls remain disabled with no submission callback, backend writes or durable draft. Between Arrival and Departure, Services and Tasks use repeatable contact-style groups with green Add rows and red minus controls that reveal a sliding Delete action. Services are entered explicitly rather than copied from scheduled services. Each service has a cached service choice, followed by the customer reference, separate Start and optional End fields, employee work periods, remarks and temporary attachments. Only its chooser row slides to reveal a small inset Delete button; the remaining service fields use the full group width. Nested employee entries in both service and task editors also slide only their chooser row, with employee code, work periods and task employee remarks using the full group width. Each task has Major/Minor type, an optional cached ATA chapter, the same timing/reference/remarks fields, and its own employees, tools, materials, general support and attachments. Employee and resource occurrences have independent work periods; task occurrences also support remarks, while service employees have no notes field. Only end-before-start is flagged inline; no schedule bounds or invented quantity/billing fields are added. Options read the existing account-scoped shared catalog cache only while the Operations form is open. Attachments retain only a temporary system-picker URI and display name, with no copy, upload or durable URI grant. Repeat entries and nested values survive configuration recreation with the other temporary fields, and confirmed discard clears the whole form. Dismiss the details sheet by dragging it down, tapping outside, or using Back. Station is shown only within the schedule context; time-zone and flight ID fields are omitted. Cached-data notices and the existing invitation controls remain available.

The light home shell uses a compact white header with theme-maroon profile and AdHoc accents, a restrained greeting, and the existing Notifications entry. AdHoc remains inert. Header controls retain 48dp touch targets while their visible shapes and typography stay compact. Long greetings stay on one line with ellipsis within the available width. Four icon-only bottom items share an opaque white navigation capsule, with the active maroon icon on a subtle brand-tinted indicator that slides smoothly between the existing destinations; the destinations are Flights, Hand Over flights, Page 3, and Page 4; only Flights currently has page content. Profile retains access to sign-out and Sync Center. The separate bottom search button opens the same inline Flights search as the tab-toolbar action. Page selection and the Flights tab survive returning from profile, notifications, or search; a flight notification returns to My Flights before opening its details.

Hand Over flights is reserved for the future handover feature. Its planned tabs are hand-over flights for the user and all station hand-over flights. The page remains empty until that feature is implemented.

Flights owns its My Flights, Per Landing, and AdHoc tabs below the common header. Bold tab labels use the theme-maroon active color and share a bottom divider with the moving selection marker. A quiet upper divider separates the tabs from the greeting, while the bottom divider carries the marker. The first card has a 16dp top inset matching the side gutters; skeletons share that inset. Tab labels stay on one line and ellipsize within bounded widths; the list has no time-zone caption. Each tab requests its own server-filtered pages and count on first use, then retains its loaded pages and scroll position when switching by tap or swipe. Switching tabs alone does not repeat a loaded tab’s list request or show initial-loading skeletons, including for an empty tab. Refreshes and sync invalidation revalidate the loaded page range while retaining visible rows; station and access changes clear the affected tab state. Search results have their own page and scroll state, leaving the normal tab lists intact. My Flights contains assigned flights excluding Per Landing and AdHoc. Per Landing contains station Per Landing flights without requiring assignment. AdHoc contains flights with the fixed Ad Hoc operation type created by the signed-in user, without requiring assignment. As on `main`, AdHoc takes precedence for a legacy flight carrying both designations. Initial flight loading and next-page reads use light skeletons matching the existing card geometry, while refreshes retain visible cards. Automatic flight updates, resume checks, and stale-tab revalidation refresh silently. Only a manual pull gesture shows the refresh indicator; the tab bar and empty state have no refresh button.

The compact rounded toolbar search button expands across the tabs into a 36dp-high search pill with a separate Close control, aligned to the list gutters. Search, clear, and Close retain 48dp touch targets; the caret and selected search-location tab use the brand accent. Search filters the opened category using the existing flight-number/customer/ID query. A segmented My Flights/Per Landing/AdHoc strip sits above the keyboard and changes the search location while keeping the query; the bottom navigation is hidden while searching. The internal clear button restores the current tab's unfiltered rows while keeping search open. Close cancels pending search and restores normal cached pages and scroll position; first-use and invalidated data still load through the bounded mobile endpoint. The existing station and rolling-window limits apply to search results as well.

An account with `operations.flights.invite` can invite active, unassigned employees at the flight's station from the details sheet. Invitations are online, add only to the existing roster, and require its current rowversion. They are unavailable for Per Landing flights. A stale response retains the selection for review against refreshed options/version; it does not silently resend the request.

The mobile server requires an active station-linked staff account and `operations.flights.view`, following the staff-link rule on `main`. The station comes from the employee record; there is no station selector or caller-provided window. Administrator and Viewer accounts without that link cannot open the mobile feed. Station-wide viewing permission does not widen My Flights. Portal scheduling and register scopes retain their existing rules.

Every mobile flight read is limited to that station and the inclusive window from server now minus 12 hours through now plus 12 hours. A flight is included when STA–STD overlaps the window (`STA <= end` and `STD >= start`), including overnight or longer stays. The same eligibility applies to mobile details, customer-logo access, and invitation routes, including notification links.

The flight list and previously opened basic details can be read from the current account's local cache when disconnected. Live reads remain the first choice while online. Downloaded customer logos are saved as private image files and reused across flights and app restarts. Customer synchronization and scoped flight refreshes check their versions; ordinary list refreshes and searches reuse unchanged images. Employee invitations require a live connection and a fresh detail/eligibility check; no flight mutation is queued. Work orders, status actions, drafts, scratch creation, return-to-ramp, and flight printing remain outside this client baseline.

## Flight synchronization

The **Flights** card in Sync Center reports the cached scoped register's count, last successful refresh, duration, and errors. Synchronization reads only the eligible union of the three categories from `/api/v1/operations/flights/mobile`. It never downloads the unrestricted portal register. Each page includes its server station/window metadata; the server recomputes the window for each request. Details opened online are cached only when their flight is in the complete bounded snapshot; a live detail opened before the first successful sync is not an offline orphan. Full flight refreshes revalidate previously cached details and remove flights no longer returned or accessible. Flights never opened online require a connection to load their details. Offline flight paging filters the last successful bounded snapshot by the requested category and the current rolling window, and the screen identifies cached data. Visible cards and open details expire as the window moves, including while offline. Legacy unrestricted flight snapshots and orphan details are discarded before fallback; shared reference catalogs remain intact.

A successful schedule, planning edit, number change, roster change, or invitation addition publishes a best-effort flight refresh hint. Flight hints refresh flights only and refresh an open flight view; catalog hints remain catalog-only. Staff station/active changes and station active changes also invalidate flights because they can change the caller's visibility. Full sign-in/token, foreground, reconnect, periodic, and **Refresh now** checks cover both flights and the eight reference catalogs. A failed connection retains the last successful flight snapshot unless valid page metadata confirms a changed station: the previous station’s rows, details, and logo authorization are removed immediately, even if later pages fail. An authorization denial evicts denied flight data. Offline access reflects the last successful authorization check and cannot observe server access changes until reconnecting. Account/server ownership and session-generation guards prevent another session's cache or late replies from being reused.

## Shared catalog synchronization

The app caches eight shared reference catalogs: customers, aircraft types, services, tools, materials, general supports, ATA chapters, and station staff. `SharedCatalogsApi` reads their existing `/api/v1/masterdata/{catalog}/options` endpoints through the same authenticated API client and configured branch URL as flight requests. These are active lookup records, not work-order forms or personalized performed-service allowances.

Each endpoint requires `masterdata.reference.view-options` or its catalog's `.view` permission. Flight viewing does not grant catalog access. Administrators, station staff, and Viewers use their configured grants; a Customer Contact with customer-view permission can receive only their own customer options. Synchronization does not broaden those scopes or add permissions. Station staff use the existing `/api/v1/masterdata/staff-members/options` route: station-staff accounts receive only active members of their own station, global readers retain their existing scope, and customer contacts have no staff access. The **Station staff** cache and status card are separate from the flight-specific invitation options, which remain live and enforce invitation eligibility.

Customer options include an opaque `logoVersion`; `none` means the customer has no logo, while a missing/null version is older or unknown metadata. The scoped flight register supplies `customerLogoVersion` for customers visible through Operations, including accounts without customer-catalog access. One account/API-scoped logo store reconciles these versions and saves images on demand under the app's private no-backup directory, outside the Room catalog payload. Unchanged versions reuse the existing file; a changed version replaces only that image after a successful download, and `none` removes only that customer's image. Failed downloads retain a previous good file for retry. Signing out, changing accounts, or losing flight access removes the affected saved images. The full scoped flight register governs visibility cleanup; a denied customer catalog does not remove logos still authorized through flights.

Open **Sync Center** from the Sync Center action in **My profile**. The restored page shows network online/offline status, live-channel connection state, the last accepted catalog event, and each catalog's cached count, last successful refresh, measured duration, and current error or permission denial. **Refresh now** refreshes all eight catalogs and flights. Connection status is independent of cache freshness: a connected channel can be awaiting its first event, and a failed channel does not stop HTTP refreshes. The last accepted event remains visible while offline and clears when the session ends. Full refreshes also run after sign-in/token refresh, reconnecting to the network or catalog hub, returning to the app, and every five minutes while the app process is active and connected. A live catalog-change hint refreshes only the affected catalog; ordinary staff changes refresh the staff cache without changing other catalogs' sync metadata. Staff/station access changes also trigger flight revalidation. **Last sync** records the last successful fetch, rather than the last data change: another catalog's event leaves that timestamp unchanged, while a full refresh updates it for every successful fetch. Creating a record, changing active lookup fields, or activating/deactivating a record publishes a refresh hint after persistence succeeds. Deactivation therefore removes it from the next cached active-options snapshot. Customer logo replacements also publish customer hints, including for inactive customers; byte-identical uploads keep the existing file/version and publish no hint. Other changes outside the lookup payload, inactive-only edits, repeated state transitions, and rejected writes do not publish hints. Hints are best-effort; periodic and foreground refreshes also cover missed delivery. No background WorkManager catalog job is installed.

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

Flight calls use the bounded Operations mobile routes (`category` is omitted only for union synchronization):

- `GET /api/v1/operations/flights/mobile?page=1&pageSize=20&search=...&category=MyFlights`
- `GET /api/v1/operations/flights/mobile/{id}`
- `GET /api/v1/operations/flights/mobile/customer-logos/{customerId}?expectedVersion={logoVersion}` for customers on a currently visible flight; the response ETag identifies the returned version and `412` rejects a version mismatch
- `GET /api/v1/operations/flights/mobile/{id}/invitation-options`
- `POST /api/v1/operations/flights/mobile/{id}/invite` with `{ "staffMemberIds": [...] }` and `If-Match`.

Shared catalog calls use the `customers`, `aircraft-types`, `services`, `tools`, `materials`, `general-supports`, `ata-chapters`, and `staff-members` options routes under `/api/v1/masterdata`. Catalog change hints use the retained `/hubs/mobile-sync` hub. The flight cache uses only the bounded Operations routes above. There are no calls to the retired `/api/v1/mobile` Operations BFF, legacy flight-sync endpoints, or WO mutation APIs.

## Local development

Use Android Studio Rabbit 2 (2026.2.2) or newer with support for AGP 9.5; older Studio versions can reject Gradle sync even when command-line builds succeed. See the [Android Studio/AGP compatibility table](https://developer.android.com/build/releases/about-agp#android_gradle_plugin_and_android_studio_compatibility). Use JDK 17 with the pinned Gradle 9.8.2 / Android Gradle Plugin 9.5.0 toolchain, Android SDK platform 37, and SDK Build Tools 36.0.0. AGP uses built-in Kotlin with an explicit Kotlin Gradle plugin 2.4.21 classpath override; the Compose and serialization compiler plugins use the same version, and Room uses KSP 2.3.13. Do not apply the separate `kotlin-android` plugin. APK naming uses the public `androidComponents` API. The app compiles and targets Android 17 (API 37), with minimum API 26 retained. See the [AGP compatibility requirements](https://developer.android.com/build/releases/agp-9-5-0-release-notes) and [built-in Kotlin migration](https://developer.android.com/build/migrate-to-built-in-kotlin).

The dependency refresh uses Compose BOM 2026.09.00 / Material3 1.4.0, Room 2.8.5, DataStore 1.3.0, WorkManager 2.12.0, and Firebase BOM 35.1.0. The HTTP/realtime libraries use Ktor 3.6.1, kotlinx.serialization 1.12.0, kotlinx.coroutines 1.11.0, and SignalR 10.0.14. Existing Room schemas and explicit migrations remain unchanged. Validate Android 16/17 back navigation, edge-to-edge insets, large-screen resizing, and notification navigation on representative devices before release; a successful build or lint run alone does not establish runtime compatibility. The API URL is selected at build time through `BuildConfig.API_BASE_URL`.

For the current branch's Docker API, set the ignored `local.properties` entry:

```properties
operations.api.debug.base.url=http://10.0.2.2:5213
```

`10.0.2.2` is the Android emulator's host-loopback alias. Changing the property requires rebuilding the app.

### Run on a USB-connected phone

Keep the branch's Docker API running and reachable on the computer at `http://127.0.0.1:5213`. Enable USB debugging on the phone, connect it, and accept its USB debugging authorization prompt. For this workflow, replace the ignored `local.properties` API entry with:

```properties
operations.api.debug.base.url=http://127.0.0.1:5213
```

From this directory, list devices and replace `YOUR_PHONE_SERIAL` with the phone's serial below. `adb` must be on `PATH` (on this Mac it is in `~/Library/Android/sdk/platform-tools`). Selecting the serial avoids deploying to an emulator that is also running.

```sh
adb devices -l
phone_serial=YOUR_PHONE_SERIAL
adb -s "$phone_serial" reverse tcp:5213 tcp:5213
export JAVA_HOME=$(/usr/libexec/java_home -v 17)
ANDROID_SERIAL="$phone_serial" ./gradlew :app:installDebug
```

The install task rebuilds the app with the configured URL. Open **Operations Module** on the phone, or select the connected Huawei phone in Android Studio and run **app** after syncing. [ADB reverse port forwarding](https://developer.android.com/develop/ui/views/layout/webapps/access-local-server) sends the phone's loopback port to the computer's API; repeat the reverse command after reconnecting the phone. To return to the emulator workflow, restore `operations.api.debug.base.url=http://10.0.2.2:5213` in `local.properties` and rebuild.

For a LAN connection instead of USB forwarding, use a reachable private IP or `.local` hostname for the same branch API. On Android 17, a configured private IP or `.local` backend prompts once for Nearby devices (local-network access). If denied, cached reads remain available; enable the permission in app Settings to reconnect. Public hosted API endpoints do not prompt. Other DNS names resolving to LAN addresses are not detected by this startup prompt.

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
