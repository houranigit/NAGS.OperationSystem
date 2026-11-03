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
    "summary": "Use the portal for flight scheduling and the existing administration and master-data catalogs.",
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
    "eyebrow": "Portal",
    "summary": "Start here for the scheduling baseline.",
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
    "summary": "This branch keeps basic flight planning. Additional operational workflows will be introduced separately.",
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
        "tone": "warning",
        "title": "Mobile is not supported by this backend baseline",
        "body": "The retained Android source uses the earlier operational API. Do not use it against this reset backend until its workflows are implemented again."
      }
    ],
    "sectionId": "get-started",
    "categoryId": "quick-start",
    "audience": "all",
    "location": "Portal",
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
    "id": "flight-access",
    "title": "Flight permissions and visibility",
    "eyebrow": "Account scope",
    "summary": "The backend checks both permission and scope for every flight request.",
    "keywords": [
      "permission",
      "view",
      "schedule",
      "update",
      "assign",
      "station",
      "viewer"
    ],
    "steps": [
      {
        "id": "view",
        "title": "Grant flight viewing",
        "body": "The flight view permission opens the grid. A viewer with only that destination lands directly on Flights."
      },
      {
        "id": "actions",
        "title": "Grant the needed actions",
        "body": "Scheduling, updating, and assigning staff have separate permissions. Creating a flight with staff assignments also requires assignment permission."
      },
      {
        "id": "scope",
        "title": "Check account scope",
        "body": "Visible records remain constrained by account and station scope. Staff reads follow the existing assignment and station visibility rules; the station-wide flight-view permission can widen permitted station reads."
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
