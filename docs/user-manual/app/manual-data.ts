export type ManualAudience = "all" | "portal" | "mobile" | "administrator";

export type ManualCalloutTone = "info" | "tip" | "warning" | "success";

export type LifecycleTone =
  | "neutral"
  | "info"
  | "warning"
  | "success"
  | "danger";

export interface ManualStep {
  id: string;
  title: string;
  body: string;
}

export interface ManualCallout {
  tone: ManualCalloutTone;
  title: string;
  body: string;
}

export interface ManualContentNode {
  id: string;
  title: string;
  eyebrow: string;
  summary: string;
  keywords: readonly string[];
  steps: readonly ManualStep[];
  callouts: readonly ManualCallout[];
  screenshotKey?: string;
}

export interface ManualSection extends ManualContentNode {
  categoryIds: readonly string[];
}

export interface ManualCategory extends ManualContentNode {
  sectionId: string;
  articleIds: readonly string[];
}

export interface ManualArticle extends ManualContentNode {
  sectionId: string;
  categoryId: string;
  audience: ManualAudience;
  location?: string;
}

export interface LifecycleStatusDefinition {
  id: string;
  label: string;
  summary: string;
  enteredWhen: string;
  availableActions: readonly string[];
  nextStatusIds: readonly string[];
  tone: LifecycleTone;
}

export interface MobileSyncStatusDefinition {
  id: string;
  label: string;
  summary: string;
  operatorAction: string;
  tone: LifecycleTone;
}

export interface MasterDataMatrixRow {
  id: string;
  record: string;
  location: string;
  keyFields: readonly string[];
  usedBy: readonly string[];
  controls: readonly string[];
  screenshotKey?: string;
}

export const manualSections = [
  {
    id: "get-started",
    title: "Get started",
    eyebrow: "Orientation",
    summary:
      "Learn the shared operating rules, find the right record, and understand what your access allows.",
    keywords: ["quick start", "sign in", "search", "filters", "UTC", "access"],
    steps: [
      {
        id: "choose-surface",
        title: "Choose your surface",
        body: "Use the portal for scheduling, approval, reporting, and master data. Use mobile for ramp execution and offline work.",
      },
      {
        id: "confirm-context",
        title: "Confirm your context",
        body: "Check the station, date range, and status filters before changing a flight or work order.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Times are UTC",
        body: "Operational date and time fields are shown and entered in UTC unless a screen explicitly says otherwise.",
      },
    ],
    categoryIds: ["quick-start"],
  },
  {
    id: "access-and-oversight",
    title: "Access & oversight",
    eyebrow: "Roles, dashboards & reports",
    summary:
      "Give each persona only the pages, actions, and station scope needed for its work, then monitor and export the result.",
    keywords: [
      "roles",
      "permissions",
      "System Administrator",
      "Dispatcher",
      "Station Operator",
      "dashboard",
      "reports",
      "print work order",
    ],
    steps: [
      {
        id: "choose-persona",
        title: "Start with a persona",
        body: "Use the System Administrator, Dispatcher, and Station Operator patterns as a small, understandable access baseline.",
      },
      {
        id: "grant-action",
        title: "Grant the exact actions",
        body: "A visible page does not grant every action on that page. Add create, update, assignment, approval, export, or identity permissions deliberately.",
      },
      {
        id: "verify-scope",
        title: "Verify effective scope",
        body: "Test both what the user can do and which station, assigned flights, and other users’ work orders they can see.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "The API is authoritative",
        body: "Hidden buttons improve the interface, but every protected request is checked again by the server.",
      },
    ],
    categoryIds: ["roles-permissions", "dashboards-reporting"],
  },
  {
    id: "flight-operations",
    title: "Flight operations",
    eyebrow: "Portal",
    summary:
      "Schedule, find, update, merge, and follow a flight from Scheduled to its final state.",
    keywords: [
      "flight",
      "schedule",
      "calendar",
      "status",
      "duplicate",
      "merge",
      "portal",
    ],
    steps: [
      {
        id: "schedule",
        title: "Schedule",
        body: "Create the flight and assign its planned services and staff.",
      },
      {
        id: "execute",
        title: "Execute",
        body: "A submitted work order moves the flight into active execution.",
      },
      {
        id: "settle",
        title: "Settle",
        body: "Approval completes or cancels the flight; a return reopens it for correction.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Status changes are linked",
        body: "Flight and work-order statuses settle together. Do not treat either record as an independent checklist.",
      },
    ],
    categoryIds: ["portal-flight-lifecycle"],
  },
  {
    id: "work-orders",
    title: "Work orders",
    eyebrow: "Portal",
    summary:
      "Capture performed work, approve evidence, return corrections, and consolidate compatible work orders.",
    keywords: [
      "work order",
      "completion",
      "cancellation",
      "approve",
      "return",
      "merge",
      "portal",
    ],
    steps: [
      {
        id: "capture",
        title: "Capture",
        body: "Record flight facts, performed services, tasks, resources, attachments, and signature.",
      },
      {
        id: "review",
        title: "Review",
        body: "Check submitted or returned work against the flight before approval.",
      },
      {
        id: "close",
        title: "Close or correct",
        body: "Approve to settle the record, or return an approved record with a clear correction reason.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Two work-order types",
        body: "Completion records performed work. Cancellation records the cancellation time and reason.",
      },
    ],
    categoryIds: ["portal-work-order-lifecycle"],
  },
  {
    id: "mobile-operations",
    title: "Mobile operations",
    eyebrow: "Ramp execution",
    summary:
      "Open an assigned flight, capture work locally, and confirm that queued work reaches the server.",
    keywords: [
      "mobile",
      "my flights",
      "draft",
      "offline",
      "outbox",
      "sync center",
      "conflict",
    ],
    steps: [
      {
        id: "open-flight",
        title: "Open the flight",
        body: "Find the flight in My Flights, Per Landing, or Ad Hoc, then open its action sheet.",
      },
      {
        id: "record-work",
        title: "Record work",
        body: "Complete the seeded service lines, tasks, resources, evidence, and signature.",
      },
      {
        id: "confirm-sync",
        title: "Confirm delivery",
        body: "Use Sync Center to verify that no pending, failed, or conflicting submission remains.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Queued is not accepted",
        body: "An offline submission stays on that device until the server accepts it.",
      },
    ],
    categoryIds: ["mobile-work-order-lifecycle"],
  },
  {
    id: "special-operations",
    title: "Special operations",
    eyebrow: "Per Landing & Ad Hoc",
    summary:
      "Handle contract-based Per Landing flights and unscheduled Ad Hoc work without mixing their rules.",
    keywords: [
      "per landing",
      "ad hoc",
      "extraction",
      "on call",
      "unscheduled",
    ],
    steps: [
      {
        id: "identify-mode",
        title: "Identify the operating mode",
        body: "Confirm whether the flight is Per Landing or Ad Hoc before creating work.",
      },
      {
        id: "apply-rule",
        title: "Apply its rule",
        body: "Per Landing can be settled by extraction when no individual performed service is recorded; Ad Hoc captures real unscheduled work.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Per Landing is not a performed service",
        body: "Never add Aircraft Per Landing as a service line on a work order.",
      },
    ],
    categoryIds: ["per-landing-ad-hoc"],
  },
  {
    id: "master-data",
    title: "Master data",
    eyebrow: "Administration",
    summary:
      "Maintain the controlled catalogs that drive stations, customers, staffing, scheduling, and work-order entry.",
    keywords: [
      "master data",
      "country",
      "staff",
      "customer",
      "station",
      "service",
      "tool",
      "material",
      "aircraft",
    ],
    steps: [
      {
        id: "check-dependencies",
        title: "Check dependencies",
        body: "Find where a record is already used before changing its code, qualification, or availability.",
      },
      {
        id: "maintain-record",
        title: "Maintain the record",
        body: "Use clear names, valid codes, and complete relationships so operational forms remain usable.",
      },
      {
        id: "retire-safely",
        title: "Retire safely",
        body: "Deactivate obsolete records when available so historical flights and work orders remain readable.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Permission controlled",
        body: "Menus and actions appear only when your role includes the required master-data permission.",
      },
    ],
    categoryIds: [
      "master-locations",
      "master-people",
      "master-service-catalog",
      "master-resources",
      "master-business-access",
    ],
  },
] as const satisfies readonly ManualSection[];

