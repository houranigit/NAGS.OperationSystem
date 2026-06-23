# Migration Inventory

Single source of truth for the gradual rewrite from the old Blazor OperationsSystem (under `legacy/`) to v1.0.0.

Update a row whenever a feature's status changes. Status values: `not-started`, `in-progress`, `done`, `verified` (behavior confirmed against the old project).

## How To Use

1. When starting a feature, locate its row and set status to `in-progress`.
2. Confirm/expand the old reference files you used.
3. When the v1.0.0 feature is complete and tested, set `done`.
4. After comparing behavior against the old project, set `verified`.

Reference paths are relative to `legacy/src/`.

## Key Findings From The Legacy Census

- The legacy is already a DDD modular monolith (Result pattern, aggregates, value objects, snapshots, domain + integration events, outbox/inbox, vertical slices). The rewrite is primarily **legacy Blazor UI → API-first backend + a new clean Blazor (Auto + Radzen) portal**, not an architecture rebuild.
- **No `/api/v1`**: legacy exposes only `/api/identity/*`, `/api/notifications`, and `/api/mobile/v2`. Legacy Core/Store plus Contracts/Operations portal features have **no REST** today — every Blazor page needs an API-first equivalent in the rewrite.
- **Audit module is a stub**: real audit is automatic EF change capture into `audit.AuditTrails` (`BuildingBlocks.Infrastructure/Persistence/BaseDbContext.cs`, owned by legacy Core migrations). `SecurityEventType` is enum-only with no writers.
- **MasterData replaces legacy Core + Store**: catalog-only customers, staff members (legacy Employees), stations, services, tools, materials, and related records share one explicit v1.0.0 boundary. Stock and warehouse workflows require a future `Inventory` module rather than expanding MasterData.
- **Roles admin UI missing**: API + queries exist (`RoleEndpoints.cs`) but `/system/roles` page is absent.
- **Permissions are coarse** in legacy (Portal/Scheduler/Flights/Users/Roles/Sessions); master-data pages gated by `portal.manage`. v1.0.0 moves to `module.resource.action`.
- **Mobile is deferred** for v1.0.0 (BFF, offline sync, FCM, `MobileJwt`). Legacy mobile v2 + sync hub are reference for when mobile returns.
- **No CSV/PDF/Excel exports** exist in the legacy.
- Snapshots, per-station work-order numbering, contract numbering, advance-payment consumption, and the SignalR+FCM notification fan-out are the strong behaviors to preserve.

## Modules

