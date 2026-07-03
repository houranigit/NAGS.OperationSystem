# MasterData Module Foundation

Status: **Draft — active design and implementation reference**
Last updated: 2026-07-03

## 1. Purpose And Authority

This document is the living source of truth for the v1.0.0 `MasterData` module. Future implementation prompts should say: **Follow `docs/modules/master-data-foundation.md`.**

The document records three kinds of statements:

- **Decided** — accepted project direction; implementation should follow it.
- **Proposed** — recommended direction awaiting explicit confirmation.
- **Open** — requires further business discussion before implementation.

The legacy project is a business reference for fields, relationships, validation, and workflows. It is not an architectural template. Known incomplete legacy flows must be completed deliberately rather than copied.

## 2. Initial Scope

### Decided

The first MasterData features are:

- Countries
- Stations
- Customers
- Customer contacts
- Manpower types
- Licenses
- Staff members (the station workforce concept named `Employee` in the legacy project)
- Optional portal access for CustomerContacts and StaffMembers

The current MVP also includes additional MasterData catalog-management slices:

- Services
- Operation types
- Aircraft types
- Tools and tool equipment
- Materials
- General support items

Station and Customer start from the legacy business model and are redesigned to follow the v1.0.0 architecture and conventions.

Features such as contracts, flights, and work orders are not part of MasterData. MasterData supplies stable identities and snapshots that those future modules can consume.

### First-release acceptance target

When the initial MasterData implementation is complete, the running application—not merely the domain layer—must provide persistence migrations, APIs, authorization permissions, automated tests, typed Blazor clients, and usable Blazor management screens for:

- Creating, viewing, updating, activating, and deactivating Countries.
- Creating, viewing, updating, activating, and deactivating ManpowerTypes.
- Creating, viewing, updating, activating, and deactivating Licenses.
- Creating, viewing, updating, activating, and deactivating Services.
- Creating, viewing, updating, activating, and deactivating OperationTypes.
- Creating, viewing, updating, activating, and deactivating AircraftTypes.
- Creating, viewing, updating, activating, and deactivating Tools, including tool equipment rows.
- Creating, viewing, updating, activating, and deactivating Materials.
- Creating, viewing, updating, activating, and deactivating GeneralSupport items.
- Creating, viewing, updating, activating, and deactivating Stations.
- Viewing a Station's StaffMembers and adding/managing a StaffMember from the Station experience.
- Creating, viewing, updating, activating, and deactivating Customers.
- Viewing a Customer's contacts and adding, updating, or removing contacts from the Customer experience.
- Creating and updating StaffMembers with general information, required Station and ManpowerType, optional employment-contract period, optional working schedule, and optional license assignments.
- Optionally requesting portal access for a StaffMember or CustomerContact using an administrator-selected compatible Role, according to the invitation workflow in this foundation.

“CRUD” for long-lived master records means create/read/update plus activate/deactivate lifecycle management; it does not imply unrestricted hard deletion. CustomerContact removal follows the explicit removal rules below.

### UI consistency requirement

MasterData UI implementation must follow the established Identity Users/Roles feature patterns already present in `OperationsSystem.Blazor.Client`; it must not introduce a separate visual or interaction system.