export const manualCategories = [
  {
    id: "quick-start",
    sectionId: "get-started",
    title: "Quick start",
    eyebrow: "First five minutes",
    summary:
      "Orient yourself, search efficiently, and avoid the most common time and permission mistakes.",
    keywords: ["navigation", "search", "filters", "refresh", "permissions", "UTC"],
    steps: [
      {
        id: "open-home",
        title: "Open your home view",
        body: "Use the portal operations area or the mobile flight tabs that match your assignment.",
      },
      {
        id: "narrow-list",
        title: "Narrow the list",
        body: "Search first, then add station, customer, status, operation type, or date filters.",
      },
    ],
    callouts: [],
    articleIds: ["quick-start-orientation", "find-and-filter"],
  },
  {
    id: "roles-permissions",
    sectionId: "access-and-oversight",
    title: "Roles and permissions",
    eyebrow: "Three-persona baseline",
    summary:
      "Separate global administration, station dispatch, and ramp execution without creating a role for every small variation.",
    keywords: [
      "role",
      "permission",
      "scope",
      "System Administrator",
      "Dispatcher",
      "Station Operator",
    ],
    steps: [
      {
        id: "map-persona",
        title: "Map the persona",
        body: "Choose the account type and station link first, then add the smallest permission set that supports the job.",
      },
      {
        id: "test-positive-negative",
        title: "Test allowed and blocked actions",
        body: "Confirm the persona can complete its own workflow and is blocked from administration or other stations where appropriate.",
      },
    ],
    callouts: [],
    articleIds: ["role-personas-permissions"],
  },
  {
    id: "dashboards-reporting",
    sectionId: "access-and-oversight",
    title: "Dashboards and reporting",
    eyebrow: "Monitor, export & print",
    summary:
      "Use the landing dashboard for operational shortcuts and the analytics dashboard for filtered performance views and exports.",
    keywords: ["dashboard", "analytics", "report", "export", "xlsx", "csv", "pdf", "print WO"],
    steps: [
      {
        id: "monitor",
        title: "Monitor the right view",
        body: "Use the landing dashboard for current workload and the Operations dashboard for filtered trends and distributions.",
      },
      {
        id: "export-print",
        title: "Export or print",
        body: "Export the active flight result set, or print the approved work order attached to a completed flight.",
      },
    ],
    callouts: [],
    articleIds: ["operations-dashboards", "reports-and-printing"],
  },
  {
    id: "portal-flight-lifecycle",
    sectionId: "flight-operations",
    title: "Portal flight lifecycle",
    eyebrow: "Schedule to settlement",
    summary:
      "Create a sound schedule, monitor every state change, and resolve duplicates without losing history.",
    keywords: ["Scheduled", "InProgress", "Completed", "Canceled", "Merged"],
    steps: [
      {
        id: "create-flight",
        title: "Create the flight",
        body: "Enter its operating details, planned services, and eligible staff.",
      },
      {
        id: "follow-state",
        title: "Follow the state",
        body: "Use the flight detail timeline and work-order tab to understand every transition.",
      },
    ],
    callouts: [],
    articleIds: [
      "portal-schedule-flight",
      "portal-invite-staff",
      "portal-flight-status-actions",
      "portal-resolve-duplicates",
    ],
  },
  {
    id: "portal-work-order-lifecycle",
    sectionId: "work-orders",
    title: "Portal work-order lifecycle",
    eyebrow: "Submit, approve, correct",
    summary:
      "Create Completion or Cancellation work orders, review evidence, and control final approval.",
    keywords: ["Submitted", "Returned", "Approved", "Merged", "approval number"],
    steps: [
      {
        id: "submit-work",
        title: "Submit the work",
        body: "Capture complete operational evidence before sending the record for approval.",
      },
      {
        id: "approve-or-return",
        title: "Approve or return",
        body: "Approve valid work; return an approved record only when a correction is required.",
      },
    ],
    callouts: [],
    articleIds: [
      "portal-completion-work-order",
      "portal-cancellation-work-order",
      "portal-approve-return-merge",
    ],
  },
  {
    id: "mobile-work-order-lifecycle",
    sectionId: "mobile-operations",
    title: "Mobile work-order lifecycle",
    eyebrow: "Online and offline",
    summary:
      "Execute assigned work from a locally cached flight and monitor delivery through Sync Center.",
    keywords: ["mobile", "drafts", "offline", "outbox", "retry", "conflict"],
    steps: [
      {
        id: "select-flight",
        title: "Select a flight",
        body: "Open the flight card and choose the required operational action.",
      },
      {
        id: "submit-locally",
        title: "Save or submit",
        body: "Keep unfinished work as a local draft, or submit it to the outbox for delivery.",
      },
      {
        id: "watch-delivery",
        title: "Watch delivery",
        body: "Resolve failed or conflicting queued work before leaving the device.",
      },
    ],
    callouts: [],
    articleIds: [
      "mobile-find-flight",
      "mobile-create-work-order",
      "mobile-offline-sync",
      "mobile-return-cancel",
    ],
  },
  {
    id: "per-landing-ad-hoc",
    sectionId: "special-operations",
    title: "Per Landing and Ad Hoc",
    eyebrow: "Special flight modes",
    summary:
      "Use the dedicated portal and mobile flows for contract extraction and unscheduled service.",
    keywords: ["per landing", "ad hoc", "bulk approval", "extraction", "scratch flight"],
    steps: [
      {
        id: "choose-flow",
        title: "Choose the correct flow",
        body: "Use Per Landing only for flights designated by the contract; use Ad Hoc for unscheduled work.",
      },
      {
        id: "review-result",
        title: "Review the result",
        body: "Confirm the resulting work-order number, flight state, and any excluded record.",
      },
    ],
    callouts: [],
    articleIds: ["compare-flight-modes", "per-landing-operations", "ad-hoc-operations"],
  },
  {
    id: "master-locations",
    sectionId: "master-data",
    title: "Locations",
    eyebrow: "Countries & stations",
    summary:
      "Maintain valid geographic codes and the stations that scope operational work.",
    keywords: ["country", "ISO", "station", "IATA", "ICAO", "city"],
    steps: [
      {
        id: "maintain-country",
        title: "Maintain the country",
        body: "Create the country before using it on a station or customer.",
      },
      {
        id: "maintain-station",
        title: "Maintain the station",
        body: "Enter unique station codes and connect the station to its country.",
      },
    ],
    callouts: [],
    articleIds: ["master-countries", "master-stations"],
  },
  {
    id: "master-people",
    sectionId: "master-data",
    title: "People and qualifications",
    eyebrow: "Manpower & licenses",
    summary:
      "Define the qualifications that control who can perform and be assigned to operational work.",
    keywords: ["manpower", "license", "qualification", "allowed services"],
    steps: [
      {
        id: "define-qualification",
        title: "Define the qualification",
        body: "Create manpower types and licenses with stable, recognizable names and codes.",
      },
      {
        id: "link-services",
        title: "Link allowed services",
        body: "Limit manpower types to the services they are qualified to perform.",
      },
    ],
    callouts: [],
    articleIds: ["master-manpower-licenses"],
  },
  {
    id: "master-service-catalog",
    sectionId: "master-data",
    title: "Service catalog",
    eyebrow: "Services & operation types",
    summary:
      "Control the selectable services and operating modes used by flights and work orders.",
    keywords: ["service", "operation type", "Per Landing", "Ad Hoc"],
    steps: [
      {
        id: "define-service",
        title: "Define the service",
        body: "Use a clear name and description, then link eligible manpower.",
      },
      {
        id: "define-operation",
        title: "Define the operation type",
        body: "Use operation types to describe how a flight is handled, not what was performed.",
      },
    ],
    callouts: [],
    articleIds: ["master-services-operation-types", "master-system-records"],
  },
  {
    id: "master-resources",
    sectionId: "master-data",
    title: "Aircraft and resources",
    eyebrow: "Execution catalogs",
    summary:
      "Maintain aircraft types and the tools, materials, and general supports recorded against tasks.",
    keywords: [
      "aircraft",
      "tool",
      "equipment",
      "material",
      "general support",
      "calibration",
    ],
    steps: [
      {
        id: "create-catalog-item",
        title: "Create the catalog item",
        body: "Enter identifiers that let ramp staff select the correct resource without guesswork.",
      },
      {
        id: "keep-current",
        title: "Keep it current",
        body: "Update descriptive or calibration information without breaking historical records.",
      },
    ],
    callouts: [],
    articleIds: ["master-aircraft-resources"],
  },
  {
    id: "master-business-access",
    sectionId: "master-data",
    title: "Business partners and access",
    eyebrow: "Customers, staff & portal",
    summary:
      "Maintain customer identity, staff employment data, station assignment, and portal access.",
    keywords: [
      "customer",
      "staff",
      "employee",
      "contract",
      "schedule",
      "portal access",
      "invitation",
    ],
    steps: [
      {
        id: "maintain-partner",
        title: "Maintain the customer",
        body: "Keep official codes and contacts complete so flights and reports are attributable.",
      },
      {
        id: "maintain-user",
        title: "Maintain the staff member",
        body: "Set station, manpower, dates, schedule, licenses, and portal access deliberately.",
      },
    ],
    callouts: [],
    articleIds: [
      "master-customers",
      "master-staff",
      "master-portal-access",
    ],
  },
] as const satisfies readonly ManualCategory[];

