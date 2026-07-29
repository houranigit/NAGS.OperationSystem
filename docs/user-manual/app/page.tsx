"use client";

import {
  flightLifecycleStatuses,
  manualArticles,
  manualCategories,
  manualSections,
  masterDataMatrix,
  mobileSyncStatuses,
  workOrderLifecycleStatuses,
  type ManualArticle,
  type ManualAudience,
  type ManualCalloutTone,
} from "./manual-data";
import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

const articles = manualArticles as readonly ManualArticle[];
const audienceOptions: readonly {
  id: "everything" | ManualAudience;
  label: string;
}[] = [
  { id: "everything", label: "All guides" },
  { id: "portal", label: "Portal" },
  { id: "mobile", label: "Mobile" },
  { id: "administrator", label: "Admin" },
];

const statusGlyphs: Record<string, string> = {
  scheduled: "01",
  inprogress: "02",
  completed: "03",
  canceled: "×",
  merged: "↗",
  submitted: "01",
  returned: "↺",
  approved: "02",
  pending: "…",
  sending: "↑",
  failed: "!",
  conflict: "⇄",
  synced: "✓",
  draft: "○",
};

function normalizeSearchText(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
}

function SearchIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 4 4" />
    </svg>
  );
}

function ArrowIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M5 12h14M14 7l5 5-5 5" />
    </svg>
  );
}

function LogoMark() {
  return (
    <span className="logo-mark" aria-hidden="true">
      <span />
      <span />
      <span />
    </span>
  );
}

function Callout({
  tone,
  title,
  children,
}: {
  tone: ManualCalloutTone;
  title: string;
  children: ReactNode;
}) {
  const labels: Record<ManualCalloutTone, string> = {
    info: "Note",
    tip: "Field tip",
    warning: "Check",
    success: "Confirmed",
  };

  return (
    <aside className={`callout callout-${tone}`}>
      <div className="callout-label">
        <span>{labels[tone]}</span>
        <i aria-hidden="true" />
      </div>
      <div>
        <strong>{title}</strong>
        <p>{children}</p>
      </div>
    </aside>
  );
}

function FlightLifecycle() {
  return (
    <section className="visual-card lifecycle-card" aria-labelledby="flight-life-title">
      <div className="visual-heading">
        <div>
          <span className="micro-label">Lifecycle map</span>
          <h2 id="flight-life-title">A flight settles with its work order</h2>
        </div>
        <span className="visual-key">Portal + mobile</span>
      </div>
      <div className="lifecycle-track flight-track">
        {flightLifecycleStatuses.slice(0, 3).map((status, index) => (
          <div className={`status-node tone-${status.tone}`} key={status.id}>
            <div className="status-topline">
              <span className="status-glyph">
                {statusGlyphs[status.id.toLowerCase()] ??
                  String(index + 1).padStart(2, "0")}
              </span>
              {index < 2 && <span className="connector" aria-hidden="true" />}
            </div>
            <h3>{status.label}</h3>
            <p>{status.enteredWhen}</p>
          </div>
        ))}
      </div>
      <div className="branch-row">
        {flightLifecycleStatuses.slice(3).map((status) => (
          <div className={`branch-card tone-${status.tone}`} key={status.id}>
            <span>{statusGlyphs[status.id.toLowerCase()] ?? "•"}</span>
            <div>
              <strong>{status.label}</strong>
              <p>{status.enteredWhen}</p>
            </div>
          </div>
        ))}
      </div>
      <p className="visual-caption">
        Returning an approved work order reopens the flight as{" "}
        <strong>In Progress</strong>. Merged flights remain available as history.
      </p>
    </section>
  );
}

function WorkOrderLifecycle() {
  return (
    <section className="visual-card lifecycle-card" aria-labelledby="work-life-title">
      <div className="visual-heading">
        <div>
          <span className="micro-label">Lifecycle map</span>
          <h2 id="work-life-title">Review controls the final state</h2>
        </div>
        <span className="visual-key">Approval gate</span>
      </div>
      <div className="lifecycle-track work-track">
        {workOrderLifecycleStatuses.slice(0, 3).map((status, index) => (
          <div className={`status-node tone-${status.tone}`} key={status.id}>
            <div className="status-topline">
              <span className="status-glyph">
                {statusGlyphs[status.id.toLowerCase()] ??
                  String(index + 1).padStart(2, "0")}
              </span>
              {index < 2 && <span className="connector" aria-hidden="true" />}
            </div>
            <h3>{status.label}</h3>
            <p>{status.summary}</p>
          </div>
        ))}
      </div>
      <div className="branch-row single-branch">
        {workOrderLifecycleStatuses.slice(3).map((status) => (
          <div className={`branch-card tone-${status.tone}`} key={status.id}>
            <span>{statusGlyphs[status.id.toLowerCase()] ?? "↗"}</span>
            <div>
              <strong>{status.label}</strong>
              <p>{status.summary}</p>
            </div>
          </div>
        ))}
      </div>
      <div className="rule-strip">
        <strong>Correction loop</strong>
        <span>
          Submitted → Approved ↔ Returned. Editing a Returned record does not create a
          resubmit state; a reviewer approves it directly.
        </span>
      </div>
    </section>
  );
}