- Use Radzen.Blazor with the existing Material3 theme and `--os-*` design tokens.
- Reuse the existing shared shells and primitives where applicable: `PageHeader`, `DataListCard`, `DataListFilter`, `RowActionMenu`, `StatusBadge`, `LoadingCard`, `EmptyState`, `ErrorState`, `RequireAuth`, and `RequirePermission`. `DetailHero`/`DetailField` may be improved or replaced by reusable detail primitives when the new pattern requires it.
- List pages follow the Users/Roles conventions for server pagination, search, sorting, filters, status display, row actions, loading, empty, and error states.
- Detail pages are the deliberate exception: they must improve on the current User/Role detail-page UI rather than copy its visual composition.
- The improved detail pattern must remain consistent across Country, ManpowerType, License, Station, Customer, and StaffMember and should become a reusable portal pattern.
- Detail pages use a compact contextual header with identity, status, and primary actions; clearly grouped information sections; strong visual hierarchy; and efficient use of space.
- Related records receive first-class sections: Station details surface StaffMembers and Customer details surface Contacts with useful empty states and add actions.
- Destructive/lifecycle actions are visually separated from normal edits and require confirmation.
- Detail pages remain responsive, keyboard-accessible, permission-aware, localization-ready, and RTL-safe, and continue using Radzen plus existing design tokens.
- Create/update experiences use consistent Radzen dialogs or structured multi-step forms when child collections make a single form too dense.
- Staff Members also have a dedicated side-navigation list page.
- Feature components use typed client DTOs and methods over `BrowserApiClient`; components do not call raw `HttpClient` or JavaScript fetch directly.
- All routes and controls enforce the decided permission catalog in both UI visibility and backend authorization.
- Styling must remain responsive, localization-ready, and RTL-safe, matching the existing portal conventions.

## 3. Ubiquitous Language

### Decided account-facing names

The system has exactly three user types:

| User type | Meaning | MasterData link | Data scope |
|---|---|---|---|
| `SystemAdministrator` | Internal administrator with system-wide access | None | All permitted data |
| `StationStaff` | A portal account belonging to a person working for a station | StaffMember record | The linked station |
| `CustomerContact` | A portal account belonging to a customer's contact | Customer contact record | The linked customer |

Use the display names **System Administrator**, **Station Staff**, and **Customer Contact**. Avoid the vague UI labels “Station User” and “Customer User.”

`UserType` describes the account's business context and data scope. A `Role` remains a collection of permissions. These concepts must not be collapsed:

- User type answers: **who does this account represent and what business boundary scopes it?**
- Role answers: **what actions may this account perform inside that scope?**

### Decided workforce entity name

Use `StaffMember` for an individual person assigned to a Station.

The legacy `Employee` entity is renamed to `StaffMember`. Do not name an individual `Manpower`: “manpower” describes workforce capacity or a billable labor category rather than one person, and produces awkward domain language such as “one manpower” and “manpowers.”

Recommended vocabulary:

- `StaffMember` — an individual person
- `Station.StaffMembers` — a station's people
- `ManpowerType` — a labor category used for operational assignment or pricing, if that remains the business term
- `ManpowerPricePlan` — pricing for a labor category

## 4. Ownership And Relationships

### Decided

- MasterData owns `Station`, `Customer`, `CustomerContact`, and `StaffMember`.
- Identity owns `User`, credentials, invitations, sessions, roles, and permissions.
- A Station has many StaffMembers.
- A StaffMember belongs to exactly one Station in v1.0.0.
- A Customer owns many Customer Contacts.
- A Customer Contact belongs to exactly one Customer.
- A StaffMember may be linked to zero or one Identity User.
- A Customer Contact may be linked to zero or one Identity User.
- A non-administrator User must be linked to exactly one matching MasterData record.
- Cross-module links use public `Guid` identifiers and contracts/events, never navigation properties or foreign keys into another module's database schema.

### Proposed identity representation

Identity should restore the useful legacy distinction that the current rewrite does not yet contain:

- `User.UserType`
- `User.ExternalReferenceId`

For `StationStaff`, `ExternalReferenceId` identifies the StaffMember. For `CustomerContact`, it identifies the contact. A System Administrator has no external reference.

MasterData may also retain `LinkedUserId` on the StaffMember/contact record for efficient reverse lookup. The link is established through idempotent integration events.

Implementation note: the current rewrite includes `User.UserType`, `User.ExternalReferenceId`, and reverse `LinkedUserId` fields for StaffMember/contact portal links.

## 5. Prerequisite Build Order

### Decided

Build the initial MasterData slices according to these dependencies:

- Country → Station → StaffMember
- ManpowerType → StaffMember
- License → StaffMember license assignments
- Customer → CustomerContact
- StaffMember/CustomerContact → optional Identity linkage and invitation workflows