export const manualArticles = [
  {
    id: "quick-start-orientation",
    sectionId: "get-started",
    categoryId: "quick-start",
    audience: "all",
    title: "Know where work happens",
    eyebrow: "Quick start",
    summary:
      "Use the portal to plan and control operations; use mobile to execute assigned ramp work.",
    keywords: ["portal", "mobile", "navigation", "permissions", "UTC"],
    location: "Portal home or mobile flight tabs",
    steps: [
      {
        id: "portal-purpose",
        title: "Use the portal for control",
        body: "Schedule flights, review timelines, approve work orders, run Per Landing extraction, and maintain master data.",
      },
      {
        id: "mobile-purpose",
        title: "Use mobile for execution",
        body: "Open assigned or special flights, record work, save local drafts, and monitor queued submissions.",
      },
      {
        id: "check-access",
        title: "Check your access",
        body: "If a menu or action is missing, confirm your role and station assignment with an administrator.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "UTC operational time",
        body: "Read and enter scheduled, actual, cancellation, and task times in UTC where marked.",
      },
      {
        tone: "tip",
        title: "Refresh before deciding",
        body: "Refresh the current view when another operator may have just changed the record.",
      },
    ],
    screenshotKey: "quick-start-surfaces",
  },
  {
    id: "find-and-filter",
    sectionId: "get-started",
    categoryId: "quick-start",
    audience: "all",
    title: "Find the right record",
    eyebrow: "Search & filters",
    summary:
      "Combine free-text search with status, station, customer, type, and date filters.",
    keywords: [
      "search",
      "filter",
      "flight number",
      "customer",
      "station",
      "date range",
      "export",
    ],
    location: "Portal → Operations → Flights or Work Orders",
    steps: [
      {
        id: "search-first",
        title: "Search first",
        body: "Enter the flight number, customer, or known record identifier.",
      },
      {
        id: "narrow-results",
        title: "Add filters",
        body: "Narrow by station, customer, status, operation type, service category, or From/To date as available.",
      },
      {
        id: "clear-stale-filters",
        title: "Clear stale filters",
        body: "If an expected record is missing, reset filters and check the date window before escalating.",
      },
    ],
    callouts: [
      {
        tone: "tip",
        title: "Export the filtered set",
        body: "The portal flight list can export the current result set to XLSX, CSV, or PDF.",
      },
    ],
    screenshotKey: "portal-flight-filters",
  },
  {
    id: "role-personas-permissions",
    sectionId: "access-and-oversight",
    categoryId: "roles-permissions",
    audience: "administrator",
    title: "Use a three-persona permission model",
    eyebrow: "Roles and permissions",
    summary:
      "Keep access understandable with one global administrator, one station dispatcher pattern, and one station operator pattern.",
    keywords: [
      "System Administrator",
      "Dispatcher",
      "Station Operator",
      "role",
      "permissions",
      "account type",
      "station scope",
      "least privilege",
    ],
    location: "Portal → Administration → Roles and Users",
    steps: [
      {
        id: "system-administrator",
        title: "System Administrator",
        body: "Use the System Administrator account type for global administration. Separately, the protected seeded System Administrator role is synchronized to every known permission and cannot be edited or deleted.",
      },
      {
        id: "dispatcher",
        title: "Dispatcher",
        body: "Use a station-linked Station Staff account with flight view, station-wide view, schedule, update, assign, invite, dashboard, and reference-option permissions. Add export or work-order review only when the job requires them.",
      },
      {
        id: "station-operator",
        title: "Station Operator",
        body: "Use a station-linked Station Staff account with flight view and work-order authoring. The operator sees assigned regular flights plus the station’s Per Landing flights and works primarily in mobile; mobile limits the operational list to Scheduled, In Progress, or Completed flights inside the STA ±12-hour window.",
      },
      {
        id: "verify-access",
        title: "Verify the effective result",
        body: "Sign in as each demo persona and test page visibility, action visibility, station scope, assigned-flight scope, and one deliberately forbidden action.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Role names are a policy choice",
        body: "The three names in this guide are recommended demo personas. The system enforces account type, permission claims, and scope—not the display name itself.",
      },
      {
        tone: "info",
        title: "Account type is not a role",
        body: "The four account types are System Administrator, Station Staff, Customer Contact, and Viewer Only. A role is the compatible permission bundle assigned inside that account type.",
      },
      {
        tone: "warning",
        title: "Do not give assignment by accident",
        body: "Invite adds eligible teammates to one flight. Assign replaces the scheduled roster and is a broader dispatcher permission.",
      },
    ],
    screenshotKey: "portal-role-personas",
  },
  {
    id: "operations-dashboards",
    sectionId: "access-and-oversight",
    categoryId: "dashboards-reporting",
    audience: "portal",
    title: "Read the operations dashboards",
    eyebrow: "Dashboards and reporting",
    summary:
      "Use the home dashboard for current control and the Operations dashboard for filtered analytics in UTC.",
    keywords: [
      "dashboard",
      "operations dashboard",
      "analytics",
      "live connection",
      "flight status",
      "Per Landing vs On Call",
      "trends",
      "top customers",
      "top services",
    ],
    location: "Portal → Dashboard or Operations → Operations dashboard",
    steps: [
      {
        id: "home-dashboard",
        title: "Use the home dashboard for current work",
        body: "Review active master-data counts, live flight status, the flight board, recent work orders, and the Schedule flight, Create work order, and Refresh shortcuts.",
      },
      {
        id: "analytics-dashboard",
        title: "Open Operations dashboard for analysis",
        body: "Choose Today, Last month, Last 3 months, Max, or a custom day/range, then filter by station, customer, and performed service.",
      },
      {
        id: "read-distribution",
        title: "Read the distribution and trends",
        body: "Compare status totals, flights by station and operation type, Per Landing versus On Call, time trends, top customers, and top performed services.",
      },
      {
        id: "open-register",
        title: "Use the matching flight register",
        body: "The view-only table at the bottom follows the same filters. It does not open flight detail; use it to review the matching register, export when permitted, or download a printable completed work order.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Live and filter-aware",
        body: "Operational changes can refresh the dashboard in real time. Always read the active UTC date range and filters before interpreting totals.",
      },
      {
        tone: "info",
        title: "Two dashboards, two permissions",
        body: "The home Dashboard and the analytics Operations dashboard have separate view permissions. Export and Print WO on analytics also require the dashboard export permission.",
      },
    ],
    screenshotKey: "portal-operations-dashboard",
  },
  {
    id: "reports-and-printing",
    sectionId: "access-and-oversight",
    categoryId: "dashboards-reporting",
    audience: "portal",
    title: "Export reports and print a work order",
    eyebrow: "Dashboards and reporting",
    summary:
      "Generate a report from the active flight filters and print the approved work order from a completed flight.",
    keywords: [
      "report",
      "export",
      "Excel",
      "XLSX",
      "CSV",
      "PDF",
      "Print WO",
      "completed flight",
      "approved work order",
    ],
    location: "Portal → Operations → Flights or Operations dashboard",
    steps: [
      {
        id: "filter-report",
        title: "Build the report view",
        body: "Set the station, customer, status, operation, service category, and UTC date filters before exporting.",
      },
      {
        id: "choose-format",
        title: "Choose the export format",
        body: "Open Export and choose Excel workbook (.xlsx), CSV, or PDF report. The exported rows match the active result set.",
      },
      {
        id: "find-completed-flight",
        title: "Find the completed flight",
        body: "Print WO is available from the Flights list or Operations dashboard only for a Completed flight with an accessible Approved Completion work order. A Canceled flight or approved Cancellation cannot print.",
      },
      {
        id: "print-document",
        title: "Download the printable PDF",
        body: "Select Print WO to download work-order-{approval-number}.pdf. Review the generated document, then print it from your PDF viewer if a paper copy is required.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Printing is not a queue action",
        body: "The Work Orders page is the review queue. Print the final work order from the completed flight row after approval.",
      },
      {
        tone: "info",
        title: "Two separate permissions",
        body: "Flight-list export and Operations-dashboard export are distinct permissions and may be granted independently.",
      },
      {
        tone: "info",
        title: "There is no separate Reports page",
        body: "Current reporting is provided by the Flights export and Operations dashboard export menus.",
      },
    ],
    screenshotKey: "portal-report-print",
  },
  {
    id: "portal-schedule-flight",
    sectionId: "flight-operations",
    categoryId: "portal-flight-lifecycle",
    audience: "portal",
    title: "Schedule a flight",
    eyebrow: "Portal flight lifecycle",
    summary:
      "Create the flight, add its planned services, assign eligible staff, and review before saving.",
    keywords: [
      "schedule flight",
      "new flight",
      "planned services",
      "assigned staff",
      "calendar",
      "bulk",
    ],
    location: "Portal → Operations → Flights",
    steps: [
      {
        id: "enter-details",
        title: "Enter flight details",
        body: "Choose the customer, station, operation type, flight number, UTC schedule, aircraft details, and other required routing fields.",
      },
      {
        id: "add-services",
        title: "Add planned services",
        body: "Select the services expected for the flight. These seed the mobile work-order form.",
      },
      {
        id: "assign-staff",
        title: "Assign staff",
        body: "When the user has the assignment permission, choose station staff for normal scheduled work. The Staff step is permission-dependent.",
      },
      {
        id: "review-save",
        title: "Review and save",
        body: "Confirm the details, services, and assignments. A new flight starts as Scheduled.",
      },
      {
        id: "replace-roster",
        title: "Reassign the roster later",
        body: "Open Edit flight → Staff. Saving Assign replaces the full roster with the current selection, so removing every selection clears it.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Per Landing setup differs",
        body: "A Per Landing flight uses only the system Aircraft Per Landing designation and has no assigned staff at scheduling.",
      },
      {
        tone: "tip",
        title: "Use calendar bulk entry",
        body: "For repeated schedules, use the calendar bulk flow and review every generated date before saving.",
      },
      {
        tone: "warning",
        title: "Assign replaces; Invite adds",
        body: "Use Assign only when you intend to replace the scheduled roster. Use Invite employee to add teammates without removing existing assignments.",
      },
    ],
    screenshotKey: "portal-schedule-flight",
  },
  {
    id: "portal-invite-staff",
    sectionId: "flight-operations",
    categoryId: "portal-flight-lifecycle",
    audience: "all",
    title: "Invite staff to a scheduled flight",
    eyebrow: "Flight assignments",
    summary:
      "Add an eligible teammate to one regular flight from portal or mobile without replacing the dispatcher’s full roster.",
    keywords: [
      "invite staff",
      "invite teammates",
      "portal invite",
      "mobile invite",
      "flight assignment",
      "notification",
      "Scheduled",
    ],
    location: "Portal → Flight actions → Invite employee; Mobile → Flight actions → Invite teammates",
    steps: [
      {
        id: "check-eligibility",
        title: "Check the flight and permission",
        body: "Use Invite on a Scheduled, non-Per Landing flight. The inviter needs flight access and the flights.invite permission.",
      },
      {
        id: "open-invite",
        title: "Open Invite or Invite teammates",
        body: "Portal and mobile both load eligible active staff for the same station. Mobile invitation requires an online connection.",
      },
      {
        id: "select-teammates",
        title: "Select new teammates",
        body: "Choose staff who are active, belong to the same station, and are not already assigned. The current invite candidate check does not evaluate contracts, licenses, or manpower qualifications.",
      },
      {
        id: "confirm-result",
        title: "Confirm assignment and notification",
        body: "Submit the invitation, refresh the flight roster, and verify that the invited teammate receives the flight notification.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Scheduled is the reliable server rule",
        body: "The mobile action sheet can currently show Invite teammates on an In Progress flight, but the server accepts roster changes only while the flight is Scheduled.",
      },
      {
        tone: "info",
        title: "Invite and Assign are different",
        body: "Invite adds only the selected people. Assign is the dispatcher action that replaces or clears the scheduled roster.",
      },
      {
        tone: "warning",
        title: "Per Landing has no assigned list",
        body: "Do not invite or assign staff to a Per Landing flight; it is visible station-wide to eligible operators.",
      },
      {
        tone: "info",
        title: "Flight invite is not account access",
        body: "To give an existing staff member portal access, open Master Data → Staff Members → the staff record → Portal access → Grant access. Mobile has no account-administration screen.",
      },
    ],
    screenshotKey: "portal-invite-staff",
  },
  {
    id: "portal-flight-status-actions",
    sectionId: "flight-operations",
    categoryId: "portal-flight-lifecycle",
    audience: "portal",
    title: "Follow the flight lifecycle",
    eyebrow: "Portal flight lifecycle",
    summary:
      "Use status, work orders, timeline, and history together to understand the flight’s current state.",
    keywords: [
      "Scheduled",
      "InProgress",
      "Completed",
      "Canceled",
      "Merged",
      "timeline",
      "history",
    ],
    location: "Portal → Operations → Flights → Flight details",
    steps: [
      {
        id: "open-detail",
        title: "Open flight details",
        body: "Review Overview, Services & staff, Work orders, Timeline, and History.",
      },
      {
        id: "interpret-transition",
        title: "Interpret the transition",
        body: "Submission starts execution. Approval of a Completion or Cancellation work order settles the flight.",
      },
      {
        id: "return-correction",
        title: "Return for correction",
        body: "Return an approved work order when correction is required; the flight reopens as In Progress.",
      },
    ],
    callouts: [
      {
        tone: "success",
        title: "Approval settles both records",
        body: "Completion approval makes the flight Completed. Cancellation approval makes it Canceled.",
      },
      {
        tone: "info",
        title: "Actions depend on state",
        body: "Edit schedule and invite actions apply before settlement; print and return actions appear on settled records when permitted.",
      },
    ],
    screenshotKey: "portal-flight-detail",
  },
  {
    id: "portal-resolve-duplicates",
    sectionId: "flight-operations",
    categoryId: "portal-flight-lifecycle",
    audience: "portal",
    title: "Resolve duplicate flights",
    eyebrow: "Portal flight lifecycle",
    summary:
      "Keep the correct flight as the survivor and mark the duplicate as Merged.",
    keywords: ["duplicate", "merge flight", "survivor", "Merged"],
    location: "Portal → Operations → Flights → Resolve duplicates",
    steps: [
      {
        id: "compare-records",
        title: "Compare the records",
        body: "Check customer, flight number, station, UTC schedule, services, staff, and work-order activity.",
      },
      {
        id: "select-survivor",
        title: "Select the survivor",
        body: "Choose the flight that should remain operational and confirm the duplicate to merge.",
      },
      {
        id: "verify-result",
        title: "Verify the result",
        body: "The duplicate becomes Merged. The survivor keeps its existing status and remains the working record.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Review before confirming",
        body: "A merged flight is retained for history but should no longer be used for operational work.",
      },
    ],
    screenshotKey: "portal-resolve-duplicates",
  },
  {
    id: "portal-completion-work-order",
    sectionId: "work-orders",
    categoryId: "portal-work-order-lifecycle",
    audience: "portal",
    title: "Create a Completion work order",
    eyebrow: "Portal work-order lifecycle",
    summary:
      "Record what happened, who performed it, which resources were used, and the supporting evidence.",
    keywords: [
      "completion work order",
      "service lines",
      "tasks",
      "signature",
      "attachments",
      "actual times",
    ],
    location: "Portal → Operations → Flights → Open Work Order",
    steps: [
      {
        id: "complete-details",
        title: "Complete flight details",
        body: "Confirm the actual flight number, aircraft type, tail, ATA/ATD in UTC, and remarks.",
      },
      {
        id: "record-services",
        title: "Record performed services",
        body: "For each service, capture who performed it, From/To time, description, return-to-ramp indicator, and evidence.",
      },
      {
        id: "record-tasks",
        title: "Record tasks and resources",
        body: "Classify Major or Minor tasks, add employees and time, then record tools, materials, general supports, quantities, descriptions, and attachments.",
      },
      {
        id: "sign-submit",
        title: "Sign and create",
        body: "Add the optional customer signature when available, review the record, and select Create. The portal immediately saves a Submitted work order and the flight becomes In Progress.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Attachment limits",
        body: "Images: 10 MB each. Voice: 25 MB each. PDF: 20 MB each. Signature PNG: 2 MB.",
      },
      {
        tone: "warning",
        title: "Evidence editing is limited",
        body: "Attachments can be changed only while the work order is new, Submitted, or Returned.",
      },
    ],
    screenshotKey: "portal-completion-work-order",
  },
  {
    id: "portal-cancellation-work-order",
    sectionId: "work-orders",
    categoryId: "portal-work-order-lifecycle",
    audience: "portal",
    title: "Create a Cancellation work order",
    eyebrow: "Portal work-order lifecycle",
    summary:
      "Capture the cancellation time and reason, then route the record through approval.",
    keywords: ["cancel flight", "cancellation", "canceled at", "reason", "approval"],
    location: "Portal → Operations → Flights → Cancel flight",
    steps: [
      {
        id: "open-cancel",
        title: "Open Cancel flight",
        body: "Choose the scheduled or active flight only after confirming that cancellation is the intended outcome.",
      },
      {
        id: "enter-cancellation",
        title: "Enter the cancellation",
        body: "Record the cancellation time in UTC and a clear reason.",
      },
      {
        id: "submit-review",
        title: "Submit for review",
        body: "Submission creates the Cancellation work order for approval; it does not finalize the flight by itself.",
      },
      {
        id: "approve-cancel",
        title: "Approve to settle",
        body: "Approval changes the work order to Approved and the flight to Canceled.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Approval is the final step",
        body: "Do not report the flight as canceled until the Cancellation work order is approved.",
      },
    ],
    screenshotKey: "portal-cancellation-work-order",
  },
  {
    id: "portal-approve-return-merge",
    sectionId: "work-orders",
    categoryId: "portal-work-order-lifecycle",
    audience: "portal",
    title: "Approve, return, merge, and print",
    eyebrow: "Portal work-order lifecycle",
    summary:
      "Control approval numbers, reopen corrections, and combine compatible active work orders.",
    keywords: [
      "approve",
      "return",
      "merge work orders",
      "approval number",
      "print",
      "Returned",
    ],
    location: "Portal → Operations → Work Orders",
    steps: [
      {
        id: "review-queue",
        title: "Review the queue",
        body: "Filter by Submitted or Returned, work-order type, station, customer, and date. Open the record and verify all evidence.",
      },
      {
        id: "approve-record",
        title: "Approve valid work",
        body: "Approval marks the work order Approved, assigns the next station approval number, and settles the flight.",
      },
      {
        id: "return-record",
        title: "Return a correction",
        body: "Return an approved work order with a specific reason of up to 1,000 characters. Its approval number is released and the flight returns to In Progress.",
      },
      {
        id: "edit-reapprove",
        title: "Update and reapprove",
        body: "The owner—or a user with Manage Others—updates the unlocked record. It remains Returned; there is no separate resubmit state. A reviewer approves it again and a work-order number is assigned again.",
      },
      {
        id: "merge-compatible",
        title: "Merge when needed",
        body: "Select at least two Submitted or Returned work orders of the same type. Sources become Merged; the generated work order is Submitted unless approved immediately.",
      },
    ],
    callouts: [
      {
        tone: "tip",
        title: "Print from the flight view",
        body: "Print an approved work order from the Flights list or dashboard, not from the Work Orders queue.",
      },
      {
        tone: "warning",
        title: "Merge only compatible records",
        body: "Do not mix Completion and Cancellation work orders in one merge.",
      },
      {
        tone: "info",
        title: "Returned means editable",
        body: "Submitted and Returned work orders are editable. Approved and Merged work orders are locked.",
      },
    ],
    screenshotKey: "portal-work-order-approval",
  },
  {
    id: "mobile-find-flight",
    sectionId: "mobile-operations",
    categoryId: "mobile-work-order-lifecycle",
    audience: "mobile",
    title: "Find and open a mobile flight",
    eyebrow: "Mobile work-order lifecycle",
    summary:
      "Search the correct tab, refresh cached data, and open the flight action sheet.",
    keywords: [
      "My Flights",
      "Per Landing",
      "Ad Hoc",
      "flight card",
      "refresh",
      "action sheet",
    ],
    location: "Mobile → My Flights, Per Landing, or Ad Hoc",
    steps: [
      {
        id: "choose-tab",
        title: "Choose the correct tab",
        body: "Use My Flights for assigned work, Per Landing for designated contract flights, and Ad Hoc for unscheduled operations.",
      },
      {
        id: "search-refresh",
        title: "Search or refresh",
        body: "Search by the visible flight information. Pull to refresh when online if the expected flight is not shown.",
      },
      {
        id: "open-actions",
        title: "Open flight actions",
        body: "Tap the flight card to create or continue a work order, return to ramp, or cancel when those actions are available.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Lists are local-first",
        body: "Mobile screens read the device cache, then synchronization updates that cache from the server.",
      },
      {
        tone: "info",
        title: "Mobile uses an action window",
        body: "My Flights and Per Landing show Scheduled, In Progress, or Completed flights whose STA falls inside the current ±12-hour mobile window.",
      },
    ],
    screenshotKey: "mobile-my-flights",
  },
  {
    id: "mobile-create-work-order",
    sectionId: "mobile-operations",
    categoryId: "mobile-work-order-lifecycle",
    audience: "mobile",
    title: "Create or update a mobile work order",
    eyebrow: "Mobile work-order lifecycle",
    summary:
      "Complete the seeded services, add tasks and resources, save locally, or submit for delivery.",
    keywords: [
      "create work order",
      "update work order",
      "planned services",
      "draft",
      "submit",
      "signature",
    ],
    location: "Mobile → Flight actions → Create Work Order",
    steps: [
      {
        id: "verify-flight",
        title: "Verify flight details",
        body: "Confirm the selected flight and enter the required actual flight and aircraft information.",
      },
      {
        id: "complete-services",
        title: "Complete service lines",
        body: "Planned services are copied into the form. Complete the performer and timing for each performed line, or remove a line that was not performed.",
      },
      {
        id: "add-tasks",
        title: "Add tasks and evidence",
        body: "Record Major or Minor tasks, employees, times, tools, materials, general supports, quantities, descriptions, and attachments.",
      },
      {
        id: "save-or-submit",
        title: "Save or submit",
        body: "Use Save as draft for unfinished local work. Use Submit when the record is complete; the app queues it if the server is unavailable.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Remove, do not complete, Per Landing",
        body: "Aircraft Per Landing is a flight designation and must never be submitted as a performed service line.",
      },
      {
        tone: "info",
        title: "Draft is local-only",
        body: "A saved draft is not a Submitted server work order. Reopen it from Drafts and finish the submission.",
      },
    ],
    screenshotKey: "mobile-work-order-form",
  },
  {
    id: "mobile-offline-sync",
    sectionId: "mobile-operations",
    categoryId: "mobile-work-order-lifecycle",
    audience: "mobile",
    title: "Work offline and confirm sync",
    eyebrow: "Mobile work-order lifecycle",
    summary:
      "Understand local drafts, queued submissions, automatic delivery, retries, and conflicts.",
    keywords: [
      "offline",
      "sync center",
      "queued work",
      "Pending",
      "Failed",
      "Conflict",
      "retry",
      "discard",
    ],
    location: "Mobile → Sync Center",
    steps: [
      {
        id: "submit-offline",
        title: "Submit while offline",
        body: "The completed request and its attachments are stored in the device outbox.",
      },
      {
        id: "restore-connection",
        title: "Restore connectivity",
        body: "The app retries queued work automatically when connectivity returns. Refresh now is available while online.",
      },
      {
        id: "inspect-outbox",
        title: "Inspect Queued work",
        body: "Confirm that no Pending, Failed, Conflict, or Unknown item remains. Review the last error and attempt count when shown.",
      },
      {
        id: "resolve-problem",
        title: "Resolve failures",
        body: "Retry a failed item. For a conflict, discard the saved request, reopen the flight to get current server data, and submit a fresh version.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Keep local work safe",
        body: "Do not clear app data or uninstall the app while drafts or queued work remain on the device.",
      },
      {
        tone: "success",
        title: "Finish with an empty queue",
        body: "“Everything is submitted” means no offline work remains waiting in the outbox.",
      },
    ],
    screenshotKey: "mobile-sync-center",
  },
  {
    id: "mobile-return-cancel",
    sectionId: "mobile-operations",
    categoryId: "mobile-work-order-lifecycle",
    audience: "mobile",
    title: "Record return-to-ramp or cancellation",
    eyebrow: "Mobile work-order lifecycle",
    summary:
      "Use the dedicated flight actions and let the outbox deliver the mutation when offline.",
    keywords: [
      "return to ramp",
      "cancel flight",
      "cancellation reason",
      "offline mutation",
    ],
    location: "Mobile → Flight actions",
    steps: [
      {
        id: "check-return-eligibility",
        title: "Check Return to ramp eligibility",
        body: "Use the dedicated mobile action only while the flight is In Progress, you own an editable Completion work order (Submitted or Returned), and the flight is inside the mobile STA ±12-hour action window.",
      },
      {
        id: "capture-return",
        title: "Append the return-to-ramp work",
        body: "Select Return to ramp, add at least one new service or task with employees, times, resources, notes, or evidence, then select Submit. The additions are appended and marked as return-to-ramp activity.",
      },
      {
        id: "capture-cancel",
        title: "Use Cancel flight for a cancellation",
        body: "When the flight itself is canceled, use Cancel flight instead, enter the cancellation time and a clear reason, then submit the Cancellation work order.",
      },
      {
        id: "confirm-delivery",
        title: "Confirm delivery",
        body: "Both actions can queue locally. Open Sync Center and verify acceptance before a reviewer approves the work order.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Sync before approval",
        body: "Approval locks the work order. A return-to-ramp request still queued on the device can fail if a reviewer approves first.",
      },
      {
        tone: "warning",
        title: "Completed-flight action is not functional",
        body: "The current mobile build can show Return to ramp on a Completed flight, but that button is a placeholder. An approved work order must first be Returned from the portal to become editable.",
      },
      {
        tone: "info",
        title: "Portal uses the normal editor",
        body: "The portal has no separate return-to-ramp workflow. On an editable work order, use the Return to ramp checkbox on the relevant service line.",
      },
    ],
    screenshotKey: "mobile-flight-actions",
  },
  {
    id: "compare-flight-modes",
    sectionId: "special-operations",
    categoryId: "per-landing-ad-hoc",
    audience: "all",
    title: "Compare regular, Per Landing, and On Call",
    eyebrow: "Flight modes",
    summary:
      "Separate a normally assigned flight from a station-wide Per Landing flight and the derived On Call state.",
    keywords: [
      "regular flight",
      "Other not Per Landing",
      "Per Landing",
      "On Call",
      "assigned staff",
      "performed services",
      "derived state",
    ],
    location: "Portal → Operations → Flights; Mobile → My Flights or Per Landing",
    steps: [
      {
        id: "regular-flight",
        title: "Regular flight",
        body: "A regular flight is normally scheduled, is not Per Landing, and is not an Ad Hoc operation. Dispatchers can assign or invite same-station staff while Scheduled, and its planned services prefill the Completion work order.",
      },
      {
        id: "per-landing-flight",
        title: "Per Landing flight",
        body: "Aircraft Per Landing is its sole planned service. It has no assigned-staff roster, is visible station-wide to eligible operators, appears in the mobile Per Landing tab, and opens an empty performed-service list.",
      },
      {
        id: "on-call-derived",
        title: "On Call",
        body: "On Call is not selected at scheduling and is not master data. A Per Landing flight is derived as On Call when any non-Merged work order has at least one performed service line.",
      },
      {
        id: "watch-reversal",
        title: "Understand when the badge changes",
        body: "An empty or task-only work order remains Per Landing. Removing the last performed service line—or merging away the only qualifying source—returns the displayed category to Per Landing.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "On Call stays station-wide",
        body: "On Call remains a Per Landing-designated flight. The badge and extraction eligibility change, but it remains in the station’s Per Landing mobile list.",
      },
      {
        tone: "info",
        title: "Exact portal filters",
        body: "Use Per Landing (no performed services), On Call (performed services), or Other (not Per Landing).",
      },
      {
        tone: "info",
        title: "Ad Hoc is separate",
        body: "The portal’s Other (not Per Landing) filter can include Ad Hoc records, but operationally Ad Hoc is its own station-wide unscheduled flow—not a regular assigned flight.",
      },
    ],
    screenshotKey: "flight-mode-comparison",
  },
  {
    id: "per-landing-operations",
    sectionId: "special-operations",
    categoryId: "per-landing-ad-hoc",
    audience: "all",
    title: "Handle Per Landing flights",
    eyebrow: "Special operations",
    summary:
      "Keep Per Landing flights free of performed-service lines when they qualify for contract extraction.",
    keywords: [
      "Per Landing",
      "Aircraft Per Landing",
      "extraction",
      "bulk approve",
      "On Call",
    ],
    location: "Portal → Operations → Per Landing; Mobile → Per Landing",
    steps: [
      {
        id: "schedule-designation",
        title: "Use the designation",
        body: "Schedule the flight with the system Aircraft Per Landing designation and no assigned staff.",
      },
      {
        id: "monitor-mobile",
        title: "Monitor the dedicated list",
        body: "Eligible station operators can find the flight in the mobile Per Landing tab. There is no claim button; the user becomes the owner when authoring the work order.",
      },
      {
        id: "run-extraction",
        title: "Run portal extraction",
        body: "Open Extract Per Landing, then select eligible In Progress flights with an editable Completion work order and no performed service lines.",
      },
      {
        id: "verify-settlement",
        title: "Verify settlement",
        body: "Extraction approves the generated work, assigns work-order numbers, and completes eligible flights.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Performed work changes the path",
        body: "If any performed service line exists, the flight is treated as On Call and excluded from Per Landing extraction.",
      },
      {
        tone: "info",
        title: "Extraction is a bulk approval",
        body: "Review the selected station, date range, and eligibility result before confirming.",
      },
    ],
    screenshotKey: "portal-per-landing-extraction",
  },
  {
    id: "ad-hoc-operations",
    sectionId: "special-operations",
    categoryId: "per-landing-ad-hoc",
    audience: "all",
    title: "Handle Ad Hoc work",
    eyebrow: "Special operations",
    summary:
      "Create unscheduled work from the Ad Hoc flow, capture the real services performed, and confirm sync.",
    keywords: ["Ad Hoc", "unscheduled", "scratch flight", "customer", "aircraft"],
    location: "Portal → Operations → Work Orders → Create Ad Hoc; Mobile → Ad Hoc",
    steps: [
      {
        id: "start-ad-hoc",
        title: "Start an Ad Hoc record",
        body: "Use Create Ad Hoc in the portal work-order area or start from the mobile Ad Hoc flow.",
      },
      {
        id: "identify-flight",
        title: "Identify the operation",
        body: "Enter the customer, aircraft, flight, station, schedule, and other required details.",
      },
      {
        id: "capture-work",
        title: "Capture actual work",
        body: "Add the services, tasks, staff, resources, evidence, and signature that apply.",
      },
      {
        id: "submit-confirm",
        title: "Submit and confirm",
        body: "Submit the work order and, on mobile, confirm that the outbox delivers it.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Catalogs must be available",
        body: "If customers or aircraft types are empty on mobile, refresh or open Sync Center before creating the Ad Hoc record.",
      },
    ],
    screenshotKey: "mobile-ad-hoc",
  },
  {
    id: "master-countries",
    sectionId: "master-data",
    categoryId: "master-locations",
    audience: "administrator",
    title: "Maintain countries",
    eyebrow: "Master data",
    summary:
      "Create a stable country record before stations and customers reference it.",
    keywords: ["country", "ISO alpha-2", "station", "customer"],
    location: "Portal → Master Data → Countries",
    steps: [
      {
        id: "search-country",
        title: "Search first",
        body: "Check the country name and ISO alpha-2 code to avoid a duplicate.",
      },
      {
        id: "enter-country",
        title: "Enter the record",
        body: "Add the official country name and two-letter ISO code.",
      },
      {
        id: "save-review",
        title: "Save and review",
        body: "Confirm that the country is available when maintaining stations and customers.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Codes are operational identifiers",
        body: "Correct a wrong code carefully after checking existing station and customer references.",
      },
    ],
    screenshotKey: "master-data-countries",
  },
  {
    id: "master-stations",
    sectionId: "master-data",
    categoryId: "master-locations",
    audience: "administrator",
    title: "Maintain stations",
    eyebrow: "Master data",
    summary:
      "Define each station’s codes, location, and staff relationship.",
    keywords: ["station", "IATA", "ICAO", "city", "country", "station staff"],
    location: "Portal → Master Data → Stations",
    steps: [
      {
        id: "search-station",
        title: "Search by code",
        body: "Check the IATA and ICAO codes before adding a station.",
      },
      {
        id: "enter-station",
        title: "Enter station details",
        body: "Add the three-letter IATA code, optional four-letter ICAO code, name, city, and country.",
      },
      {
        id: "link-staff",
        title: "Link staff if needed",
        body: "Use the station’s staff action or the staff record to complete station assignments.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Station scopes operations",
        body: "Flights, approval numbering, staff eligibility, and many list filters depend on the station.",
      },
    ],
    screenshotKey: "master-data-stations",
  },
  {
    id: "master-manpower-licenses",
    sectionId: "master-data",
    categoryId: "master-people",
    audience: "administrator",
    title: "Maintain manpower types and licenses",
    eyebrow: "Master data",
    summary:
      "Define qualification catalogs and link manpower types to the services their operators may perform.",
    keywords: [
      "manpower type",
      "license",
      "qualification",
      "allowed services",
      "staff",
    ],
    location: "Portal → Master Data → Manpower Types or Licenses",
    steps: [
      {
        id: "create-manpower",
        title: "Create the manpower type",
        body: "Enter a clear name and description, then select its allowed services.",
      },
      {
        id: "create-license",
        title: "Create the license",
        body: "Enter the stable license code, name, and description.",
      },
      {
        id: "assign-staff",
        title: "Assign qualifications",
        body: "Update staff records with the correct manpower type and licenses.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "License code is stable",
        body: "Treat the license code as an immutable identifier after creation.",
      },
      {
        tone: "info",
        title: "Allowed services affect eligibility",
        body: "Allowed-service links filter which performed services the signed-in operator can record. They do not determine who appears as a flight-assignment candidate.",
      },
    ],
    screenshotKey: "master-data-manpower",
  },
  {
    id: "master-services-operation-types",
    sectionId: "master-data",
    categoryId: "master-service-catalog",
    audience: "administrator",
    title: "Maintain services and operation types",
    eyebrow: "Master data",
    summary:
      "Keep performed services separate from the operating mode used to classify a flight.",
    keywords: [
      "services",
      "operation types",
      "Aircraft Per Landing",
      "Ad Hoc",
      "manpower",
    ],
    location: "Portal → Master Data → Services or Operation Types",
    steps: [
      {
        id: "add-service",
        title: "Add a service",
        body: "Enter its name and description, then review which manpower types are allowed to perform it.",
      },
      {
        id: "add-operation-type",
        title: "Add an operation type",
        body: "Enter a name and description that classify how flights are operated.",
      },
      {
        id: "validate-forms",
        title: "Validate downstream use",
        body: "Confirm the service appears on schedules and work orders, and the operation type appears on flight entry.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Reserved system records",
        body: "Do not alter the system Aircraft Per Landing service or Ad Hoc operation type.",
      },
    ],
    screenshotKey: "master-data-services",
  },
  {
    id: "master-system-records",
    sectionId: "master-data",
    categoryId: "master-service-catalog",
    audience: "administrator",
    title: "Recognize protected system records",
    eyebrow: "Master-data governance",
    summary:
      "Keep fixed records intact because flight classification, special workflows, and fallback behavior depend on their stable identities.",
    keywords: [
      "system seeded",
      "protected records",
      "System badge",
      "Aircraft Per Landing",
      "Ad Hoc",
      "Unknown Customer",
      "System Administrator",
      "cannot edit",
      "cannot deactivate",
    ],
    location: "Portal → Master Data or Administration → Roles",
    steps: [
      {
        id: "find-system-badge",
        title: "Look for the System badge",
        body: "Protected rows are labeled System. Their action menus omit edit and status changes, and the server rejects direct modification attempts.",
      },
      {
        id: "protect-master-records",
        title: "Keep the three protected master records",
        body: "Do not modify Aircraft Per Landing (Service), Ad Hoc (Operation Type), or Unknown Customer (Customer). Unknown Customer also blocks logo, contact, and portal-access changes.",
      },
      {
        id: "protect-system-role",
        title: "Keep the protected role",
        body: "System Administrator is the only seeded system role. It follows the known permission catalog and cannot be renamed, have permissions edited, or be deleted.",
      },
      {
        id: "deactivate-normal-record",
        title: "Retire normal records safely",
        body: "Normal master data generally uses Activate or Deactivate instead of hard delete, preserving historical work-order and flight snapshots.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Seeded does not always mean protected",
        body: "The ISO country list is seeded reference data, but countries are not System records and remain editable or deactivatable.",
      },
      {
        tone: "warning",
        title: "On Call is not a record",
        body: "There is no current On Call service to create or maintain. On Call is calculated from real performed service lines on a Per Landing flight.",
      },
    ],
    screenshotKey: "master-data-system-record",
  },
  {
    id: "master-aircraft-resources",
    sectionId: "master-data",
    categoryId: "master-resources",
    audience: "administrator",
    title: "Maintain aircraft, tools, materials, and supports",
    eyebrow: "Master data",
    summary:
      "Keep the execution catalogs recognizable so staff can select the right aircraft and task resources.",
    keywords: [
      "aircraft type",
      "manufacturer",
      "tool",
      "factory ID",
      "serial number",
      "calibration",
      "material",
      "general support",
    ],
    location:
      "Portal → Master Data → Aircraft Types, Tools, Materials, or General Supports",
    steps: [
      {
        id: "aircraft",
        title: "Maintain aircraft types",
        body: "Enter manufacturer, model, and useful notes.",
      },
      {
        id: "tools",
        title: "Maintain tools",
        body: "Enter name, description, equipment factory ID, serial number, and calibration details as applicable.",
      },
      {
        id: "consumables",
        title: "Maintain materials and supports",
        body: "Use clear names and descriptions for materials and general supports selected on work-order tasks.",
      },
      {
        id: "test-selection",
        title: "Test selection",
        body: "Confirm active records are easy to distinguish in portal and mobile work-order forms.",
      },
    ],
    callouts: [
      {
        tone: "tip",
        title: "Use identifying detail",
        body: "Names should distinguish similar equipment without forcing ramp staff to guess from a generic label.",
      },
    ],
    screenshotKey: "master-data-resources",
  },
  {
    id: "master-customers",
    sectionId: "master-data",
    categoryId: "master-business-access",
    audience: "administrator",
    title: "Maintain customers",
    eyebrow: "Master data",
    summary:
      "Keep airline identity, official contacts, address, and branding complete.",
    keywords: [
      "customer",
      "airline",
      "IATA",
      "ICAO",
      "logo",
      "contact",
      "address",
    ],
    location: "Portal → Master Data → Customers",
    steps: [
      {
        id: "search-customer",
        title: "Search by name and code",
        body: "Check existing customers before creating another airline record.",
      },
      {
        id: "enter-identity",
        title: "Enter identity",
        body: "Add the customer name, country, optional two-letter IATA code, optional three-letter ICAO code, and logo.",
      },
      {
        id: "enter-contact",
        title: "Enter contact details",
        body: "Add the official contact, address, and any additional contacts required by operations.",
      },
      {
        id: "review-use",
        title: "Review operational use",
        body: "Confirm the customer is selectable on flights and Ad Hoc records.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Logo upload",
        body: "Use an accepted image format and keep the customer logo at or below 2 MB.",
      },
    ],
    screenshotKey: "master-data-customers",
  },
  {
    id: "master-staff",
    sectionId: "master-data",
    categoryId: "master-business-access",
    audience: "administrator",
    title: "Maintain staff",
    eyebrow: "Master data",
    summary:
      "Keep employment, station, manpower, schedule, and license data aligned with real assignments.",
    keywords: [
      "staff",
      "employee ID",
      "station",
      "manpower",
      "contract dates",
      "schedule",
      "licenses",
    ],
    location: "Portal → Master Data → Staff",
    steps: [
      {
        id: "enter-identity",
        title: "Enter identity",
        body: "Add the employee ID, name, and email.",
      },
      {
        id: "assign-operation",
        title: "Assign operational data",
        body: "Select the station and manpower type, then enter contract dates and the working schedule.",
      },
      {
        id: "assign-licenses",
        title: "Assign licenses",
        body: "Add the licenses the staff member currently holds.",
      },
      {
        id: "review-eligibility",
        title: "Review eligibility",
        body: "Confirm the record appears only for the station and services the staff member may perform.",
      },
    ],
    callouts: [
      {
        tone: "warning",
        title: "Dates and qualifications matter",
        body: "Keep employment and qualification data current. Manpower allowed-service links affect performed-service entry, while flight invitation currently checks active status, station, and existing assignment.",
      },
    ],
    screenshotKey: "master-data-staff",
  },
  {
    id: "master-portal-access",
    sectionId: "master-data",
    categoryId: "master-business-access",
    audience: "administrator",
    title: "Manage staff portal access",
    eyebrow: "Master data",
    summary:
      "Provision, monitor, suspend, and troubleshoot the portal identity attached to a staff record.",
    keywords: [
      "portal access",
      "No access",
      "Provisioning",
      "Invited",
      "Active",
      "Failed",
      "Suspended",
    ],
    location: "Portal → Master Data → Staff → Portal access",
    steps: [
      {
        id: "open-staff",
        title: "Open the staff record",
        body: "Confirm the employee email and employment details before changing access.",
      },
      {
        id: "provision-access",
        title: "Provision access",
        body: "Start portal provisioning and monitor the state until the account is Invited or Active.",
      },
      {
        id: "resolve-failure",
        title: "Resolve a failure",
        body: "Check the email and displayed error, correct the staff record, and retry the supported action.",
      },
      {
        id: "suspend-when-needed",
        title: "Suspend deliberately",
        body: "Suspend access when portal sign-in must stop while the historical staff record remains.",
      },
    ],
    callouts: [
      {
        tone: "info",
        title: "Access states",
        body: "A staff record can show No access, Provisioning, Has access (Invited or Active), Failed, or Suspended.",
      },
      {
        tone: "warning",
        title: "Portal access is not a qualification",
        body: "Provisioning an identity does not replace station, manpower, license, or role setup.",
      },
    ],
    screenshotKey: "master-data-portal-access",
  },
] as const satisfies readonly ManualArticle[];

