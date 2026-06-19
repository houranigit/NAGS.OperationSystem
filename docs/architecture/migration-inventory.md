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
- **No `/api/v1`**: legacy exposes only `/api/identity/*`, `/api/notifications`, and `/api/mobile/v2`. Core/Store/Contracts/Operations portal features have **no REST** today — every Blazor page needs an API-first equivalent in the rewrite.
- **Audit module is a stub**: real audit is automatic EF change capture into `audit.AuditTrails` (`BuildingBlocks.Infrastructure/Persistence/BaseDbContext.cs`, owned by Core migrations). `SecurityEventType` is enum-only with no writers.
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
| Identity integration events | not-started | `Identity.Contracts/IntegrationEvents/*`, `Identity.Application/IntegrationEvents/Handlers/EmployeeUserCreationRequestedIntegrationEventHandler.cs` | | Cross-module events; revisit when Core/Employee lands |
| Permission-based authorization (API) | done | `Host.Web/Authorization/PortalPolicies.cs` | `BuildingBlocks.Api/Authorization/PermissionAuthorization.cs` | Dynamic per-permission policy provider; `RequirePermission(...)` |
| Blazor portal: auth + Users/Roles/Sessions screens | in-progress | (Blazor `Components/Pages/Settings/System/**`) | `src/Host/OperationsSystem.Blazor/**` | Blazor Web App (Interactive Auto) + Radzen.Blazor (Material3); typed client over `BrowserApiClient` (JS fetch proxied to `/api/v1`); `AuthenticationStateProvider` + permission-gated UI; login, activation, account (profile/change-password/self-service sessions), users (list/detail/invite/edit/role/lifecycle + sessions), roles (CRUD + grouped permissions); in-memory token + refresh cookie. Replaces the retired React project (removed 2026-06-19) |

### Core (reference / master data)

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Customer (+ contacts, optional user link) | not-started | `Core.Domain/Aggregates/Customer/*`, `Core.Application/Features/Customer/**` | | Contact sync; activate/deactivate; mobile catalog refresh |
| Employee (+ licenses, optional user provisioning) | not-started | `Core.Domain/Aggregates/Employee/*`, `Core.Application/Features/Employee/**` | | Station + manpower type; emits `EmployeeUserCreationRequested` |
| Station | not-started | `Core.Domain/Aggregates/Station/Station.cs`, `Features/Station/**` | | IATA + timezone |
| AircraftType | not-started | `Core.Domain/Aggregates/AircraftType/*`, `Features/AircraftType/**` | | |
| OperationType (incl. seeded Ad Hoc) | not-started | `Core.Domain/Aggregates/OperationType/*`, `Features/OperationType/**` | | Ad Hoc OT has special domain rules |
| Service (incl. seeded AOG, On Call) | not-started | `Core.Domain/Aggregates/Service/*`, `Features/Service/**` | | AOG-only service rule in contracts |
| ManpowerType | not-started | `Core.Domain/Aggregates/ManpowerType/*`, `Features/ManpowerType/**` | | |
| License | not-started | `Core.Domain/Aggregates/License/*`, `Features/License/**` | | |
| Country | not-started | `Core.Domain/Aggregates/Country/*`, `Features/Country/**` | | |
| Currency (+ exchange rates) | not-started | `Core.Domain/Aggregates/Currency/*`, `Features/Currency/**` | | Cross rates; seeded SAR/USD |
| Service price plans (bracketed) | not-started | `Core.Domain/Aggregates/ServicePricePlan/*`, `Features/ServicePricePlan/**` | | Tiered pricing |
| Manpower price plans (bracketed) | not-started | `Core.Domain/Aggregates/ManpowerPricePlan/*`, `Features/ManpowerPricePlan/**` | | Tiered pricing |
| Catalog dashboard KPIs | not-started | `Core.Application/Features/Dashboard/Queries/GetCatalogDashboard/*` | | Home dashboard |
| Cross-module readers + snapshots | not-started | `Core.Contracts/Readers/*`, `Core.Infrastructure/Readers/*`, `Core.Contracts/Features/**` | | `IEmployeeReader`, `ICustomerReader`, `IStationReader`, `IServiceReader`, snapshot DTOs |
| Core seeding + well-known ids | not-started | `Core.Infrastructure/Seeding/CoreDataSeeder.cs`, `Core.Contracts/Seeding/CoreSeedIds.cs` | | Stable GUIDs; `SystemUserId` |
| Core integration event handlers (name propagation, user creation, deactivation) | not-started | `Core.Contracts/IntegrationEvents/*`, `Core.Application/IntegrationEvents/Handlers/*` | | Propagate name changes to snapshots |
| Audit trail table ownership | not-started | `Core.Infrastructure/Persistence/CoreDbContext.cs` | | `audit.AuditTrails` migration owner |

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
| Employee flight assignment / invitation | not-started | `Operations.Application/Features/Flight/Commands/{InviteEmployeeToFlight,InviteEmployeesToFlight}/*`, `Domain/Entities/FlightAssignedEmployee.cs` | | Emits `FlightEmployeeInvited` integration event |
| Work order authoring (service lines, tasks, attachments, RTR) | not-started | `Operations.Domain/Aggregates/WorkOrder/WorkOrder.cs`, `Domain/Entities/WorkOrderTask*.cs`, `Features/WorkOrder/Commands/{CreateWorkOrderForFlight,UpdateWorkOrder,RecordReturnToRampLines}/*` | | Unified task model w/ employees/tools/materials/general supports |
| Work order review workflow (approve, reject, revoke) | not-started | `Operations.Application/Features/WorkOrder/Commands/{ApproveWorkOrder,RejectWorkOrder,RevokeWorkOrder}/*` | | Approval assigns WO number; debrief gate from contract |
| Work order deferred deletion | not-started | `Operations.Application/Features/WorkOrder/Commands/DeleteWorkOrder/*`, `Infrastructure/BackgroundJobs/WorkOrderDeletionJob.cs` | | Mark-for-deletion → hard delete after delay |
| Per-station work order numbering | not-started | `Operations.Domain/StationWorkOrderSequence/*`, `Operations.Infrastructure/Persistence/{StationWorkOrderCounter.cs,Repositories/StationWorkOrderSequenceRepository.cs}` | | Serializable allocation; exception to optimistic concurrency |
| Flight queries (by id, paginated, lights, in-period) | not-started | `Operations.Application/Features/Flight/Queries/{GetFlightById,GetPaginatedFlights,GetPaginatedFlightLights,GetFlightLightsInPeriod}/*` | | Scheduler grids |
| Operations dashboard KPIs | not-started | `Operations.Application/Features/Dashboard/Queries/GetOperationsDashboard/*` | | |
| Customer signature capture (work order) | not-started | `Operations.Domain/Aggregates/WorkOrder/WorkOrder.cs` (CustomerSignature) | | Move bytes to object storage (metadata in DB) |
| [DEFERRED] Mobile v2 BFF endpoints | not-started | `Operations.Presentation/Mobile/MobileV2Endpoints.cs` | | Reference only; mobile deferred for v1.0.0 |
| [DEFERRED] Mobile offline sync (REST + hub) | not-started | `Operations.Presentation/Mobile/Sync/MobileSyncEndpoints.cs`, `BuildingBlocks.Application/Abstractions/Mobile/Sync/*`, `Host.Web` `MobileSyncHub` | | Reference only; mobile deferred |