A Station requires an existing active Country. A StaffMember requires an existing active Station and ManpowerType. License assignments are optional, but every assignment requires an existing active License.

## 6. Country Baseline

### Decided

Country is implemented as a MasterData feature before Station and initially preserves the legacy business model:

- `Id`
- ISO 3166-1 alpha-2 code
- Name
- Active/inactive lifecycle
- Created and updated timestamps

Country codes are trimmed and normalized to uppercase on the backend and are globally unique. Country names are required and have a maximum length of 100 characters.

Countries use both initialization and administrator maintenance:

- An idempotent seed supplies the ISO 3166-1 alpha-2 country list with stable IDs.
- Administrators can create, update, activate, and deactivate Country records.
- Seeding inserts missing baseline rows but must not overwrite administrator changes on every startup.

## 7. ManpowerType Baseline

### Decided

ManpowerType is implemented before StaffMember and initially preserves the legacy business model:

- `Id`
- Name
- Optional description
- Active/inactive lifecycle
- Created and updated timestamps

The name is required and has a maximum length of 100 characters. The description has a maximum length of 500 characters. Only an active ManpowerType may be assigned to a new StaffMember.

## 8. License Baseline

### Decided

License is a MasterData catalog implemented before StaffMember license assignment and preserves the legacy business model:

- `Id`
- Code
- Name
- Optional description
- Active/inactive lifecycle
- Created and updated timestamps

License rules:

- Code is required, contains 2–10 ASCII letters or digits, is normalized to uppercase on the backend, and is globally unique.
- Name is required and has a maximum length of 100 characters.
- Description has a maximum length of 500 characters.
- Only an active License may be newly assigned to a StaffMember.

## 9. Station Baseline

### Decided

The initial Station model preserves the legacy business fields and behavior:

- `Id`
- IATA airport code
- Optional ICAO airport code
- Name
- City
- Country reference
- Active/inactive lifecycle
- Created and updated timestamps

Initial legacy rules to preserve unless later changed:

- IATA code is required, is trimmed and normalized to uppercase on the backend, and must be globally unique.
- A Station IATA code is exactly three ASCII letters.
- ICAO code is optional; when supplied, it is trimmed and normalized to uppercase on the backend and must be globally unique.
- A Station ICAO code is exactly four ASCII letters.
- Name is required and has a maximum length of 150 characters.
- City is required and has a maximum length of 100 characters.
- A Station is active when created.
- Station records are deactivated rather than hard deleted after use.

All instants use `DateTimeOffset`/`TimeProvider` according to the project architecture. The new model must not copy direct `DateTime.UtcNow` calls from the legacy domain.

## 10. Customer Baseline

### Decided

The initial Customer model preserves the legacy business fields and behavior:

- `Id`
- IATA airline code
- Optional ICAO airline code
- Name
- Required Country reference
- Optional official email and phone
- Optional logo reference
- Official address
- Contacts
- Active/inactive lifecycle
- Created and updated timestamps

Initial legacy rules to preserve unless later changed:

- Customer name is required and has a maximum length of 200 characters.
- Country is required and must reference an active Country when the Customer is created or its Country changes.
- IATA code is optional, is normalized to uppercase on the backend when supplied, and is not globally unique across Customers.
- A supplied Customer IATA code is exactly two uppercase letters or digits.
- ICAO code, when supplied, is exactly three letters, is normalized to uppercase on the backend, and must be globally unique across Customers.
- Database unique constraints enforce Station IATA, non-null Station ICAO, and non-null Customer ICAO uniqueness in addition to application validation. Customer IATA is indexed for lookup but is intentionally not unique.
- Official email, when supplied, is normalized and validated.
- A Customer is active when created.
- Contact email addresses are unique within a Customer.
- Customer records are deactivated rather than hard deleted after use.
- Contact collection changes are reconciled by stable contact IDs so existing links are not accidentally destroyed.

Binary logos should follow the project storage abstraction; the Customer database record stores file metadata/reference rather than copying the legacy `byte[]` persistence design.

### Decided revised address model