export const flightLifecycleStatuses = [
  {
    id: "Scheduled",
    label: "Scheduled",
    summary: "The flight is planned and can be prepared for execution.",
    enteredWhen: "A new flight is saved.",
    availableActions: [
      "Edit schedule",
      "Invite staff for eligible non-Per Landing flights",
      "Open work order",
      "Cancel flight",
      "Resolve duplicates",
    ],
    nextStatusIds: ["InProgress", "Merged"],
    tone: "info",
  },
  {
    id: "InProgress",
    label: "In Progress",
    summary: "Operational work has been submitted and is awaiting settlement.",
    enteredWhen:
      "A work order is submitted, or an approved work order is returned for correction.",
    availableActions: ["Review work orders", "Resolve duplicates"],
    nextStatusIds: ["Completed", "Canceled", "Merged"],
    tone: "warning",
  },
  {
    id: "Completed",
    label: "Completed",
    summary: "An approved Completion work order has settled the flight.",
    enteredWhen: "A Completion work order is approved.",
    availableActions: ["Print approved work order", "Return for correction"],
    nextStatusIds: ["InProgress"],
    tone: "success",
  },
  {
    id: "Canceled",
    label: "Canceled",
    summary: "An approved Cancellation work order has settled the flight.",
    enteredWhen: "A Cancellation work order is approved.",
    availableActions: ["Return for correction"],
    nextStatusIds: ["InProgress"],
    tone: "danger",
  },
  {
    id: "Merged",
    label: "Merged",
    summary: "The duplicate flight is retained for history but is no longer operational.",
    enteredWhen: "A duplicate is merged into the selected survivor flight.",
    availableActions: ["Review history"],
    nextStatusIds: [],
    tone: "neutral",
  },
] as const satisfies readonly LifecycleStatusDefinition[];

