# Migration Inventory

Single source of truth for the gradual rewrite from the old Blazor OperationsSystem (under `legacy/`) to v1.0.0.

Update a row whenever a feature's status changes. Status values: `not-started`, `in-progress`, `done`, `verified` (behavior confirmed against the old project).

## How To Use

1. When starting a feature, add/locate its row and set status to `in-progress`.
2. Record the old reference files you used.
3. When the v1.0.0 feature is complete and tested, set `done`.
4. After comparing behavior against the old project, set `verified`.

## Modules

Replace these placeholder rows with the real features as you discover them in `legacy/`.

### Identity

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Authentication (login, refresh) | not-started | | | |
| Users | not-started | | | |
| Roles and permissions | not-started | | | |

### Core (reference/master data)

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| ManpowerTypes | not-started | | | |

### Operations

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Work orders | not-started | | | |
| Flights | not-started | | | |

### Contracts

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| Contracts | not-started | | | |

### Store

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| | not-started | | | |

### Notifications

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| | not-started | | | |

### Audit

| Feature | Status | Old reference files | New location | Notes |
|---|---|---|---|---|
| | not-started | | | |