The Customer address improves on the legacy model rather than copying it unchanged. The required official Address value object contains:

- Address line 1
- Optional address line 2
- City
- Optional state/province/region
- Optional postal code

Address line 1 and city are required. The address uses the Customer's required Country reference rather than storing a second potentially inconsistent Country ID.

## 11. Customer Contact Baseline

### Decided

A Customer Contact contains:

- `Id`
- Customer ID
- Name
- Optional job title
- Email
- Optional phone
- Optional linked User ID
- Active/inactive lifecycle
- Created and updated timestamps

A contact is a business contact regardless of whether portal access is enabled. Portal access is optional and must not define the contact's existence.

CustomerContact creation is available in both places:

- While creating a Customer, the administrator may add zero or more initial Contacts.
- From an existing Customer detail view, an **Add contact** action creates another Contact for that Customer.

Contacts are managed inside the Customer experience; a separate top-level Contacts navigation page is not required for the initial release.

## 12. StaffMember Baseline

### Decided

StaffMember is a separate aggregate that references a Station; it is not stored as a child collection inside the Station aggregate. The Station UI and API may expose nested StaffMember navigation/actions while StaffMember retains its own lifecycle and endpoints.

StaffMember creation is available in all three places:

- While creating a Station, the administrator may add zero or more initial StaffMembers.
- From an existing Station detail view, an **Add staff member** action creates a StaffMember with that Station preselected.
- A top-level **Staff Members** side-navigation entry provides a separate list and create/edit experience; Station is selected there.

StaffMember general information contains:

- `Id`
- Full name
- Email
- Required Station ID
- Required ManpowerType ID
- Optional employment contract period
- Optional working schedule
- Zero or more license assignments
- Optional linked User ID
- Active/inactive lifecycle
- Created and updated timestamps

General rules preserved from legacy:

- Full name is required and has a maximum length of 200 characters.
- Email is required, normalized on the backend, and unique across StaffMembers.
- Station is required and must be active when creating or moving a StaffMember.
- ManpowerType is required and must be active when creating or updating a StaffMember's type.
- Employment contract is optional. When supplied, a start date is required; the end date is optional and cannot precede the start date.
- A StaffMember is active when created.

### Working schedule

WorkingSchedule preserves the legacy day-based model:

- WorkingSchedule is optional for a StaffMember.
- It contains a distinct set of `DayOfWeek` values.
- When a schedule is supplied, at least one working day is required.
- It is persisted as a seven-bit mask (Sunday through Saturday).
- It does not model shift start/end times in this release.

### StaffMember licenses

- A StaffMember may have zero or more license assignments.
- Each assignment contains a stable assignment ID, License ID, and license number.
- License number is required, has a maximum length of 100 characters, and is normalized to uppercase.
- A StaffMember cannot hold the same License type more than once.
- Create/update input sends the complete desired license list; the aggregate reconciles additions, updates, and removals by stable assignment ID.

## 13. Invitation-Only Account Provisioning

### Decided

- Runtime registration is invitation-only; there is no public self-registration.
- Administrators do not create ordinary User accounts directly from Identity.
- A Station Staff or Customer Contact account originates from its MasterData record.
- While creating a StaffMember or contact, an administrator can choose whether to grant portal access.
- An administrator may also grant portal access later from an existing StaffMember or contact profile.
- The administrator selects one role compatible with the requested user type.
- CustomerContact and StaffMember create forms expose a **Create portal user and send invitation** option.
- When that option is selected, selecting a compatible Role is mandatory.
- When it is not selected, only the MasterData record is created; portal access may be requested later from its detail view.
- If portal access is requested, Identity creates an invited account without a password.
- The invitee activates the account by following the invitation and choosing a password.
- The bootstrap System Administrator remains the one special seeding exception.
- A login email is unique among accounts that retain a login identity. A permanently detached account may release its login email for reuse under the removal rules below.
- Provisioning must be idempotent and must never create two Users for the same MasterData record.

### Proposed reliable workflow