export const workOrderLifecycleStatuses = [
  {
    id: "Submitted",
    label: "Submitted",
    summary: "The work order is active and ready for review.",
    enteredWhen:
      "A Completion or Cancellation work order is submitted, including a generated merge that is not immediately approved.",
    availableActions: ["Review", "Update where permitted", "Approve", "Merge"],
    nextStatusIds: ["Approved", "Merged"],
    tone: "warning",
  },
  {
    id: "Approved",
    label: "Approved",
    summary:
      "The work order has a station approval number and has settled the flight.",
    enteredWhen: "An authorized reviewer approves the work order.",
    availableActions: ["Print from the flight view", "Return for correction"],
    nextStatusIds: ["Returned"],
    tone: "success",
  },
  {
    id: "Returned",
    label: "Returned",
    summary: "An approved record has been reopened for correction.",
    enteredWhen:
      "An approver returns an Approved work order with a correction reason.",
    availableActions: ["Edit", "Approve", "Merge"],
    nextStatusIds: ["Approved", "Merged"],
    tone: "info",
  },
  {
    id: "Merged",
    label: "Merged",
    summary:
      "The source work order is retained for history and replaced by a consolidated record.",
    enteredWhen:
      "At least two compatible Submitted or Returned work orders are merged.",
    availableActions: ["Review history"],
    nextStatusIds: [],
    tone: "neutral",
  },
] as const satisfies readonly LifecycleStatusDefinition[];