function MobileSyncGuide() {
  return (
    <section className="visual-card sync-card" aria-labelledby="sync-guide-title">
      <div className="visual-heading">
        <div>
          <span className="micro-label">Device delivery</span>
          <h2 id="sync-guide-title">A local save is not yet server acceptance</h2>
        </div>
        <span className="visual-key">Sync Center</span>
      </div>
      <div className="sync-grid">
        {mobileSyncStatuses.map((status) => (
          <div className={`sync-state tone-${status.tone}`} key={status.id}>
            <span className="sync-icon">
              {statusGlyphs[status.id.toLowerCase()] ?? "•"}
            </span>
            <div>
              <strong>{status.label}</strong>
              <p>{status.operatorAction}</p>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

function SpecialOperationsGuide() {
  return (
    <section className="visual-card compare-card" aria-labelledby="special-guide-title">
      <div className="visual-heading">
        <div>
          <span className="micro-label">Decision guide</span>
          <h2 id="special-guide-title">Choose the operating mode first</h2>
        </div>
      </div>
      <div className="compare-grid">
        <article>
          <span className="compare-index">PL</span>
          <h3>Per Landing</h3>
          <p>Contract-designated flight with no staff assigned at scheduling.</p>
          <ul>
            <li>Aircraft Per Landing is the sole planned service</li>
            <li>Extraction can settle eligible flights in batch</li>
            <li>Never record it as performed work</li>
          </ul>
        </article>
        <article>
          <span className="compare-index">AH</span>
          <h3>Ad Hoc</h3>
          <p>Real, unscheduled work created when no planned flight exists.</p>
          <ul>
            <li>Capture the actual customer, flight, and aircraft</li>
            <li>Add only services actually performed</li>
            <li>Submit through the normal approval path</li>
          </ul>
        </article>
      </div>
    </section>
  );
}

function PersonaGuide() {
  const personas = [
    {
      key: "SA",
      name: "System Administrator",
      scope: "Global",
      surface: "Portal",
      actions: ["Identity & roles", "Master data", "Approve", "Reports"],
    },
    {
      key: "DP",
      name: "Dispatcher",
      scope: "One station",
      surface: "Portal",
      actions: ["Schedule", "Assign", "Invite", "Monitor"],
    },
    {
      key: "OP",
      name: "Station Operator",
      scope: "Assigned + station PL",
      surface: "Mobile",
      actions: ["Author WO", "Return to ramp", "Sync", "Invite*"],
    },
  ] as const;

  return (
    <section className="visual-card persona-guide" aria-labelledby="persona-guide-title">
      <div className="visual-heading">
        <div>
          <span className="micro-label">Access model</span>
          <h2 id="persona-guide-title">Three roles, three clear boundaries</h2>
        </div>
        <span className="visual-key">Least privilege</span>
      </div>
      <div className="persona-grid">
        {personas.map((persona) => (
          <article key={persona.key}>
            <div className="persona-top">
              <span>{persona.key}</span>
              <small>{persona.surface}</small>
            </div>
            <h3>{persona.name}</h3>
            <p>{persona.scope}</p>
            <div>
              {persona.actions.map((action) => (
                <span key={action}>{action}</span>
              ))}
            </div>
          </article>
        ))}
      </div>
      <div className="permission-formula">
        <span>Effective access</span>
        <strong>Page permission</strong>
        <i>+</i>
        <strong>Action permission</strong>
        <i>+</i>
        <strong>Account scope</strong>
      </div>
      <p className="visual-caption">
        *Mobile can currently display Invite teammates without checking permission. The
        server still rejects unauthorized requests.
      </p>
    </section>
  );
}

function OversightGuide() {
  return (
    <section className="visual-card oversight-guide" aria-labelledby="oversight-guide-title">
      <div className="visual-heading">
        <div>
          <span className="micro-label">Oversight path</span>
          <h2 id="oversight-guide-title">Monitor, narrow, then publish</h2>
        </div>
        <span className="visual-key">UTC reporting</span>
      </div>
      <div className="oversight-flow">
        <article>
          <span>01</span>
          <small>Home dashboard</small>
          <strong>Current control</strong>
          <p>Live workload, active flights, recent work orders, master-data totals.</p>
        </article>
        <i aria-hidden="true">→</i>
        <article>
          <span>02</span>
          <small>Operations dashboard</small>
          <strong>Filtered analysis</strong>
          <p>Status, station mix, Per Landing vs On Call, trends, customers, services.</p>
        </article>
        <i aria-hidden="true">→</i>
        <article>
          <span>03</span>
          <small>Output</small>
          <strong>Export or Print WO</strong>
          <p>XLSX, CSV, PDF report—or an Approved Completion work-order PDF.</p>
        </article>
      </div>
    </section>
  );
}

function FlightModeGuide() {
  const modes = [
    {
      index: "01",
      title: "Regular",
      subtitle: "Scheduled · not PL · not Ad Hoc",
      detail: "Planned services + optional assigned roster",
      visibility: "Assigned staff, unless station-wide view is granted",
      tone: "regular",
    },
    {
      index: "02",
      title: "Per Landing",
      subtitle: "No performed services",
      detail: "Aircraft Per Landing is the sole planned service",
      visibility: "All eligible station operators; no roster",
      tone: "landing",
    },
    {
      index: "03",
      title: "On Call",
      subtitle: "Derived, never scheduled",
      detail: "Per Landing + ≥1 real performed service line",
      visibility: "Still station-wide and still in Per Landing mobile",
      tone: "oncall",
    },
  ] as const;

  return (
    <section className="visual-card mode-guide" aria-labelledby="mode-guide-title">
      <div className="visual-heading">
        <div>
          <span className="micro-label">Classification map</span>
          <h2 id="mode-guide-title">The service evidence determines the mode</h2>
        </div>
      </div>
      <div className="mode-grid">
        {modes.map((mode) => (
          <article className={mode.tone} key={mode.title}>
            <div>
              <span>{mode.index}</span>
              <small>{mode.subtitle}</small>
            </div>
            <h3>{mode.title}</h3>
            <p>{mode.detail}</p>
            <footer>
              <span>Visibility</span>
              <strong>{mode.visibility}</strong>
            </footer>
          </article>
        ))}
      </div>
      <div className="mode-derivation">
        <span>Per Landing</span>
        <i>+ performed service line</i>
        <strong>On Call</strong>
        <small>Task-only or empty work does not change the mode.</small>
      </div>
    </section>
  );
}

type ScreenshotReference = {
  src: string;
  alt: string;
  title: string;
  caption: string;
  kind: "portal" | "mobile";
  href?: string;
  linkLabel?: string;
};

const screenshotLibrary: Record<string, readonly ScreenshotReference[]> = {
  "quick-start-surfaces": [
    {
      src: "screenshots/portal-login.png",
      alt: "Actual Operations portal sign-in screen",
      title: "Portal sign in",
      caption: "Portal entry point for planners, reviewers, and administrators.",
      kind: "portal",
    },
    {
      src: "screenshots/mobile-login.png",
      alt: "Actual Operations mobile application sign-in screen",
      title: "Mobile sign in",
      caption: "Mobile entry point for station and ramp execution.",
      kind: "mobile",
    },
  ],
  "portal-flight-filters": [
    {
      src: "screenshots/portal-flights-populated.png",
      alt: "Actual populated portal flight register with search and filters",
      title: "Flight register",
      caption:
        "The live register shows regular, Per Landing, and On Call flights together.",
      kind: "portal",
    },
    {
      src: "screenshots/mobile-my-flights-lifecycle.png",
      alt: "Actual mobile My Flights list with lifecycle statuses",
      title: "Mobile flight list",
      caption: "My Flights keeps assigned records and their current status together.",
      kind: "mobile",
    },
  ],
  "portal-role-personas": [
    {
      src: "screenshots/portal-system-role.png",
      alt: "Actual protected System Administrator role overview",
      title: "System Administrator",
      caption: "The protected administrator role carries global system access.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-dispatcher-role-permissions.png",
      alt: "Actual Dispatcher role permission editor",
      title: "Dispatcher permissions",
      caption:
        "The station-scoped Dispatcher role grants scheduling, assignment, invite, and monitoring actions.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-station-operator-role-permissions.png",
      alt: "Actual Station Operator role permission editor",
      title: "Station Operator permissions",
      caption:
        "The regular operator role is intentionally narrow and centered on flight visibility and work-order authoring.",
      kind: "portal",
    },
    {
      src: "screenshots/mobile-my-flights-assigned.png",
      alt: "Actual regular-user mobile assigned-flight list",
      title: "Regular-user scope",
      caption: "The mobile operator sees assigned regular flights in My Flights.",
      kind: "mobile",
    },
  ],
  "portal-operations-dashboard": [
    {
      src: "screenshots/portal-operations-dashboard-populated.png",
      alt: "Actual populated operations dashboard with flight KPIs",
      title: "Operations overview",
      caption:
        "Live totals, status distribution, and Per Landing versus On Call are calculated from the current records.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-operations-dashboard-register.png",
      alt: "Actual dashboard flight register",
      title: "Dashboard flight register",
      caption: "The register underneath the charts follows the same active filters.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-dashboard-work-orders.png",
      alt: "Actual home dashboard work-order panel",
      title: "Home work-order panel",
      caption: "The home dashboard provides a quick operational queue.",
      kind: "portal",
    },
  ],
  "portal-report-print": [
    {
      src: "screenshots/portal-flight-export-menu.png",
      alt: "Actual portal flight export menu",
      title: "Export menu",
      caption: "Export the authorized filtered result as Excel, CSV, or PDF.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-flights-report-pdf.png",
      alt: "Actual generated flight report PDF",
      title: "Flight report PDF",
      caption: "The generated report includes the populated regular and special-operation rows.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-print-work-order-action.png",
      alt: "Actual Print work order action on a completed flight",
      title: "Print work order",
      caption:
        "Print WO is offered for a Completed flight with an Approved Completion work order.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-work-order-pdf.png",
      alt: "Actual approved work-order PDF",
      title: "Approved WO PDF",
      caption: "The printable station-numbered document is generated from the approved record.",
      kind: "portal",
      href: "downloads/work-order-KAI-0001.pdf",
      linkLabel: "Open sample PDF",
    },
  ],
  "portal-schedule-flight": [
    {
      src: "screenshots/portal-schedule-flight.png",
      alt: "Actual portal flight scheduling wizard",
      title: "Schedule flight",
      caption: "Enter flight details, planned services, and eligible staff in sequence.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-flight-staff-assignment.png",
      alt: "Actual portal Staff step with Dispatcher and Station Operator",
      title: "Replace the roster",
      caption:
        "Edit flight → Staff shows the complete selected roster that Save changes will replace.",
      kind: "portal",
    },
  ],
  "portal-invite-staff": [
    {
      src: "screenshots/portal-invite-employee.png",
      alt: "Actual portal invite employee dialog",
      title: "Invite from portal",
      caption:
        "Invite adds selected active staff from the same station to a Scheduled regular flight.",
      kind: "portal",
    },
    {
      src: "screenshots/mobile-invite-teammates.png",
      alt: "Actual mobile Invite teammates screen",
      title: "Invite from mobile",
      caption:
        "Mobile shows the same add-only teammate selection; the server still checks permission and eligibility.",
      kind: "mobile",
    },
  ],
  "portal-flight-detail": [
    {
      src: "screenshots/portal-flights-populated.png",
      alt: "Actual portal flight register with Scheduled, InProgress, and Completed records",
      title: "Lifecycle in the register",
      caption: "Flight status changes follow the linked work-order decision.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-flight-actions-scheduled.png",
      alt: "Actual scheduled-flight action menu",
      title: "Flight actions",
      caption: "Available actions are state- and permission-dependent.",
      kind: "portal",
    },
  ],
  "portal-resolve-duplicates": [
    {
      src: "screenshots/portal-flight-actions-scheduled.png",
      alt: "Actual flight action menu containing Resolve duplicates",
      title: "Start duplicate resolution",
      caption: "Open More actions and choose Resolve duplicates.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-resolve-duplicates.png",
      alt: "Actual duplicate-flight candidate dialog",
      title: "Candidate check",
      caption:
        "The portal compares the selected flight and reports when no compatible candidate exists.",
      kind: "portal",
    },
  ],
  "portal-completion-work-order": [
    {
      src: "screenshots/portal-returned-work-order-edit.png",
      alt: "Actual editable returned Completion work order details",
      title: "Completion details",
      caption: "A Returned work order is unlocked so its actual flight facts can be corrected.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-work-order-service-lines-return-to-ramp.png",
      alt: "Actual portal work-order service-line form",
      title: "Performed service lines",
      caption:
        "Record the service, performer, UTC interval, evidence, and optional Return to ramp flag.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-work-order-signature.png",
      alt: "Actual portal customer-signature work-order step",
      title: "Signature and update",
      caption: "The final step accepts a PNG signature before Create or Update.",
      kind: "portal",
    },
  ],
  "portal-cancellation-work-order": [
    {
      src: "screenshots/portal-flight-actions-scheduled.png",
      alt: "Actual scheduled-flight menu containing Cancel flight",
      title: "Choose Cancel flight",
      caption: "Cancellation begins from the Scheduled flight action menu.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-cancellation-work-order.png",
      alt: "Actual portal Cancellation work-order form",
      title: "Cancellation work order",
      caption: "Record the cancellation UTC timestamp and reason, then create it for review.",
      kind: "portal",
    },
  ],
  "portal-work-order-approval": [
    {
      src: "screenshots/portal-work-orders-populated.png",
      alt: "Actual work-order queue containing Submitted, Returned, and Approved records",
      title: "Review queue",
      caption: "The queue exposes every review state in one operational view.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-work-order-approved.png",
      alt: "Actual approved work order and confirmation",
      title: "Approval result",
      caption: "Approval assigns a station number, locks the work order, and settles the flight.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-work-order-return-dialog.png",
      alt: "Actual portal Return work order dialog",
      title: "Return for correction",
      caption: "Returning an approved work order requires a reason and reopens the flight.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-returned-work-order-edit.png",
      alt: "Actual unlocked Returned work-order editor",
      title: "Correct and approve again",
      caption: "The author edits the Returned record; a reviewer then approves it directly.",
      kind: "portal",
    },
  ],
  "mobile-my-flights": [
    {
      src: "screenshots/mobile-my-flights-assigned.png",
      alt: "Actual mobile My Flights assigned list",
      title: "My Flights",
      caption: "Regular flights appear for staff on the assigned roster.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-per-landing-list.png",
      alt: "Actual mobile Per Landing station list",
      title: "Per Landing",
      caption: "Per Landing flights are station-wide and do not use an assigned-staff roster.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-flight-action-sheet.png",
      alt: "Actual mobile assigned-flight action sheet",
      title: "Flight actions",
      caption: "Open a row to see the actions allowed by status and permission.",
      kind: "mobile",
    },
  ],
  "mobile-work-order-form": [
    {
      src: "screenshots/mobile-returned-work-order-edit.png",
      alt: "Actual mobile returned work-order flight-details step",
      title: "Flight details",
      caption: "Returned work opens as an editable four-step work-order form.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-work-order-service-lines.png",
      alt: "Actual mobile work-order service-lines step",
      title: "Service lines",
      caption: "Record only services actually performed.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-work-order-tasks.png",
      alt: "Actual mobile work-order tasks step",
      title: "Tasks",
      caption: "Add task timing, staff, resources, and evidence.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-work-order-signature-submit.png",
      alt: "Actual mobile work-order signature and submit step",
      title: "Signature and submit",
      caption: "Save a draft or submit it to the device outbox from the final step.",
      kind: "mobile",
    },
  ],
  "mobile-sync-center": [
    {
      src: "screenshots/mobile-work-order-draft-list.png",
      alt: "Actual mobile work-order draft list",
      title: "Local drafts",
      caption: "Drafts remain editable on the device until submitted.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-sync-center.png",
      alt: "Actual mobile Sync Center",
      title: "Sync Center",
      caption: "Confirm pending, sending, accepted, failed, and conflict delivery states here.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-drafts.png",
      alt: "Actual mobile drafts screen",
      title: "Draft workspace",
      caption: "A local save is not server acceptance; keep checking the outbox after Submit.",
      kind: "mobile",
    },
  ],
  "mobile-flight-actions": [
    {
      src: "screenshots/mobile-submitted-flight-actions.png",
      alt: "Actual mobile actions for a Submitted work order",
      title: "Submitted WO actions",
      caption: "Return to Ramp is available before approval when the action window and ownership checks pass.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-return-to-ramp.png",
      alt: "Actual mobile Return to Ramp form",
      title: "Return to Ramp",
      caption: "Append new service or task evidence, then submit it before a reviewer approves the WO.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-returned-flight-actions.png",
      alt: "Actual mobile actions for a Returned work order",
      title: "Returned WO actions",
      caption: "Returned work is unlocked for editing and can also receive eligible Return-to-Ramp evidence.",
      kind: "mobile",
    },
  ],
  "flight-mode-comparison": [
    {
      src: "screenshots/portal-flights-populated.png",
      alt: "Actual portal list with regular, Per Landing, and On Call badges",
      title: "Three flight modes",
      caption: "The live register distinguishes the modes beside the flight number.",
      kind: "portal",
    },
    {
      src: "screenshots/mobile-my-flights-assigned.png",
      alt: "Actual mobile assigned regular-flight list",
      title: "Regular flight visibility",
      caption: "A regular flight appears in My Flights for assigned staff.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-per-landing-lifecycle.png",
      alt: "Actual mobile Per Landing and On Call lifecycle list",
      title: "Per Landing and On Call",
      caption: "Both remain station-wide in the Per Landing mobile area.",
      kind: "mobile",
    },
  ],
  "portal-per-landing-extraction": [
    {
      src: "screenshots/portal-per-landing-extraction.png",
      alt: "Actual portal Per Landing extraction list",
      title: "Eligible extraction",
      caption:
        "Only In Progress Per Landing flights without performed service lines are eligible; On Call is excluded.",
      kind: "portal",
    },
    {
      src: "screenshots/mobile-per-landing-action-sheet.png",
      alt: "Actual mobile Per Landing flight action sheet",
      title: "Ramp execution",
      caption: "Station operators open Per Landing work from the dedicated mobile tab.",
      kind: "mobile",
    },
  ],
  "mobile-ad-hoc": [
    {
      src: "screenshots/mobile-work-order-flight-step.png",
      alt: "Actual mobile Create Ad Hoc Flight form",
      title: "Create Ad Hoc Flight",
      caption:
        "Capture the real customer, flight number, aircraft, tail, STA, and STD before performed work.",
      kind: "mobile",
    },
    {
      src: "screenshots/mobile-work-order-service-lines.png",
      alt: "Actual mobile Ad Hoc service-lines step",
      title: "Performed work",
      caption: "Add the services actually performed, then continue through tasks and signature.",
      kind: "mobile",
    },
  ],
  "master-data-countries": [
    {
      src: "screenshots/portal-master-data-create.png",
      alt: "Actual portal Create country form",
      title: "Create country",
      caption: "Enter unique ISO codes and names, then create the country.",
      kind: "portal",
    },
  ],
  "master-data-stations": [
    {
      src: "screenshots/portal-station-create.png",
      alt: "Actual portal Create station wizard",
      title: "Create station",
      caption: "Enter IATA/ICAO codes, station identity, city, and country before optional staff assignment.",
      kind: "portal",
    },
  ],
  "master-data-manpower": [
    {
      src: "screenshots/portal-manpower-create.png",
      alt: "Actual portal Create manpower type form",
      title: "Create manpower type",
      caption: "Manpower type and allowed services determine what staff can record as performed work.",
      kind: "portal",
    },
  ],
  "master-data-services": [
    {
      src: "screenshots/portal-service-create.png",
      alt: "Actual portal Create service form",
      title: "Create service",
      caption: "Create maintainable operational services for scheduling and performed-work entry.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-system-operation-type.png",
      alt: "Actual operation-type catalog with protected Ad Hoc",
      title: "Operation types",
      caption: "Ad Hoc is system-seeded; normal operation types are administrator-maintained.",
      kind: "portal",
    },
  ],
  "master-data-system-record": [
    {
      src: "screenshots/portal-system-service.png",
      alt: "Actual Aircraft Per Landing service marked System",
      title: "Protected service",
      caption: "Aircraft Per Landing is system-seeded and view-only.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-system-operation-type.png",
      alt: "Actual Ad Hoc operation type marked System",
      title: "Protected operation type",
      caption: "Ad Hoc is system-seeded and cannot be removed or edited.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-system-customer.png",
      alt: "Actual Unknown Customer marked System",
      title: "Protected customer",
      caption: "Unknown Customer is the protected fallback customer.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-system-role.png",
      alt: "Actual System Administrator role marked protected",
      title: "Protected role",
      caption: "System Administrator is the protected global administrator role.",
      kind: "portal",
    },
  ],
  "master-data-resources": [
    {
      src: "screenshots/portal-aircraft-type-create.png",
      alt: "Actual portal Create aircraft type form",
      title: "Create aircraft type",
      caption: "Add searchable aircraft identity used by Completion work orders.",
      kind: "portal",
    },
  ],
  "master-data-customers": [
    {
      src: "screenshots/portal-customer-create.png",
      alt: "Actual portal Create customer wizard",
      title: "Create customer",
      caption: "Capture airline identity, country, contact, address, and logo through the wizard.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-system-customer.png",
      alt: "Actual customer catalog with Unknown Customer marked System",
      title: "Customer catalog",
      caption: "Normal customers remain maintainable while Unknown Customer stays protected.",
      kind: "portal",
    },
  ],
  "master-data-staff": [
    {
      src: "screenshots/portal-staff-create.png",
      alt: "Actual portal Create staff member wizard",
      title: "Create staff member",
      caption: "Start with employee identity, station, and manpower type.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-staff-list.png",
      alt: "Actual portal staff-member register",
      title: "Staff register",
      caption: "Review station, manpower type, account email, and active status after creation.",
      kind: "portal",
    },
  ],
  "master-data-portal-access": [
    {
      src: "screenshots/portal-staff-portal-access.png",
      alt: "Actual staff Portal access tab with linked identity account",
      title: "Portal access",
      caption: "The staff record shows whether a portal account is linked and opens identity management.",
      kind: "portal",
    },
    {
      src: "screenshots/portal-role-editor-details.png",
      alt: "Actual portal custom-role editor",
      title: "Assign an account type",
      caption: "Create a role for the correct account type and access scope before assigning permissions.",
      kind: "portal",
    },
  ],
};

function ScreenshotGallery({ article }: { article: ManualArticle }) {
  const screenshots =
    screenshotLibrary[article.screenshotKey ?? article.id] ??
    screenshotLibrary["portal-flight-filters"];

  return (
    <section className="screenshot-section" aria-labelledby="observed-interface-title">
      <div className="section-heading-row">
        <div>
          <span className="micro-label">Observed interface</span>
          <h2 id="observed-interface-title">Screen reference</h2>
        </div>
        <span className="privacy-note">Captured from the running applications</span>
      </div>
      <div className="screenshot-grid">
        {screenshots.map((shot) => (
          <figure className={`screenshot-card ${shot.kind}`} key={shot.src}>
            <div className="screenshot-browserbar">
              <span />
              <span />
              <span />
              <small>{shot.title}</small>
            </div>
            <div className="screenshot-viewport">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={shot.src} alt={shot.alt} />
            </div>
            <figcaption>
              <span>{shot.caption}</span>
              {shot.href && (
                <a href={shot.href} target="_blank" rel="noreferrer">
                  {shot.linkLabel ?? "Open file"} <ArrowIcon />
                </a>
              )}
            </figcaption>
          </figure>
        ))}
      </div>
    </section>
  );
}

function RelevantMasterData({ articleId }: { articleId: string }) {
  const recordGroups: Record<string, readonly string[]> = {
    "master-countries": ["Countries"],
    "master-stations": ["Stations"],
    "master-manpower-licenses": ["Manpower types", "Licenses"],
    "master-services-operation-types": ["Services", "Operation types"],
    "master-aircraft-resources": [
      "Aircraft types",
      "Tools",
      "Materials",
      "General supports",
    ],
    "master-customers": ["Customers"],
    "master-staff": ["Staff"],
    "master-portal-access": ["Portal access"],
  };
  const wanted = recordGroups[articleId];
  const rows = wanted
    ? masterDataMatrix.filter((row) => wanted.includes(row.record))
    : masterDataMatrix;

  return (
    <section className="matrix-section" aria-labelledby="matrix-title">
      <div className="section-heading-row">
        <div>
          <span className="micro-label">Data map</span>
          <h2 id="matrix-title">What this catalog controls</h2>
        </div>
        <span className="visual-key">{rows.length} record {rows.length === 1 ? "type" : "types"}</span>
      </div>
      <div className="matrix-table-wrap">
        <table className="matrix-table">
          <thead>
            <tr>
              <th>Record</th>
              <th>Key fields</th>
              <th>Used by</th>
              <th>Controls</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <th scope="row">
                  <strong>{row.record}</strong>
                  <small>{row.location}</small>
                </th>
                <td>{row.keyFields.join(" · ")}</td>
                <td>{row.usedBy.join(" · ")}</td>
                <td>{row.controls.join(" · ")}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function ContextVisual({ article }: { article: ManualArticle }) {
  if (article.sectionId === "access-and-oversight") {
    return article.id === "role-personas-permissions" ? (
      <PersonaGuide />
    ) : (
      <OversightGuide />
    );
  }
  if (article.sectionId === "flight-operations") {
    return <FlightLifecycle />;
  }
  if (article.sectionId === "work-orders") {
    return <WorkOrderLifecycle />;
  }
  if (article.sectionId === "mobile-operations") {
    return article.id === "mobile-offline-sync" ? <MobileSyncGuide /> : null;
  }
  if (article.sectionId === "special-operations") {
    return article.id === "compare-flight-modes" ? (
      <FlightModeGuide />
    ) : (
      <SpecialOperationsGuide />
    );
  }
  return null;
}

function AudienceBadge({ audience }: { audience: ManualAudience }) {
  const labels: Record<ManualAudience, string> = {
    all: "Portal + mobile",
    portal: "Portal",
    mobile: "Mobile",
    administrator: "Administrator",
  };
  return <span className={`audience-badge audience-${audience}`}>{labels[audience]}</span>;
}

function ArticleReader({
  article,
  onSelect,
}: {
  article: ManualArticle;
  onSelect: (id: string) => void;
}) {
  const section = manualSections.find((item) => item.id === article.sectionId);
  const category = manualCategories.find((item) => item.id === article.categoryId);
  const related = articles
    .filter((item) => item.sectionId === article.sectionId && item.id !== article.id)
    .slice(0, 3);
  const showScreens = true;

  return (
    <article className="article-reader">
      <header className="article-hero">
        <div className="article-breadcrumb">
          <span>{section?.title}</span>
          <i>/</i>
          <span>{category?.title}</span>
        </div>
        <div className="article-title-row">
          <div>
            <span className="article-eyebrow">{article.eyebrow}</span>
            <h1>{article.title}</h1>
          </div>
          <span className="article-number">
            {String(articles.findIndex((item) => item.id === article.id) + 1).padStart(2, "0")}
          </span>
        </div>
        <p className="article-summary">{article.summary}</p>
        <div className="article-meta">
          <AudienceBadge audience={article.audience} />
          {article.location && (
            <span className="location-chip">
              <span aria-hidden="true">⌁</span>
              {article.location}
            </span>
          )}
        </div>
      </header>

      <div className="article-layout">
        <div className="article-body">
          <section className="steps-section" aria-labelledby="procedure-title">
            <div className="section-heading-row">
              <div>
                <span className="micro-label">Procedure</span>
                <h2 id="procedure-title">Do this in order</h2>
              </div>
              <span className="step-count">{article.steps.length} steps</span>
            </div>
            <ol className="procedure-list">
              {article.steps.map((step, index) => (
                <li key={step.id}>
                  <span className="step-index">
                    {String(index + 1).padStart(2, "0")}
                  </span>
                  <div>
                    <h3>{step.title}</h3>
                    <p>{step.body}</p>
                  </div>
                </li>
              ))}
            </ol>
          </section>

          {article.callouts.map((callout) => (
            <Callout tone={callout.tone} title={callout.title} key={callout.title}>
              {callout.body}
            </Callout>
          ))}

          <ContextVisual article={article} />
          {showScreens && <ScreenshotGallery article={article} />}
          {article.sectionId === "master-data" && (
            <RelevantMasterData articleId={article.id} />
          )}
        </div>

        <aside className="article-rail">
          <div className="rail-card">
            <span className="micro-label">In this guide</span>
            <ol>
              <li><a href="#procedure-title">Procedure</a></li>
              {article.sectionId === "master-data" && <li><a href="#matrix-title">Data map</a></li>}
              {showScreens && <li><a href="#observed-interface-title">Screen reference</a></li>}
            </ol>
          </div>
          <div className="rail-card related-card">
            <span className="micro-label">Continue reading</span>
            {related.map((item) => (
              <button key={item.id} onClick={() => onSelect(item.id)}>
                <span>{item.title}</span>
                <ArrowIcon />
              </button>
            ))}
          </div>
          <div className="rail-note">
            <span>UTC</span>
            <p>Operational times are UTC unless the screen explicitly says otherwise.</p>
          </div>
        </aside>
      </div>
    </article>
  );
}

function SearchResults({
  results,
  query,
  onSelect,
}: {
  results: readonly ManualArticle[];
  query: string;
  onSelect: (id: string) => void;
}) {
  return (
    <section className="search-results" aria-live="polite">
      <header>
        <span className="micro-label">Search results</span>
        <h1>
          {results.length} {results.length === 1 ? "guide" : "guides"} for “{query}”
        </h1>
        <p>Results include guide titles, procedures, keywords, and operational locations.</p>
      </header>
      {results.length > 0 ? (
        <div className="result-grid">
          {results.map((article) => (
            <button className="result-card" key={article.id} onClick={() => onSelect(article.id)}>
              <div>
                <AudienceBadge audience={article.audience} />
                <span className="result-eyebrow">{article.eyebrow}</span>
              </div>
              <h2>{article.title}</h2>
              <p>{article.summary}</p>
              <span className="result-open">
                Open guide <ArrowIcon />
              </span>
            </button>
          ))}
        </div>
      ) : (
        <div className="empty-results">
          <span>0</span>
          <h2>No exact match</h2>
          <p>Try a status, flight number concept, screen name, or master-data record.</p>
        </div>
      )}
    </section>
  );
}

export default function Home() {
  const [selectedArticleId, setSelectedArticleId] = useState(
    "portal-flight-status-actions",
  );
  const [query, setQuery] = useState("");
  const [audience, setAudience] = useState<"everything" | ManualAudience>("everything");
  const [menuOpen, setMenuOpen] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const syncFromHash = () => {
      const hash = window.location.hash.replace(/^#guide=/, "");
      if (hash && articles.some((article) => article.id === hash)) {
        setSelectedArticleId(hash);
      }
    };
    const handleKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const typing =
        target?.tagName === "INPUT" ||
        target?.tagName === "TEXTAREA" ||
        target?.isContentEditable;
      if (event.key === "/" && !typing) {
        event.preventDefault();
        searchRef.current?.focus();
      }
      if (event.key === "Escape") {
        setQuery("");
        searchRef.current?.blur();
      }
    };
    const hashTimer = window.setTimeout(syncFromHash, 0);
    window.addEventListener("hashchange", syncFromHash);
    window.addEventListener("keydown", handleKey);
    return () => {
      window.clearTimeout(hashTimer);
      window.removeEventListener("hashchange", syncFromHash);
      window.removeEventListener("keydown", handleKey);
    };
  }, []);

  const selectedArticle =
    articles.find((article) => article.id === selectedArticleId) ?? articles[0];
  const activeSectionId = selectedArticle.sectionId;

  const results = useMemo(() => {
    const needle = normalizeSearchText(query);
    if (!needle) return [];
    return articles.filter((article) => {
      if (
        audience !== "everything" &&
        article.audience !== "all" &&
        article.audience !== audience
      ) {
        return false;
      }
      const haystack = normalizeSearchText([
        article.title,
        article.eyebrow,
        article.summary,
        article.location ?? "",
        ...article.keywords,
        ...article.steps.flatMap((step) => [step.title, step.body]),
        ...article.callouts.flatMap((callout) => [callout.title, callout.body]),
      ].join(" "));
      return needle
        .split(/\s+/)
        .filter(Boolean)
        .every((term) => haystack.includes(term));
    });
  }, [audience, query]);

  const selectArticle = (id: string) => {
    setSelectedArticleId(id);
    setQuery("");
    setMenuOpen(false);
    window.history.replaceState(null, "", `#guide=${id}`);
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  const selectSection = (sectionId: string) => {
    const category = manualCategories.find((item) => item.sectionId === sectionId);
    const firstId = category?.articleIds[0];
    if (firstId) selectArticle(firstId);
  };

  return (
    <div className="manual-shell">
      <a className="skip-link" href="#main-content">Skip to guide</a>
      <aside className={`sidebar ${menuOpen ? "open" : ""}`} aria-label="Manual navigation">
        <div className="brand-block">
          <LogoMark />
          <div>
            <strong>NAGS</strong>
            <span>Operations field guide</span>
          </div>
          <button className="close-menu" onClick={() => setMenuOpen(false)} aria-label="Close navigation">
            ×
          </button>
        </div>

        <nav className="chapter-nav" aria-label="Guide chapters">
          <span className="nav-label">Manual contents</span>
          {manualSections.map((section, sectionIndex) => {
            const isActive = section.id === activeSectionId;
            const sectionCategories = manualCategories.filter(
              (category) => category.sectionId === section.id,
            );
            return (
              <div className={`chapter ${isActive ? "active" : ""}`} key={section.id}>
                <button className="chapter-button" onClick={() => selectSection(section.id)}>
                  <span>{String(sectionIndex + 1).padStart(2, "0")}</span>
                  <div>
                    <strong>{section.title}</strong>
                    <small>{section.eyebrow}</small>
                  </div>
                  <i aria-hidden="true">⌄</i>
                </button>
                {isActive && (
                  <div className="chapter-articles">
                    {sectionCategories.map((category) => (
                      <div key={category.id}>
                        <span>{category.title}</span>
                        {category.articleIds.map((id) => {
                          const item = articles.find((article) => article.id === id);
                          if (!item) return null;
                          return (
                            <button
                              className={item.id === selectedArticle.id ? "current" : ""}
                              key={item.id}
                              onClick={() => selectArticle(item.id)}
                            >
                              {item.title}
                            </button>
                          );
                        })}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            );
          })}
        </nav>

        <div className="sidebar-footer">
          <div>
            <span className="live-dot" />
            <strong>Operations reference</strong>
          </div>
          <p>Portal and Android application</p>
          <small>Product-aligned · July 2026</small>
        </div>
      </aside>

      {menuOpen && <button className="menu-scrim" onClick={() => setMenuOpen(false)} aria-label="Close menu" />}

      <div className="manual-workspace">
        <header className="topbar">
          <button className="menu-button" onClick={() => setMenuOpen(true)} aria-label="Open navigation">
            <span />
            <span />
            <span />
          </button>
          <label className="search-box">
            <SearchIcon />
            <input
              ref={searchRef}
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Search flights, work orders, master data…"
              aria-label="Search the user manual"
            />
            {query ? (
              <button type="button" onClick={() => setQuery("")} aria-label="Clear search">×</button>
            ) : (
              <kbd>/</kbd>
            )}
          </label>
          <div className="topbar-meta">
            <span>{articles.length} guides</span>
            <i />
            <span>{manualSections.length} chapters</span>
          </div>
        </header>

        <div className="audience-filter" aria-label="Filter search by audience">
          <span>Audience</span>
          {audienceOptions.map((option) => (
            <button
              key={option.id}
              className={audience === option.id ? "active" : ""}
              onClick={() => setAudience(option.id)}
            >
              {option.label}
            </button>
          ))}
        </div>

        <main id="main-content">
          {query.trim() ? (
            <SearchResults results={results} query={query.trim()} onSelect={selectArticle} />
          ) : (
            <ArticleReader article={selectedArticle} onSelect={selectArticle} />
          )}
        </main>

        <footer className="site-footer">
          <div>
            <LogoMark />
            <span>NAGS Operations field guide</span>
          </div>
          <p>Use the application as the source of truth for current records and permissions.</p>
        </footer>
      </div>
    </div>
  );
}