### Identity

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Authentication (login, refresh, logout, activate, change-password) | done | `Identity.Application/Commands/{Login,RefreshToken,Logout}/*`, `Identity.Presentation/Endpoints/AuthEndpoints.cs` | `src/Modules/Identity/**` (Features/Auth, Endpoints/AuthEndpoints) | JWT access + httpOnly refresh-cookie rotation; verified via integration tests |
| Account lifecycle (invite, resend invite, activate, update, deactivate) | done | `Identity.Application/Commands/{CreateUser,InviteUser,ResendInvitation,ActivateAccount,UpdateUser,UnlockUser,ChangePassword}/*`, `UserEndpoints.cs` | `Identity.Application/Features/Users/*`, `Identity.Api/Endpoints/UserEndpoints.cs` | Invitation flow with token; logging notifier (SMTP later) |
| User lockout | done | `Identity.Domain/Aggregates/User/{User.cs,PasswordHistoryEntry.cs}` | `Identity.Domain/Users/User.cs` | Auto-lock on failed attempts + manual lock/unlock. Password history deferred |
| Roles & permissions admin | done | `Identity.Application/Commands/{CreateRole,UpdateRolePermissions,AssignRole,RemoveRole}/*`, `RoleEndpoints.cs`, `Identity.Domain/Aggregates/Role/*`, `Authorization/Permissions.cs` | `Identity.Domain/Roles`, `Identity.Application/Features/Roles`, `Identity.Api/Endpoints/RoleEndpoints.cs` | New `module.resource.action` catalog; system roles protected; Blazor CRUD + permissions UI |
| Sessions (refresh-token tracking, rotation, revoke) | done | `Identity.Application/Commands/{RevokeSession,RevokeAllSessions}/*`, `SessionEndpoints.cs`, `Aggregates/UserSession/UserSession.cs` | `Identity.Domain/Sessions/UserSession.cs`, `Identity.Application/Features/Sessions/*`, `Identity.Api/Endpoints/SessionEndpoints.cs` | Issue/rotate/revoke + admin (list/revoke/revoke-all per user) and self-service (`/me/sessions` list, revoke, sign-out-others) endpoints; integration tests cover current-session marking and revoke flows |
| User queries (paginated users, role lookups) | done | `Identity.Application/Queries/{GetPaginatedUsers,GetAllRoleSelectOptions,GetRolesOverview}/*` | `Identity.Application/Features/{Users,Roles}/*Queries.cs` | Paged + search + status filter |
| Admin/role seeding + permission catalog | done | `Identity.Infrastructure/Seeding/{RoleSeeder.cs,AdminSeeder.cs}` | `Identity.Infrastructure/Seeding/IdentityDataSeeder.cs` | Seeds System Admin role (all perms) + bootstrap admin; `SystemUserId` |
| Identity integration events | done | `Identity.Contracts/IntegrationEvents/*`, `Identity.Application/IntegrationEvents/Handlers/EmployeeUserCreationRequestedIntegrationEventHandler.cs` | `Identity.Contracts/IntegrationEvents.cs`, `Identity.Application/Features/PortalAccess/*`, `Identity.Application/Features/Auth/ConfirmEmailChange.cs` | Outbox/inbox-driven (BuildingBlocks): consumes `PortalAccessRequested`/`LinkedRecordDeactivated`/`LinkedEmailChangeRequested` from MasterData and emits `PortalUserProvisioned`/`PortalUserDeactivated`; provisions invited User with `UserType`+`ExternalReferenceId`, enforces role↔UserType compatibility, deactivates/releases email + revokes sessions, email-reverification confirm flow |
| Portal access + lifecycle (cross-module) | done | (legacy Employee↔User events) | `MasterData.Application/Features/PortalAccess/*`, `MasterData.Application/Features/Customers/RemoveCustomerContact.cs`, `Identity.Application/Features/PortalAccess/*` | Grant access at create/later with a compatible Role via outbox/inbox; invitation provisioning; linked-email reverification; CustomerContact removal + released-email reuse (filtered unique index); deactivation propagation (Station/Customer/StaffMember/Contact → User) with session revocation; idempotent inbox; grant-access Blazor dialog on StaffMember/Customer-contact UIs; end-to-end + idempotency integration tests |
| Server-side data scope (fail-closed) | done | `Host.Web/Authorization/*` (coarse `portal.manage`) | `BuildingBlocks.Application/Abstractions/IUserContext.cs`, `BuildingBlocks.Api/Security/HttpUserContext.cs`, `MasterData.Application/Authorization/MasterDataScope.cs` | `UserType`+`ExternalReferenceId` claims on the JWT; MasterData query/command handlers restrict StationStaff to their Station and CustomerContact to their Customer, failing closed when the linked record or parent is missing/inactive (even mid-event); integration tests cover cross-scope denial and deactivation fail-closed |
| Permission-based authorization (API) | done | `Host.Web/Authorization/PortalPolicies.cs` | `BuildingBlocks.Api/Authorization/PermissionAuthorization.cs` | Dynamic per-permission policy provider; `RequirePermission(...)` |
| Blazor portal: auth + Users/Roles/Sessions screens | in-progress | (Blazor `Components/Pages/Settings/System/**`) | `src/Host/OperationsSystem.Blazor/**` | Blazor Web App (Interactive Auto) + Radzen.Blazor (Material3); typed client over `BrowserApiClient` (JS fetch proxied to `/api/v1`); `AuthenticationStateProvider` + permission-gated UI; login, activation, account (profile/change-password/self-service sessions), users (list/detail/invite/edit/role/lifecycle + sessions), roles (CRUD + grouped permissions); in-memory token + refresh cookie. Replaces the retired React project (removed 2026-06-19) |

### MasterData

