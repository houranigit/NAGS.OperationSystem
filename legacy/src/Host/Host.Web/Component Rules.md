# Feature standards — reference: Customer

End-to-end conventions for new **Core** features consumed by **Host.Web**. The **Customer** vertical slice (`Customer` aggregate + `Components/Pages/Customers` + matching queries/commands) is the **golden reference**. Other features must align unless there is a documented, reviewed reason.

Also read **`OperationsManager/CLAUDE.md`** for solution-wide architecture (CQRS, domain rules, outbox, cross-module Contracts-only references).

---

## Golden reference (copy targets)

| Layer | Reference path |
|------|----------------|
| **Host.Web UI** | `Components/Pages/Customers/` — `_Index.razor`, `CustomersGrid.razor`, `Dialog/CustomerAddDialog.razor`, `Dialog/CustomerUpdateDialog.razor`, `Dialog/sections/` |
| **Grid + dialog CSS** | `settings-wizard-*`, `settings-grid-add-btn`, `settings-grid-edit-btn`, `settings-field*` (see sections below) |
| **Paginated grid query** | `Core.Application` → `Features/Customer/Queries/GetPaginatedCustomers/GetPaginatedCustomersQueryHandler.cs` |
| **Paginated SelectOptions query** | `Core.Application` → `Features/Customer/Queries/GetPaginatedCustomerSelectOptions/GetPaginatedCustomerSelectOptionsQueryHandler.cs` |
| **Create / update commands** | `Features/Customer/Commands/CreateCustomer`, `UpdateCustomer` — handler orchestration, validators, no `SaveChanges` in handler |
| **Domain** | `Core.Domain/Aggregates/Customer/` — `Result<T>` mutators, no repository injection in entities |
| **EF configuration** | `Core.Infrastructure/Persistence/Configurations/CustomerConfiguration.cs` — `core` schema, conversions, indexes |

---

## A. Host.Web — feature UI

### A1. Feature folder layout

Each feature lives under `Components/Pages/{FeatureName}/`.

| Item | Purpose |
|------|---------|
| `_Index.razor` | Route entry (`@page`). Thin shell only. |
| Data page | `*Grid.razor` (or list/scheduler) — owns data load and opening dialogs. |

**Reference:** `Customers/_Index.razor` → `CustomersGrid.razor`.

### A2. Dialog folder layout

`Components/Pages/{FeatureName}/Dialog/`

| Item | Purpose |
|------|---------|
| `sections/` | Sections shared by Add and Update. |
| `{Feature}AddDialog.razor` | **Wizard** — step order, validate before Next, `JumpTo` / `_furthestReached`. |
| `{Feature}UpdateDialog.razor` | **Free navigation** — any section from sidebar; same `settings-wizard-*` shell. |
| `{Feature}FormModel.cs` | UI model; maps to/from commands/DTOs. |

Match **Customer** dialog structure: `RadzenTemplateForm`, `settings-wizard-host`, sidebar step buttons, footer `settings-wizard-btn`, **Saving…** with `_saving` + `await InvokeAsync(StateHasChanged)` before awaits and in `finally` after submit.

### A3. Add dialog = wizard

- **Next** validates the current step (`EditContext.Validate()`) before advancing.
- Sidebar: active / completed / upcoming; jump back only to steps already reached.
- Final step submits create command via `IScopedMediator` / `ScopedMediator`.
- While submitting: set `_saving` first, `InvokeAsync(StateHasChanged)`, then validate all steps if needed, then send command (same order as `CustomerAddDialog`).

### A4. Update dialog = section navigation

- User may open any section without completing others.
- Dirty tracking and unsaved confirm — mirror `CustomerUpdateDialog`.
- Disable sidebar while `_saving`; same **Saving…** pattern as Add.

### A5. Dropdowns and lookups

1. Load multiple lookups with **`Task.WhenAll`** when there are several.
2. Each lookup uses a **`*SelectOptions`** query in Core.Application (paginated).
3. Per-field loading flag + skeleton where appropriate (`AddressSection` / `DropdownFieldSkeleton`).

**Bind to Contracts types:** `Data` = `IReadOnlyList<{Entity}SelectOption>`; display text on the Contracts type (`DisplayLabel` or equivalent); `TextProperty` / `ValueProperty` with `nameof(...)`. No parallel “display only” projection lists in the UI.

### A6. Dialog sections — typography

- No extra `<h2>` / page titles inside sections that duplicate the wizard sidebar step.
- Use `settings-field__label`, `settings-field`, `settings-field__row2` like **Company** / **Address** and other master-data `BasicDetailsSection` screens.

### A7. UI replication checklist

