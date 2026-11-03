export type ManualAudience = "all" | "portal" | "mobile" | "administrator";

export type ManualCalloutTone = "info" | "tip" | "warning" | "success";

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
    "id": "get-started",
    "title": "Get started",
    "eyebrow": "Current scope",
    "summary": "Use the portal for flight planning and Android for scoped flight viewing and employee invitations. Administration and master-data catalogs remain available on the portal.",
    "keywords": [
      "portal",
      "scope",
      "mobile"
    ],
    "steps": [],
    "callouts": [],
    "categoryIds": [
      "quick-start"
    ]
  },
  {
    "id": "flight-operations",
    "title": "Flight scheduling",
    "eyebrow": "Portal",
    "summary": "Create one flight or schedule many flights, find records, and edit planned details.",
    "keywords": [
      "flight",
      "schedule",
      "batch"
    ],
    "steps": [],
    "callouts": [],
    "categoryIds": [
      "scheduling"
    ]
  },
  {
    "id": "android",
    "title": "Android",
    "eyebrow": "Flights and invitations",
    "summary": "Find flights, view planned details, and invite permitted station colleagues from the Android app.",
    "keywords": [
      "mobile",
      "android",
      "flights",
      "invite"
    ],
    "steps": [],
    "callouts": [],
    "categoryIds": [
      "android-flight-tools"
    ]
  },
  {
    "id": "access-and-oversight",
    "title": "Access",
    "eyebrow": "Permissions and scope",
    "summary": "Actions and visible records follow your effective permissions and account scope.",
    "keywords": [
      "access",
      "permissions",
      "station"
    ],
    "steps": [],
    "callouts": [],
    "categoryIds": [
      "access"
    ]
  },
  {
    "id": "master-data",
    "title": "Master data",
    "eyebrow": "Reference catalogs",
    "summary": "Maintain the reference records used by flight planning and administration.",
    "keywords": [
      "catalog",
      "customer",
      "staff"
    ],
    "steps": [],
    "callouts": [],
    "categoryIds": [
      "catalogs"
    ]
  }
] as const satisfies readonly ManualSection[];

export const manualCategories = [
  {
    "id": "quick-start",
    "title": "Orientation",
    "eyebrow": "Web and Android",
    "summary": "Start here for the current flight tools.",
    "keywords": [
      "start"
    ],
    "steps": [],
    "callouts": [],
    "sectionId": "get-started",
    "articleIds": [
      "current-scope"
    ]
  },
  {
    "id": "scheduling",
    "title": "Flights",
    "eyebrow": "Planning",
    "summary": "Use the Flights grid and scheduling dialog.",
    "keywords": [
      "schedule"
    ],
    "steps": [],
    "callouts": [],
    "sectionId": "flight-operations",
    "articleIds": [
      "schedule-flight",
      "schedule-many-flights",
      "find-edit-flight"
    ]
  },
  {
    "id": "android-flight-tools",
    "title": "Mobile flight tools",
    "eyebrow": "Android",
    "summary": "Browse flights and details, invite employees, and use account and notification tools.",
    "keywords": [
      "mobile",
      "android",
      "search",
      "refresh",
      "invite"
    ],
    "steps": [],
    "callouts": [],
    "sectionId": "android",
    "articleIds": [
      "android-browse-flights",
      "android-invite-employees",
      "android-catalog-sync",
      "android-account-notifications"
    ]
  },
  {
    "id": "access",
    "title": "Authorization",
    "eyebrow": "Administration",
    "summary": "Grant the actions needed for flight planning.",
    "keywords": [
      "permission"
    ],
    "steps": [],
    "callouts": [],
    "sectionId": "access-and-oversight",
    "articleIds": [
      "flight-access"
    ]
  },
  {
    "id": "catalogs",
    "title": "Reference records",
    "eyebrow": "Master data",
    "summary": "Keep planning references current.",
    "keywords": [
      "master data"
    ],
    "steps": [],
    "callouts": [],
    "sectionId": "master-data",
    "articleIds": [
      "master-data-overview"
    ]
  }
] as const satisfies readonly ManualCategory[];