export const mobileSyncStatuses = [
  {
    id: "Pending",
    label: "Pending",
    summary: "The request is stored on the device and waiting to send.",
    operatorAction:
      "Restore connectivity and leave the app available to retry; check Sync Center again.",
    tone: "warning",
  },
  {
    id: "Sending",
    label: "Sending",
    summary: "The app is currently delivering the request.",
    operatorAction: "Wait for the next status and avoid submitting the same work again.",
    tone: "info",
  },
  {
    id: "Failed",
    label: "Failed",
    summary: "The last delivery attempt failed.",
    operatorAction: "Read the last error, correct connectivity if needed, and tap Retry.",
    tone: "danger",
  },
  {
    id: "Conflict",
    label: "Conflict",
    summary: "Newer server data conflicts with the saved request.",
    operatorAction:
      "Discard the queued request, reopen the flight, and submit a fresh version.",
    tone: "danger",
  },
  {
    id: "Accepted",
    label: "Accepted",
    summary: "The server accepted the request.",
    operatorAction: "No action is required.",
    tone: "success",
  },
  {
    id: "Unknown",
    label: "Unknown",
    summary: "The app cannot classify the queued item’s current state.",
    operatorAction:
      "Review its error details and retry or discard only after confirming the work was not accepted.",
    tone: "neutral",
  },
] as const satisfies readonly MobileSyncStatusDefinition[];