Design source of truth: `docs/modules/master-data-foundation.md`.

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Customer (+ contacts, optional user link) | done | `Core.Domain/Aggregates/Customer/*`, `Core.Application/Features/Customer/**` | `MasterData.*/Features/Customers` | Full stack: required active Country; 2-char IATA (unique) + optional 3-letter ICAO (filtered-unique); revised Address VO (separate table) with optional line2/region/postal; official email/phone; logo via IFileStorage (multipart endpoint); contact reconciliation by stable id with per-customer active-email uniqueness; add-contact endpoint; rowversion/ETag; `/api/v1/masterdata/customers` + permissions; typed client + list/detail/dialog + nested-contacts UI + nav; domain + integration tests. Contact portal access + LinkedUserId now wired via the portal-integration phase (grant-access UI + outbox/inbox provisioning) |
| StaffMember (+ working schedule, licenses, optional user provisioning) | done | `Core.Domain/Aggregates/Employee/*`, `Core.Application/Features/Employee/**` | `MasterData.*/Features/StaffMembers` | Full stack: required active Station + ManpowerType (app guards), unique normalized email (app + filtered DB index), optional `EmploymentContract` period (end ≥ start), optional `WorkingSchedule` 7-bit mask, license assignments reconciled by stable id with unique-license-type rule + uppercase number, rowversion/ETag, `/api/v1/masterdata/staff-members` + permissions, typed client + list/detail/dialog UI + nav + Station-detail nested staff section, domain + integration tests. Portal-access user provisioning now delivered in the portal-integration phase (grant-access UI + outbox/inbox provisioning + linked-email reverification + deactivation propagation). Detail page redesigned (2026-06-23) as a tabbed SaaS workspace (hero + Overview/Licenses/Schedule/Portal Access/Activity tabs, stat tiles, schedule chips, timeline-ready Activity placeholder) — frontend-only, no API change |
| Station | done | `Core.Domain/Aggregates/Station/Station.cs`, `Features/Station/**` | `MasterData.*/Features/Stations` | Full stack: 3-letter IATA (unique) + optional 4-letter ICAO (filtered-unique), required active Country (app + FK guard), name/city, rowversion/ETag, `/api/v1/masterdata/stations` + permissions, typed client + list/detail/dialog UI + nav, nested StaffMembers section scaffold, domain + integration tests |
| AircraftType | not-started | `Core.Domain/Aggregates/AircraftType/*`, `Features/AircraftType/**` | `MasterData.*/Features/AircraftTypes` | |
| OperationType (incl. seeded Ad Hoc) | not-started | `Core.Domain/Aggregates/OperationType/*`, `Features/OperationType/**` | `MasterData.*/Features/OperationTypes` | Ad Hoc OT has special domain rules |
| Service (incl. seeded AOG, On Call) | not-started | `Core.Domain/Aggregates/Service/*`, `Features/Service/**` | `MasterData.*/Features/Services` | AOG-only service rule in contracts |
| ManpowerType | done | `Core.Domain/Aggregates/ManpowerType/*`, `Features/ManpowerType/**` | `MasterData.*/Features/ManpowerTypes` | Full stack: domain + CQRS, EF + migration, unique name, rowversion/ETag, `/api/v1/masterdata/manpower-types` + permissions, typed client + list/detail/dialog UI + nav, domain + integration tests |
| License | done | `Core.Domain/Aggregates/License/*`, `Features/License/**` | `MasterData.*/Features/Licenses` | Full stack: domain + CQRS, EF + migration, unique uppercase alphanumeric code (immutable after create), rowversion/ETag, `/api/v1/masterdata/licenses` + permissions, typed client + list/detail/dialog UI + nav, domain + integration tests |
| Country | done | `Core.Domain/Aggregates/Country/*`, `Features/Country/**` | `MasterData.*/Features/Countries` | First MasterData slice complete: domain + CQRS, EF `masterdata` schema + migration, idempotent ISO-3166 seed, `/api/v1/masterdata/countries` with permissions + rowversion/ETag concurrency, typed Blazor client + list/detail/dialog UI + nav, domain + integration tests |
| Currency (+ exchange rates) | not-started | `Core.Domain/Aggregates/Currency/*`, `Features/Currency/**` | `MasterData.*/Features/Currencies` | Cross rates; seeded SAR/USD |
| Unit | not-started | `Store.Domain/Aggregates/Unit/*`, `Store.Application/Features/Unit/**` | `MasterData.*/Features/Units` | Measurement units; catalog only |
| Tool (+ equipment lines) | not-started | `Store.Domain/Aggregates/Tool/{Tool.cs,Equipment.cs}`, `Features/Tool/**` | `MasterData.*/Features/Tools` | Add/update/remove equipment; no stock tracking |
| Material | not-started | `Store.Domain/Aggregates/Material/*`, `Features/Material/**` | `MasterData.*/Features/Materials` | Catalog only; stock behavior belongs in a future Inventory module |
| General support | not-started | `Store.Domain/Aggregates/GeneralSupport/*`, `Features/GeneralSupport/**` | `MasterData.*/Features/GeneralSupports` | Catalog only |
| Service price plans (bracketed) | not-started | `Core.Domain/Aggregates/ServicePricePlan/*`, `Features/ServicePricePlan/**` | `MasterData.*/Features/ServicePricePlans` | Tiered catalog pricing |
| Manpower price plans (bracketed) | not-started | `Core.Domain/Aggregates/ManpowerPricePlan/*`, `Features/ManpowerPricePlan/**` | `MasterData.*/Features/ManpowerPricePlans` | Tiered catalog pricing |
| Tool price plans (bracketed) | not-started | `Store.Domain/Aggregates/ToolPricePlan/*`, `Features/ToolPricePlan/**`, `Store.Domain/Events/ToolPricePlan*` | `MasterData.*/Features/ToolPricePlans` | Tiered catalog pricing |
| Material price plans (bracketed) | not-started | `Store.Domain/Aggregates/MaterialPricePlan/*`, `Features/MaterialPricePlan/**`, `Store.Domain/Events/MaterialPricePlan*` | `MasterData.*/Features/MaterialPricePlans` | Tiered catalog pricing |
| General support price plans (bracketed) | not-started | `Store.Domain/Aggregates/GeneralSupportPricePlan/*`, `Features/GeneralSupportPricePlan/**`, `Store.Domain/Events/GeneralSupportPricePlan*` | `MasterData.*/Features/GeneralSupportPricePlans` | Tiered catalog pricing |
| Catalog dashboard KPIs | not-started | `Core.Application/Features/Dashboard/Queries/GetCatalogDashboard/*` | `MasterData.Application/Features/Dashboard` | Home dashboard |
| Cross-module readers + snapshots | not-started | `Core.Contracts/Readers/*`, `Core.Infrastructure/Readers/*`, `Core.Contracts/Features/**`, `Store.Contracts/Readers/*`, `Store.Contracts/Features/**` | `MasterData.Contracts`, `MasterData.Infrastructure/Readers` | StaffMember, customer, station, service, tool, material, and support snapshots/read models |
| MasterData seeding + well-known ids | not-started | `Core.Infrastructure/Seeding/CoreDataSeeder.cs`, `Core.Contracts/Seeding/CoreSeedIds.cs` | `MasterData.Infrastructure/Seeding`, `MasterData.Contracts/Seeding` | Stable GUIDs; `SystemUserId` |
| MasterData integration event handlers (name propagation, user creation, deactivation) | not-started | `Core.Contracts/IntegrationEvents/*`, `Core.Application/IntegrationEvents/Handlers/*` | `MasterData.Application/IntegrationEvents` | Propagate name changes to snapshots |