export const manualArticles = [
  {
    "id": "current-scope",
    "title": "Flight scheduling baseline",
    "eyebrow": "Start here",
    "summary": "Plan flights in the portal. On Android, browse flights and invite employees within your permissions and account scope.",
    "keywords": [
      "baseline",
      "portal",
      "mobile",
      "work orders",
      "status",
      "history"
    ],
    "steps": [
      {
        "id": "sign-in",
        "title": "Sign in to the portal",
        "body": "Use your existing account. Open Operations → Flights when your permissions include flight viewing."
      },
      {
        "id": "planning",
        "title": "Use the planning tools",
        "body": "Create a flight, schedule a batch of dates, search the grid, or edit the scheduled details."
      },
      {
        "id": "android",
        "title": "Open flights on Android",
        "body": "Sign in with your existing account to search and refresh flights, open basic planned details, and invite employees when permitted."
      },
      {
        "id": "catalogs",
        "title": "Use reference catalogs",
        "body": "Administration, account settings, notifications, and Master Data remain available according to existing access."
      }
    ],
    "callouts": [
      {
        "tone": "info",
        "title": "Operational workflows are deferred",
        "body": "Work orders (WO), operational history, handover, approvals, status changes, Return to Ramp, exports, and Operations analytics are unavailable in this scheduling baseline."
      },
      {
        "tone": "info",
        "title": "Android flight access",
        "body": "The Android app supports flight viewing and employee invitations. Use the portal for single-flight and batch scheduling and planning edits. Connect to load current flight records and send invitations."
      }
    ],
    "sectionId": "get-started",
    "categoryId": "quick-start",
    "audience": "all",
    "location": "Portal and Android",
    "screenshotKey": "portal-login"
  },
  {
    "id": "schedule-flight",
    "title": "Create a flight",
    "eyebrow": "Single flight",
    "summary": "Enter the planned flight details, choose services, and optionally assign station staff.",
    "keywords": [
      "flight",
      "create",
      "customer",
      "station",
      "services",
      "staff"
    ],
    "steps": [
      {
        "id": "open",
        "title": "Open the form",
        "body": "In Operations → Flights, select Schedule flight."
      },
      {
        "id": "details",
        "title": "Enter flight details",
        "body": "Select customer, station, operation type, and optional aircraft type. Enter the flight number and scheduled arrival and departure using the time zone displayed by the form. Departure must be after arrival. Ad Hoc is not available for scheduled flights."
      },
      {
        "id": "services",
        "title": "Choose planned services",
        "body": "Select at least one service. Aircraft Per Landing must be the only service when selected."
      },
      {
        "id": "staff",
        "title": "Choose staff and save",
        "body": "If you have assignment permission, choose staff from the selected station. A Per Landing flight is scheduled without assigned staff. Select Schedule flight to create the record."
      }
    ],
    "callouts": [
      {
        "tone": "info",
        "title": "Use the displayed time zone",
        "body": "The form and grid use your selected user time zone. The backend stores the schedule in UTC."
      }
    ],
    "sectionId": "flight-operations",
    "categoryId": "scheduling",
    "audience": "portal",
    "location": "Operations → Flights"
  },
  {
    "id": "schedule-many-flights",
    "title": "Schedule many flights",
    "eyebrow": "Batch scheduling",
    "summary": "Create flights with shared planning details on selected calendar dates.",
    "keywords": [
      "batch",
      "schedule flights",
      "calendar",
      "weekdays",
      "time zone"
    ],
    "steps": [
      {
        "id": "open",
        "title": "Open Schedule flights",
        "body": "Select Schedule flights from the Flights grid."
      },
      {
        "id": "common",
        "title": "Set the shared details",
        "body": "Choose customer, station, operation type, optional aircraft type, and flight number. Select the date period, weekdays, and arrival and departure clock times."
      },
      {
        "id": "dates",
        "title": "Review the dates",
        "body": "The calendar step starts with dates matching the weekdays. Select or clear individual dates inside the period. At least one date is required."
      },
      {
        "id": "save",
        "title": "Select services and staff, then schedule",
        "body": "Choose the planned services and permitted station staff. Select Schedule flights. The grid refreshes after the batch is accepted."
      }
    ],
    "callouts": [
      {
        "tone": "tip",
        "title": "Overnight departures",
        "body": "When the departure clock time is at or before arrival, batch scheduling uses the following day for departure. Each selected date is interpreted in the displayed user time zone; invalid or ambiguous daylight-saving times are rejected."
      }
    ],
    "sectionId": "flight-operations",
    "categoryId": "scheduling",
    "audience": "portal",
    "location": "Operations → Flights → Schedule flights"
  },
  {
    "id": "find-edit-flight",
    "title": "Find and edit scheduled flights",
    "eyebrow": "Flights grid",
    "summary": "Filter the list and update the planning fields through Edit schedule.",
    "keywords": [
      "search",
      "filter",
      "grid",
      "edit",
      "assignment",
      "flight number"
    ],
    "steps": [
      {
        "id": "find",
        "title": "Find the flight",
        "body": "Search by flight number, customer, or ID. Narrow the grid by station, customer, operation type, and date range. Use paging and sorting for longer lists."
      },
      {
        "id": "edit",
        "title": "Open Edit schedule",
        "body": "Open the row action menu and select Edit schedule. This action requires update permission."
      },
      {
        "id": "save",
        "title": "Save the planned changes",
        "body": "Review the flight number, schedule, aircraft type, planned services, and staff assignments and save. Customer, station, and operation type are fixed after creation. If another user changed the flight, reload its current version before retrying."
      }
    ],
    "callouts": [
      {
        "tone": "info",
        "title": "Planning only",
        "body": "There is no status column, flight-history page, work-order menu, or operational lifecycle action in this baseline."
      }
    ],
    "sectionId": "flight-operations",
    "categoryId": "scheduling",
    "audience": "portal",
    "location": "Operations → Flights"
  },
  {
    "id": "android-browse-flights",
    "title": "Find and view flights on Android",
    "eyebrow": "Flight register",
    "summary": "Search the flights you can access, refresh the list, and open the basic planned details.",
    "keywords": [
      "android",
      "mobile",
      "flights",
      "search",
      "refresh",
      "load more",
      "details",
      "planned services",
      "assigned staff"
    ],
    "steps": [
      {
        "id": "sign-in",
        "title": "Sign in and open Flights",
        "body": "Use your existing account and complete the MFA code step if prompted. Flight viewing permission is required; the app shows an access message when it is missing."
      },
      {
        "id": "search",
        "title": "Find a flight",
        "body": "Enter a flight number, customer, or flight ID in Search. Tap Load more when more matching records are available. The list follows the same account and station visibility rules as the portal."
      },
      {
        "id": "refresh",
        "title": "Refresh the list",
        "body": "Pull down or tap Refresh flights to request current records. Flights also refresh when the screen resumes. A connection is required; if a request fails, check the connection and retry."
      },
      {
        "id": "details",
        "title": "Open the flight details",
        "body": "Tap a flight card to view its customer, station, operation type, scheduled arrival and departure, optional aircraft type, planned services, assigned staff, and flight ID. Times use the device time zone shown above the list."
      }
    ],
    "callouts": [
      {
        "tone": "info",
        "title": "Mobile flight actions",
        "body": "Android provides flight viewing and permitted employee invitations. Use the portal to create flights, schedule many dates, or edit planning details."
      }
    ],
    "sectionId": "android",
    "categoryId": "android-flight-tools",
    "audience": "mobile",
    "location": "Android → Flights"
  },
  {
    "id": "android-invite-employees",
    "title": "Invite employees from Android",
    "eyebrow": "Flight staff",
    "summary": "Add eligible station colleagues to a flight while preserving the existing assigned staff.",
    "keywords": [
      "android",
      "mobile",
      "invite",
      "employees",
      "colleagues",
      "staff",
      "assignment",
      "permission",
      "per landing"
    ],
    "steps": [
      {
        "id": "open",
        "title": "Open Invite employees",
        "body": "Open the flight details and tap Invite employees. This action appears only with flight invite permission and is unavailable for Per Landing flights."
      },
      {
        "id": "choose",
        "title": "Choose station colleagues",
        "body": "Select one or more employees from the available list. Options contain active staff at the flight station who are not already assigned. Existing assigned staff remain on the flight."
      },
      {
        "id": "send",
        "title": "Send the invitations",
        "body": "Tap Send invitations while connected. After the server accepts the request, the app refreshes the flight details and list. Invited colleagues are added to the assigned staff; notifications follow the existing notification setup."
      },
      {
        "id": "refresh",
        "title": "Review a changed flight before retrying",
        "body": "If another user changed the flight, review the refreshed details and invitation options before sending again. Selected employees who are no longer available must be deselected; Refresh available employees loads the latest choices."
      }
    ],
    "callouts": [
      {
        "tone": "info",
        "title": "Permissions and station scope apply",
        "body": "An invitation adds employees to the flight. The server checks your invite permission, writable account, flight access, and the selected employees at the flight station."
      }
    ],
    "sectionId": "android",
    "categoryId": "android-flight-tools",
    "audience": "mobile",
    "location": "Android → Flights → Flight details → Invite employees"
  },
  {
    "id": "android-catalog-sync",
    "title": "Keep shared catalogs synchronized",
    "eyebrow": "Android reference data",
    "summary": "Refresh the local aircraft, customer, service, tool, material, general-support, and ATA chapter catalogs.",
    "keywords": [
      "mobile",
      "android",
      "sync",
      "catalogs",
      "aircraft types",
      "customers",
      "offline"
    ],
    "steps": [
      {
        "id": "open",
        "title": "Open Sync Center",
        "body": "Tap the sync-status icon in the app header, or open My profile → Sync Center. The page shows network and live-channel status, the last catalog event, and a separate card for each catalog with its cached record count, last successful sync time and age, duration, and any refresh error."
      },
      {
        "id": "refresh",
        "title": "Refresh the catalogs",
        "body": "While connected, catalog creates, edits, activations, and deactivations in the portal trigger a live refresh of the cached records you may access. You can also select Refresh now. Automatic refresh after sign-in, returning to the app, reconnecting, and periodically while the app process is running covers missed live updates."
      },
      {
        "id": "offline",
        "title": "Check the last successful sync",
        "body": "A connection failure keeps the last successful catalog cache for the current account. The displayed refresh time shows its age. Reconnect and retry if a catalog has not refreshed."
      },
      {
        "id": "access",
        "title": "Resolve catalog access errors",
        "body": "Catalog access follows your Master Data permissions. Access denied clears that catalog's cached records; ask an administrator to review your access. Signing out or changing accounts clears the shared catalog cache."
      }
    ],
    "callouts": [
      {
        "tone": "info",
        "title": "Catalog caching and flight access",
        "body": "Shared catalog synchronization does not queue work orders or other changes. Loading current flights, opening flight details, and sending employee invitations still require a connection."
      }
    ],
    "sectionId": "android",
    "categoryId": "android-flight-tools",
    "audience": "mobile",
    "location": "Android → Sync Center"
  },
  {
    "id": "android-account-notifications",
    "title": "Use your Android account and notifications",
    "eyebrow": "Account tools",
    "summary": "Keep using sign-in, MFA, profile viewing, and the notification inbox alongside the flight register.",
    "keywords": [
      "android",
      "mobile",
      "login",
      "sign in",
      "MFA",
      "profile",
      "account",
      "notifications",
      "read",
      "archive",
      "sign out"
    ],
    "steps": [
      {
        "id": "account",
        "title": "Open your account",
        "body": "Use the account action in the app header to view your name and email. The profile refreshes while connected. Select Sign out to end the current session."
      },
      {
        "id": "inbox",
        "title": "Open the notification inbox",
        "body": "Use the notification bell to view your inbox and unread count. Read or archive notifications using the available inbox actions."
      },
      {
        "id": "flight",
        "title": "Follow a flight notification",
        "body": "Open a flight notification to load its basic flight details. Access is checked again for the signed-in account. A schedule-level notification refreshes Flights; a flight that is no longer accessible shows an error."
      }
    ],
    "callouts": [
      {
        "tone": "info",
        "title": "Device notifications",
        "body": "System alerts follow device permissions and the configured notification service. The in-app inbox remains available through its existing account access."
      }
    ],
    "sectionId": "android",
    "categoryId": "android-flight-tools",
    "audience": "mobile",
    "location": "Android → Account / Notifications"
  },
  {
    "id": "flight-access",
    "title": "Flight permissions and visibility",
    "eyebrow": "Account scope",
    "summary": "Web and Android use the same backend permission and scope checks for flight requests.",
    "keywords": [
      "permission",
      "view",
      "schedule",
      "update",
      "assign",
      "station",
      "viewer",
      "invite",
      "android",
      "mobile"
    ],
    "steps": [
      {
        "id": "view",
        "title": "Grant flight viewing",
        "body": "The flight view permission opens the portal grid and Android flight list and details. A portal viewer with only that destination lands directly on Flights."
      },
      {
        "id": "actions",
        "title": "Grant the needed actions",
        "body": "Portal scheduling, updating, and assigning staff have separate permissions. Creating a flight with staff assignments also requires assignment permission. Android employee invitations use the separate flight invite permission; viewing a flight alone does not grant invitations."
      },
      {
        "id": "scope",
        "title": "Check account scope",
        "body": "System administrators and Viewer Only accounts can read across stations when granted flight viewing. Station staff remain limited to their active linked station: Per Landing flights and assigned flights are visible, with the station-wide flight-view permission widening reads at that station. Invitations require a writable account, invite permission, and access to the flight."
      }
    ],
    "callouts": [
      {
        "tone": "info",
        "title": "The API is authoritative",
        "body": "Hidden buttons are not an access boundary. An account must have the required server-side permission and scope."
      }
    ],
    "sectionId": "access-and-oversight",
    "categoryId": "access",
    "audience": "administrator",
    "location": "Administration → Roles"
  },
  {
    "id": "master-data-overview",
    "title": "Maintain planning reference data",
    "eyebrow": "Master Data",
    "summary": "The existing catalogs continue to supply validated references and snapshots for flight planning.",
    "keywords": [
      "master data",
      "customer",
      "station",
      "aircraft",
      "service",
      "staff",
      "protected system records"
    ],
    "steps": [
      {
        "id": "find",
        "title": "Open the relevant catalog",
        "body": "Use Master Data to find customers, stations, operation types, aircraft types, services, or staff. Other existing reference catalogs remain available through their own permissions."
      },
      {
        "id": "review",
        "title": "Review before changing",
        "body": "Search for existing records, use stable codes, and review the record detail and existing constraints before editing or deactivating."
      },
      {
        "id": "staff",
        "title": "Keep station staff accurate",
        "body": "Check staff station and active state before assigning a flight. Flight scheduling only offers eligible staff from its selected station."
      }
    ],
    "callouts": [
      {
        "tone": "info",
        "title": "Protected system records",
        "body": "Keep the catalog’s existing system-record protections. Aircraft Per Landing remains the sole planned service for a Per Landing flight; Ad Hoc is excluded from scheduled-flight creation."
      }
    ],
    "sectionId": "master-data",
    "categoryId": "catalogs",
    "audience": "administrator",
    "location": "Master Data",
    "screenshotKey": "master-data-services"
  }
] as const satisfies readonly ManualArticle[];

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
    usedBy: ["Flights", "Staff", "Operational filters"],
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
    usedBy: ["Flight planning", "Service catalog"],
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
    usedBy: ["Flight classification", "Flight search"],
    controls: ["Protect Ad Hoc", "Do not use as a performed service"],
    screenshotKey: "master-data-operation-types",
  },
  {
    id: "aircraft-types",
    record: "Aircraft types",
    location: "Master Data → Aircraft Types",
    keyFields: ["Manufacturer", "Model", "Notes"],
    usedBy: ["Flights", "Aircraft catalog"],
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
    usedBy: ["Resource catalog"],
    controls: ["Keep identifiers current", "Review calibration detail"],
    screenshotKey: "master-data-tools",
  },
  {
    id: "materials",
    record: "Materials",
    location: "Master Data → Materials",
    keyFields: ["Name", "Description"],
    usedBy: ["Resource catalog"],
    controls: ["Use specific names", "Retire obsolete records safely"],
    screenshotKey: "master-data-materials",
  },
  {
    id: "general-supports",
    record: "General supports",
    location: "Master Data → General Supports",
    keyFields: ["Name", "Description"],
    usedBy: ["Resource catalog"],
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
    usedBy: ["Flights", "Customer catalog"],
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
    usedBy: ["Flight assignments", "Portal identity"],
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

export const manualData = { sections: manualSections, categories: manualCategories, articles: manualArticles, masterDataMatrix } as const;