export const masterDataMatrix = [
  {
    id: "countries",
    record: "Countries",
    location: "Master Data → Countries",
    keyFields: ["Official name", "ISO alpha-2 code"],
    usedBy: ["Stations", "Customers"],
    controls: ["Search before adding", "Keep codes unique", "Prefer deactivation"],
    screenshotKey: "master-data-countries",
  },
  {
    id: "stations",
    record: "Stations",
    location: "Master Data → Stations",
    keyFields: ["IATA (3)", "ICAO (4, optional)", "Name", "City", "Country"],
    usedBy: ["Flights", "Staff", "Approval numbers", "Operational filters"],
    controls: ["Keep codes unique", "Link the correct country", "Review staff"],
    screenshotKey: "master-data-stations",
  },
  {
    id: "manpower-types",
    record: "Manpower types",
    location: "Master Data → Manpower Types",
    keyFields: ["Name", "Description", "Allowed services"],
    usedBy: ["Staff qualifications", "Service eligibility"],
    controls: ["Link allowed services", "Review before deactivation"],
    screenshotKey: "master-data-manpower",
  },
  {
    id: "licenses",
    record: "Licenses",
    location: "Master Data → Licenses",
    keyFields: ["Stable code", "Name", "Description"],
    usedBy: ["Staff qualifications"],
    controls: ["Treat code as immutable", "Avoid duplicate qualifications"],
    screenshotKey: "master-data-licenses",
  },
  {
    id: "services",
    record: "Services",
    location: "Master Data → Services",
    keyFields: ["Name", "Description", "Allowed manpower types"],
    usedBy: ["Flight planning", "Work-order service lines"],
    controls: [
      "Protect Aircraft Per Landing",
      "Review manpower links",
      "Keep labels distinct",
    ],
    screenshotKey: "master-data-services",
  },
  {
    id: "operation-types",
    record: "Operation types",
    location: "Master Data → Operation Types",
    keyFields: ["Name", "Description"],
    usedBy: ["Flight classification", "Search and reporting"],
    controls: ["Protect Ad Hoc", "Do not use as a performed service"],
    screenshotKey: "master-data-operation-types",
  },
  {
    id: "aircraft-types",
    record: "Aircraft types",
    location: "Master Data → Aircraft Types",
    keyFields: ["Manufacturer", "Model", "Notes"],
    usedBy: ["Flights", "Completion work orders", "Ad Hoc work"],
    controls: ["Use recognizable model names", "Avoid duplicates"],
    screenshotKey: "master-data-aircraft",
  },
  {
    id: "tools",
    record: "Tools",
    location: "Master Data → Tools",
    keyFields: [
      "Name",
      "Description",
      "Equipment factory ID",
      "Serial number",
      "Calibration",
    ],
    usedBy: ["Work-order tasks", "Mobile resource catalog"],
    controls: ["Keep identifiers current", "Review calibration detail"],
    screenshotKey: "master-data-tools",
  },
  {
    id: "materials",
    record: "Materials",
    location: "Master Data → Materials",
    keyFields: ["Name", "Description"],
    usedBy: ["Work-order tasks", "Mobile resource catalog"],
    controls: ["Use specific names", "Retire obsolete records safely"],
    screenshotKey: "master-data-materials",
  },
  {
    id: "general-supports",
    record: "General supports",
    location: "Master Data → General Supports",
    keyFields: ["Name", "Description"],
    usedBy: ["Work-order tasks", "Mobile resource catalog"],
    controls: ["Use specific names", "Retire obsolete records safely"],
    screenshotKey: "master-data-supports",
  },
  {
    id: "customers",
    record: "Customers",
    location: "Master Data → Customers",
    keyFields: [
      "Name",
      "Country",
      "IATA (2, optional)",
      "ICAO (3, optional)",
      "Official contact",
      "Address",
      "Contacts",
      "Logo",
    ],
    usedBy: ["Flights", "Ad Hoc work", "Reports"],
    controls: ["Search by code", "Validate contacts", "Logo at or below 2 MB"],
    screenshotKey: "master-data-customers",
  },
  {
    id: "staff",
    record: "Staff",
    location: "Master Data → Staff",
    keyFields: [
      "Employee ID",
      "Name",
      "Email",
      "Station",
      "Manpower type",
      "Contract dates",
      "Schedule",
      "Licenses",
    ],
    usedBy: ["Flight assignments", "Service performers", "Tasks", "Portal identity"],
    controls: [
      "Keep station current",
      "Review dates and schedule",
      "Assign valid qualifications",
    ],
    screenshotKey: "master-data-staff",
  },
  {
    id: "portal-access",
    record: "Portal access",
    location: "Master Data → Staff → Portal access",
    keyFields: ["Email", "Access state", "Role and permissions"],
    usedBy: ["Portal sign-in", "Visible menus", "Permitted actions"],
    controls: [
      "Monitor provisioning",
      "Resolve failed invitations",
      "Suspend deliberately",
    ],
    screenshotKey: "master-data-portal-access",
  },
] as const satisfies readonly MasterDataMatrixRow[];

export const manualData = {
  sections: manualSections,
  categories: manualCategories,
  articles: manualArticles,
  lifecycles: {
    flights: flightLifecycleStatuses,
    workOrders: workOrderLifecycleStatuses,
    mobileSync: mobileSyncStatuses,
  },
  masterDataMatrix,
} as const;