1. MasterData saves the StaffMember or Customer Contact and the access request in one transaction.
2. MasterData writes `StationStaffPortalAccessRequested` or `CustomerContactPortalAccessRequested` to its outbox.
3. Identity consumes the request, creates the invited User with the correct user type and role, and commits it.
4. Identity sends the invitation through its notifier after the invitation exists.
5. Identity publishes `UserProvisionedForStationStaff` or `UserProvisionedForCustomerContact`.
6. MasterData consumes the reply and stores `LinkedUserId`.

The legacy employee flow is a partial reference for this process. The legacy customer-contact creation and deactivation handlers are placeholders/no-ops, so they must not be treated as completed behavior.

Provisioning failures such as a duplicate email must be visible to an administrator and retryable. They must not be silently swallowed as in parts of the legacy flow.

### Decided invitation notification behavior

- Identity owns invitation delivery; MasterData never sends SMTP email directly.
- `EmailSettings:EnableEmailNotifications` defaults to `false`.
- When delivery is disabled, no SMTP connection is attempted. Development logs include the activation token for local testing; non-development logs never include it.
- When delivery is enabled, Identity sends an HTML invitation containing a link to `/activate` with the email and invitation token, plus the UTC expiry.
- SMTP delivery happens only after the invited User has been committed.
- A delivery failure is logged and does not delete or roll back the invited User. The account remains `Invited`, and an administrator can use **Resend invitation**.
- Resending rotates the invitation token and expiry before attempting delivery again.
- SMTP passwords and other secrets are supplied through .NET user-secrets or environment variables, never committed to `appsettings*.json`.
- The production activation URL is configuration, not a hard-coded development URL.

### Decided later-access rule

Allow an administrator to request portal access later from an existing StaffMember/CustomerContact profile. This still respects “no manual User creation” because the account always originates from a MasterData identity. It also handles the common case where a person initially does not need access but needs it later.

## 14. Authorization And Data Scope

### Decided

Authorization always combines permissions with server-side data scope:

- System Administrator: may access all records allowed by the role's permissions.
- Station Staff: may access only operational/customer data permitted for the linked Station.
- Customer Contact: may access only data belonging to the linked Customer, such as that customer's future contracts and flights.

A role can remove capabilities but cannot widen a User beyond the User type's data scope. UI hiding is not security; commands and queries enforce scope on the server.

### Decided role model

User types are fixed to `SystemAdministrator`, `StationStaff`, and `CustomerContact`. Roles remain permission collections:

- A User has exactly one Role in v1.0.0.
- A Role contains multiple permissions.
- A Role declares which UserType it is compatible with.
- Multiple Roles may exist for a UserType.
- When requesting portal access, the administrator selects a compatible Role.
- Identity rejects assigning a Role whose UserType does not match the User.
- Portal access cannot be requested until the administrator has created or selected a compatible Role.
- Identity never silently assigns a fallback Role.

Only the protected System Administrator Role is seeded for the bootstrap administrator. No default StationStaff or CustomerContact Role is seeded. Administrators explicitly define roles such as Station Supervisor, Station Technician, Customer Manager, or Customer Viewer and choose one when granting portal access.

### Decided MasterData permission catalog

A permission is one action that can be placed into a Role; it is not itself a Role. For example, a Station Supervisor Role could contain `masterdata.staff-members.view` and `masterdata.staff-members.update`, while a more limited Station Staff Role might contain only the view permission.

The initial MasterData permission resources/actions are:

| Resource | Actions |
|---|---|
| Countries | `view`, `create`, `update`, `activate`, `deactivate` |
| ManpowerTypes | `view`, `create`, `update`, `activate`, `deactivate` |
| Licenses | `view`, `create`, `update`, `activate`, `deactivate` |
| Services | `view`, `create`, `update`, `activate`, `deactivate` |
| OperationTypes | `view`, `create`, `update`, `activate`, `deactivate` |
| AircraftTypes | `view`, `create`, `update`, `activate`, `deactivate` |
| Tools | `view`, `create`, `update`, `activate`, `deactivate` |
| Materials | `view`, `create`, `update`, `activate`, `deactivate` |
| GeneralSupports | `view`, `create`, `update`, `activate`, `deactivate` |
| Stations | `view`, `create`, `update`, `activate`, `deactivate` |
| StaffMembers | `view`, `create`, `update`, `activate`, `deactivate`, `grant-access` |
| Customers | `view`, `create`, `update`, `activate`, `deactivate` |
| CustomerContacts | `view`, `create`, `update`, `remove`, `grant-access` |