### Contracts

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Contract create/update (wizard) | not-started | `Contracts.Domain/Aggregates/Contract/Contract.cs`, `Contracts.Application/Features/Contract/Commands/{CreateContract,UpdateContract}/*` | | Stations, OTs, 5 pricing line types, fees, advance payments |
| Contract lifecycle (suspend, activate/resume, terminate) | not-started | `Contracts.Application/Features/Contract/Commands/{SuspendContract,ActivateContract,TerminateContract}/*` | | Draft→Active→Suspended→Active→Expired/Terminated |
| Contract pricing lines (service/manpower/tool/material/general-support, brackets, overrides) | not-started | `Contracts.Domain/Aggregates/Contract/Pricing/*` | | Per-line package balances preserved across edits |
| Advance payments (per operation type, consumption) | not-started | `Contracts.Domain/Aggregates/Contract/ContractAdvancePayment.cs`, `ValueObjects/ScheduledAdvancedPayment.cs`, `Events/ContractAdvancePaymentConsumedEvent.cs` | | Balance/deposit consumption + shortfall events |
| Cancellation & delay charge plans (brackets) | not-started | `Contracts.Domain/Aggregates/Contract/{CancellationBracket.cs,DelayBracket.cs}` | | Penalty bracket tables |
| Contract numbering | not-started | `Contracts.Domain/.../ContractNo.cs` | | Human-readable contract number VO |
| Contract queries (by id, paginated, light, find-for-flight) | not-started | `Contracts.Application/Features/Contract/Queries/*` | | `FindContractForFlight` binds flight↔contract |
| Contract read service (for Operations) | not-started | `Contracts.Infrastructure/Readers/ContractReadService.cs` | | Surfaces `DebriefRequired` to WO approval |
| Contract status sync job | not-started | `Contracts.Infrastructure/BackgroundJobs/ContractStatusSyncJob.cs` | | Auto Draft/Active/Expired every 5 min |
| Contract expiring-soon notification job | not-started | `Contracts.Infrastructure/BackgroundJobs/ContractExpiringNotificationJob.cs` | | Every 6 hr |
| Contract integration events (lifecycle, expiring, advance balance) | not-started | `Contracts.Contracts/IntegrationEvents/*`, `Contracts.Application/IntegrationEvents/Handlers/*` | | Several handlers are placeholders |