### Store

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Unit | not-started | `Store.Domain/Aggregates/Unit/*`, `Store.Application/Features/Unit/**` | | Measurement units |
| Tool (+ equipment lines) | not-started | `Store.Domain/Aggregates/Tool/{Tool.cs,Equipment.cs}`, `Features/Tool/**` | | Add/update/remove equipment |
| Material | not-started | `Store.Domain/Aggregates/Material/*`, `Features/Material/**` | | |
| General support | not-started | `Store.Domain/Aggregates/GeneralSupport/*`, `Features/GeneralSupport/**` | | |
| Tool price plans (bracketed) | not-started | `Store.Domain/Aggregates/ToolPricePlan/*`, `Features/ToolPricePlan/**`, `Store.Domain/Events/ToolPricePlan*` | | |
| Material price plans (bracketed) | not-started | `Store.Domain/Aggregates/MaterialPricePlan/*`, `Features/MaterialPricePlan/**`, `Store.Domain/Events/MaterialPricePlan*` | | |
| General support price plans (bracketed) | not-started | `Store.Domain/Aggregates/GeneralSupportPricePlan/*`, `Features/GeneralSupportPricePlan/**`, `Store.Domain/Events/GeneralSupportPricePlan*` | | |
| Store snapshots (cross-module reads) | not-started | `Store.Contracts/Features/Pricing/*`, `Store.Domain/ValueObjects/{ToolSnapshot,MaterialSnapshot,GeneralSupportSnapshot}.cs` | | |

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
| Automatic EF change-capture audit trail | not-started | `BuildingBlocks.Infrastructure/Persistence/BaseDbContext.cs`, `Persistence/Models/AuditTrail.cs` | | Real audit today; writes `audit.AuditTrails` |
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
| Ordered migrations apply | not-started | `Host.Web/Extensions/DatabaseMigrationExtensions.cs` | | Identity → Core (audit) → … |
| Auth policies (portal cookie + MobileJwt) | not-started | `Host.Web/Authorization/PortalPolicies.cs`, `Extensions/WebApplicationExtensions.cs` | | v1.0.0: web cookie now, Bearer scheme ready |
| OpenAPI / Scalar | not-started | `Host.Web/Program.cs` | | Make OpenAPI a first-class artifact (Scalar) for the Blazor portal, future mobile, and integrations |