Codes follow `masterdata.resource.action`, for example `masterdata.stations.view`. Each future module adds its own permissions when it is designed; Operations permissions do not need to be guessed during MasterData implementation.

### Decided UserType compatibility

Compatibility is the maximum permission set from which a Role for that UserType may select. It does not grant every compatible permission automatically.

`SystemAdministrator` Roles may contain every registered permission.

`StationStaff` Roles may contain the following MasterData permissions:

- `masterdata.countries.view`
- `masterdata.manpower-types.view`
- `masterdata.licenses.view`
- `masterdata.services.view`
- `masterdata.operation-types.view`
- `masterdata.aircraft-types.view`
- `masterdata.tools.view`
- `masterdata.materials.view`
- `masterdata.general-supports.view`
- `masterdata.stations.view`
- `masterdata.stations.update`
- `masterdata.staff-members.view`
- `masterdata.staff-members.create`
- `masterdata.staff-members.update`
- `masterdata.staff-members.activate`
- `masterdata.staff-members.deactivate`

All StationStaff access is restricted to the linked Station. StationStaff cannot create, activate, or deactivate Stations; change global catalogs; manage Customers; grant portal access; or assign Roles.

`CustomerContact` Roles may contain the following MasterData permissions:

- `masterdata.countries.view`
- `masterdata.customers.view`
- `masterdata.customers.update`
- `masterdata.customer-contacts.view`
- `masterdata.customer-contacts.create`
- `masterdata.customer-contacts.update`
- `masterdata.customer-contacts.remove`

All CustomerContact access is restricted to the linked Customer. CustomerContacts cannot create, activate, or deactivate Customers; manage Stations/StaffMembers/global catalogs; grant portal access; or assign Roles.

The `grant-access` permissions are SystemAdministrator-compatible only. This preserves the decision that an administrator chooses the compatible Role before an account is invited and prevents delegated users from escalating privileges.

Each future module defines its own permission actions and UserType compatibility. For example, Operations may later allow StationStaff work-order permissions and CustomerContact flight-view permissions without changing this MasterData catalog.

Identity validates Roles against the combined permission catalogs registered by all modules. This is implemented through the shared permission catalog/registry contract, so each module contributes its own permission definitions and UserType compatibility.

## 15. Linked Email Changes

### Decided

- Changing the email of an unlinked StaffMember or CustomerContact updates MasterData only.
- Changing the email of a linked StaffMember or CustomerContact starts an Identity-owned email-change and reverification workflow.
- After successful reverification, the new address becomes the Identity login email.
- The new email must satisfy Identity validation and global uniqueness rules.

### Proposed safe verification sequence

- Identity sends a verification message to the new address.
- The Identity login email changes only after the new address is successfully verified.
- Until verification succeeds, the previous verified email remains the login identifier. This prevents a typo or undeliverable address from locking the User out.
- MasterData must expose that an email change is pending and must not claim that the linked login email has changed before Identity confirms it.
- Identity publishes a confirmation event after verification so MasterData can finalize the email and clear the pending state.
- Repeated delivery and retries must be idempotent.

## 16. Removal And Lifecycle Propagation

### Decided