### Operations

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Flight scheduling (create, batch create, update, cancel) | not-started | `Operations.Domain/Aggregates/Flight/Flight.cs`, `Operations.Application/Features/Flight/Commands/{CreateFlight,BatchCreateFlights,UpdateFlight,CancelFlight}/*` | | Customer/station/OT snapshots, services, crew |
| AOG flights (claim, auto-WO job) | not-started | `Operations.Application/Features/Flight/Commands/ClaimAogFlight/*`, `Operations.Infrastructure/BackgroundJobs/AutoAogWorkOrderJob.cs` | | Auto-issue WO for overdue AOG flights |
| Ad-hoc flights & work orders from scratch | not-started | `Operations.Application/Features/Flight/Commands/CreateAdHocWorkOrderFromScratch/*` | | |
| StaffMember flight assignment / invitation | not-started | `Operations.Application/Features/Flight/Commands/{InviteEmployeeToFlight,InviteEmployeesToFlight}/*`, `Domain/Entities/FlightAssignedEmployee.cs` | | Rename legacy Employee terminology; emits a StaffMember invitation integration event |
| Work order authoring (service lines, tasks, attachments, RTR) | not-started | `Operations.Domain/Aggregates/WorkOrder/WorkOrder.cs`, `Domain/Entities/WorkOrderTask*.cs`, `Features/WorkOrder/Commands/{CreateWorkOrderForFlight,UpdateWorkOrder,RecordReturnToRampLines}/*` | | Unified task model with staff members/tools/materials/general supports |
| Work order review workflow (approve, reject, revoke) | not-started | `Operations.Application/Features/WorkOrder/Commands/{ApproveWorkOrder,RejectWorkOrder,RevokeWorkOrder}/*` | | Approval assigns WO number; debrief gate from contract |
| Work order deferred deletion | not-started | `Operations.Application/Features/WorkOrder/Commands/DeleteWorkOrder/*`, `Infrastructure/BackgroundJobs/WorkOrderDeletionJob.cs` | | Mark-for-deletion → hard delete after delay |
| Per-station work order numbering | not-started | `Operations.Domain/StationWorkOrderSequence/*`, `Operations.Infrastructure/Persistence/{StationWorkOrderCounter.cs,Repositories/StationWorkOrderSequenceRepository.cs}` | | Serializable allocation; exception to optimistic concurrency |
| Flight queries (by id, paginated, lights, in-period) | not-started | `Operations.Application/Features/Flight/Queries/{GetFlightById,GetPaginatedFlights,GetPaginatedFlightLights,GetFlightLightsInPeriod}/*` | | Scheduler grids |
| Operations dashboard KPIs | not-started | `Operations.Application/Features/Dashboard/Queries/GetOperationsDashboard/*` | | |
| Customer signature capture (work order) | not-started | `Operations.Domain/Aggregates/WorkOrder/WorkOrder.cs` (CustomerSignature) | | Move bytes to object storage (metadata in DB) |
| [DEFERRED] Mobile v2 BFF endpoints | not-started | `Operations.Presentation/Mobile/MobileV2Endpoints.cs` | | Reference only; mobile deferred for v1.0.0 |
| [DEFERRED] Mobile offline sync (REST + hub) | not-started | `Operations.Presentation/Mobile/Sync/MobileSyncEndpoints.cs`, `BuildingBlocks.Application/Abstractions/Mobile/Sync/*`, `Host.Web` `MobileSyncHub` | | Reference only; mobile deferred |