1. `Pages/{Feature}/` with `_Index.razor` + grid (or primary surface).
2. `Dialog/` + `sections/`; Add = wizard, Update = navigation; copy **Customer** shell.
3. Grid uses `settings-grid-add-btn` / `settings-grid-edit-btn` and dialog width `min(56rem, 95vw)` unless the feature truly needs a different width.
4. Scoped `*.razor.css` for wizard and grid buttons when not reusing identical class names from the same component.

---

## B. Core.Application — queries and handlers

### B1. Paginated pipeline (mandatory for grids and SelectOptions)

**Wrong:** `ToListAsync()` on the full `DbSet` (or multiple tables), then `Skip`/`Take` in memory, or `AsQueryable()` on a materialized list.

**Right:** One **`IQueryable`** chain through filter → count → order → **`Skip`/`Take`** → **`Select`** projection → **`ToListAsync`** once at the end.

| Use case | Reference handler |
|----------|-------------------|
| Full data grid | `GetPaginatedCustomersQueryHandler.cs` |
| Dropdown / `*SelectOptions` | `GetPaginatedCustomerSelectOptionsQueryHandler.cs` |

**Pipeline order:**

1. `db.{Set}.AsQueryable()` (optional baseline `Where`, e.g. `IsActive` for lookups).
2. Optional `Where(FilterQuery)` — filters use **entity** property names (Radzen dynamic filter strings).
3. `Count()` for total (still server-side for EF `IQueryable`).
4. `OrderBy` — dynamic `OrderByQuery` or a sensible default consistent with the grid.
5. `Skip` / `Take`.
6. `Select` → Contracts DTO / `*SelectOption`.
7. `ToListAsync(cancellationToken)` **once**.

If a row needs columns from another table, **`Join`** (or `Include` + projection) so that paging still applies to the correct shape — see `GetPaginatedEmployeeSelectOptionsQueryHandler` when it follows this pattern.

Handlers that load large graphs into memory before paging are **not** templates; refactor them to this pipeline.

Document new grid/SelectOptions handlers with a short `<summary>` / `<remarks>` pointing to **`GetPaginatedCustomersQueryHandler`** (same style as that file).

### B2. Commands

- Handlers orchestrate: load aggregate → call domain method → persist via repository; **`TransactionBehavior`** performs `SaveChanges` — **never** call `SaveChanges` inside the handler.
- **Child collections:** full snapshot on create/update; map to one aggregate sync method — mirror **Customer contacts** / `SyncContacts` (see `CLAUDE.md`).

### B3. Application replication checklist

- [ ] `GetPaginated{Entity}` and `GetPaginated{Entity}SelectOptions` (if used from UI) follow **B1**.
- [ ] No full-table materialization before paging.
- [ ] XML remarks reference the Customer handler pair where helpful.

---

## C. Core.Domain

- Business rules live on aggregates and value objects; mutators return **`Result`** / **`Result<T>`**.
- **Do not** inject repositories or `DbContext` into domain entities.
- Commands/handlers **double-dispatch**: handler loads data, passes into aggregate methods.
- Naming: see **`CLAUDE.md`** (commands, events, repositories, value objects).

**Reference aggregate:** `Core.Domain/Aggregates/Customer/`.

---

## D. Core.Infrastructure — EF and composition

- **Schema:** module tables live in the **`core`** schema (see `CustomerConfiguration`, `AircraftTypeConfiguration`).
- **Configuration class per aggregate** (or owned child): `IEntityTypeConfiguration<T>`, explicit conversions for strongly typed ids, uniqueness indexes where the domain requires them.
- **Repositories** are thin persistence adapters; **no** business rules.
- **Readers** (read models / snapshots for other modules) stay projection-focused and should not replace paginated queries for grids.

---

## E. Cross-cutting review checklist (before merge)

Use this as a self-review or PR checklist for any new “master data” or CRUD feature:

| Area | Question |
|------|----------|
| UI | Does the grid + Add/Update flow mirror **Customers** (`settings-wizard-*`, save/dispose patterns)? |
| UI | Are dropdowns fed by **SelectOptions** queries and Contracts DTOs only? |
| Queries | Is paging done **entirely** on `IQueryable` with a **single** terminal `ToListAsync`? |
| Commands | No `SaveChanges` in handlers; validators registered; child collections synced like Customer where applicable? |
| Domain | Rules on aggregates; `Result` returns; no infrastructure in domain? |
| Infrastructure | EF configuration in **`core`** schema; id conversions and indexes consistent with invariants? |

---

*Extend this file when a new cross-cutting pattern is introduced; keep the Customer reference links accurate.*