- A Customer Contact may be removed from the Customer.
- Removing a linked Customer Contact blocks its data-scope resolution immediately, deactivates the linked User through an integration event, and revokes active sessions.
- The Identity User is retained in a deactivated state for security history and audit; it is not hard deleted with the contact.
- Permanently removing a CustomerContact detaches the retained Identity User and releases its login email so the address may be used by a different MasterData identity.
- Reusing an email never reactivates or relinks the old User; Identity creates a new invited User with a new ID.
- Deactivating a Customer, Station, StaffMember, or Contact without permanently removing it does not release the email because the source record and account may later be restored.
- Identity preserves historical Users while allowing permanently released login emails to be reused by a new MasterData identity.
- Deactivating an individual StaffMember deactivates the linked User and revokes active sessions.
- Deactivating a Customer blocks access for all linked contacts.
- Deactivating a Station blocks access for all linked StaffMembers.
- Reactivation does not silently reactivate accounts; an administrator explicitly restores portal access.

These operations cross module boundaries and therefore use integration events with outbox/inbox idempotency.

Access checks fail closed: an active Identity account cannot gain scoped access when its linked StaffMember/contact or parent Station/Customer is missing or inactive, even while an integration event is still being processed.

## 17. Resolved Design Decisions

The initial discussion resolved the following:

1. The individual workforce entity is `StaffMember`.
2. The system has three fixed UserTypes with permission-bearing compatible Roles.
3. An administrator selects a compatible Role when requesting portal access.
4. Portal access can be requested at initial creation or later.
5. A linked email change is coordinated with Identity, changes the login email, and requires reverification.
6. Customer Contacts may be removed; linked Identity accounts are deactivated and retained for audit.
7. Deactivating a Station or Customer blocks its linked Users.
8. A StaffMember belongs to one Station in v1.0.0.
9. A Customer Contact belongs to one Customer in v1.0.0.
10. Country is implemented before Station.
11. ManpowerType is implemented before StaffMember.
12. Station IATA/ICAO and Customer IATA/ICAO codes are backend-normalized to uppercase. Station IATA is required and unique, Station/Customer ICAO is optional and unique when supplied, and Customer IATA is optional and non-unique.
13. StationStaff and CustomerContact accounts have no automatically assigned default Role; the administrator must select a compatible Role.
14. A permanently removed contact's released email may be reused for a new MasterData identity and a new User.
15. The first usable release includes complete Country, ManpowerType, License, Service, OperationType, AircraftType, Tool, Material, GeneralSupport, Station, Customer, CustomerContact, and StaffMember management across backend and Blazor UI.
16. StaffMember requires Station and ManpowerType, supports an optional legacy day-based WorkingSchedule, and supports zero-or-more legacy-style License assignments.
17. StaffMember employment-contract dates are optional; when present, the period must be valid.
18. Countries are seeded from the ISO list and remain administrator-maintainable without startup seeding overwriting edits.
19. MasterData permissions and their UserType compatibility follow the catalog/matrix above; StationStaff and CustomerContact roles operate only within their linked scope.
20. Customer Address uses the revised fields defined above, including optional state/province/region.
21. Customer has a required Country and required official Address; the Address uses the Customer Country rather than duplicating Country ID.
22. Contacts may be created with a Customer or later from Customer details; StaffMembers may be created with a Station, from Station details, or from a top-level Staff Members screen.
23. StaffMember/CustomerContact creation optionally provisions an invited User when the administrator selects the portal-user option and a compatible Role.
24. Invitation delivery is configuration-driven, disabled by default, resendable after failure, and never stores SMTP secrets in tracked configuration.
25. MasterData lists, forms, dialogs, navigation, and client architecture must match the existing Users/Roles template; detail pages intentionally introduce an improved, reusable detail-page UI/UX pattern.

## 18. Remaining Questions

There are no unresolved business-scope questions blocking the initial MasterData implementation. Technical workflows still marked **Proposed** must be finalized when planning their implementation slices.

## 19. Implementation Gate

Country, ManpowerType, and License are prerequisite catalog slices. Station and Customer follow Country. StaffMember follows Station, ManpowerType, and License. Services, OperationTypes, AircraftTypes, Tools, Materials, and GeneralSupports are catalog slices consumed by future Contracts/Operations workflows. Portal-access integration depends on the Identity changes (`UserType`, role compatibility, external reference, email reverification, released-email semantics, and integration messaging), which are now implemented in the MVP.