### Notifications

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Notification inbox (list, unread count, mark read, mark all read, archive all) | not-started | `Notifications.Domain/Aggregates/Notification/*`, `Notifications.Application/Features/{GetMyInbox,GetUnreadCount,MarkAsRead,MarkAllAsRead,ArchiveAll}/*`, `Notifications.Presentation/Endpoints/NotificationEndpoints.cs` | | Read/archived state |
| SignalR live delivery (web) | not-started | `Notifications.Presentation/Hubs/{NotificationsHub.cs,SignalRNotificationPusher.cs}` | | v1.0.0 real-time transport |
| Composite pusher abstraction | not-started | `Notifications.Infrastructure/Push/CompositeNotificationPusher.cs`, `Notifications.Application/Abstractions/I*NotificationPusher.cs` | | Keep abstraction so FCM slots in later |
| Notification-from-event handlers | not-started | `Notifications.Application/IntegrationEvents/Handlers/FlightEmployeeInvitedIntegrationEventHandler.cs` | | Create inbox row + push |
| [DEFERRED] Device tokens + FCM push | not-started | `Notifications.Domain/Aggregates/DeviceToken/*`, `Notifications.Application/Features/{RegisterDeviceToken,RevokeDeviceToken}/*`, `Notifications.Infrastructure/Push/{FcmNotificationPusher.cs,FirebaseAppFactory.cs}` | | Deferred with mobile |

### Audit

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Automatic EF change-capture audit trail | not-started | `BuildingBlocks.Infrastructure/Persistence/BaseDbContext.cs`, `Persistence/Models/AuditTrail.cs`, `Core.Infrastructure/Persistence/CoreDbContext.cs` | `Audit.Infrastructure` | New Audit module owns `audit.AuditTrails`; do not preserve legacy Core migration ownership |
| Business/security event audit | not-started | `Audit.Domain/Enumerations/SecurityEventType.cs`, `Audit.Contracts/SecurityEvent/SecurityEventDto.cs` | | Legacy is enum-only stub; implement properly in v1.0.0 |

### BuildingBlocks (shared foundation)

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| CQRS abstractions + pipeline behaviors | not-started | `BuildingBlocks.Application/Abstractions/**`, `Behaviors/{Validation,Authorization,Logging,Transaction}*` | | |
| Result/Error + domain primitives | not-started | `BuildingBlocks.Domain/{Results,Aggregates,Entities,ValueObjects,Events}/*` | | Preserve Result pattern |
| Outbox / Inbox processing | not-started | `BuildingBlocks.Infrastructure/Outbox/OutboxProcessorJob.cs`, `Persistence/Models/OutboxMessage.cs` | | Per-module outbox processor (10s) |
| Integration event base | not-started | `BuildingBlocks.Contracts/IntegrationEvents/*` | | |
| Email (SMTP) | not-started | `BuildingBlocks.Infrastructure/Email/SmtpEmailSender.cs` | | Invitations etc. |
| Pagination | not-started | `BuildingBlocks.Application/Pagination/PaginatedResult.cs` | | Maps to `PagedResult<T>` API convention |

### Host / Cross-cutting

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Module composition & wiring | not-started | `Host.Web/Program.cs`, `Extensions/ServiceCollectionExtensions.cs` | | New host stays thin |
| Ordered migrations apply | not-started | `Host.Web/Extensions/DatabaseMigrationExtensions.cs` | | Identity → Audit → MasterData → … |
| Auth policies (portal cookie + MobileJwt) | not-started | `Host.Web/Authorization/PortalPolicies.cs`, `Extensions/WebApplicationExtensions.cs` | | v1.0.0: web cookie now, Bearer scheme ready |
| OpenAPI / Scalar | not-started | `Host.Web/Program.cs` | | Make OpenAPI a first-class artifact (Scalar) for the Blazor portal, future mobile, and integrations |
