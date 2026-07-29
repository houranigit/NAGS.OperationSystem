import assert from "node:assert/strict";
import { access, readFile } from "node:fs/promises";
import test from "node:test";

async function render() {
  const workerUrl = new URL("../dist/server/index.js", import.meta.url);
  workerUrl.searchParams.set("test", `${process.pid}-${Date.now()}`);
  const { default: worker } = await import(workerUrl.href);

  return worker.fetch(
    new Request("http://localhost/", {
      headers: { accept: "text/html" },
    }),
    {
      ASSETS: {
        fetch: async () => new Response("Not found", { status: 404 }),
      },
    },
    {
      waitUntil() {},
      passThroughOnException() {},
    },
  );
}

test("server-renders the operations field guide", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);

  const html = await response.text();
  assert.match(html, /NAGS Operations Field Guide/i);
  assert.match(html, /Search the user manual/i);
  assert.match(html, /Follow the flight lifecycle/i);
  assert.doesNotMatch(html, /codex-preview|Your site is taking shape/i);
});

test("manual source covers the requested operating scenarios", async () => {
  const [page, data, layout, packageJson] = await Promise.all([
    readFile(new URL("../app/page.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/manual-data.ts", import.meta.url), "utf8"),
    readFile(new URL("../app/layout.tsx", import.meta.url), "utf8"),
    readFile(new URL("../package.json", import.meta.url), "utf8"),
  ]);

  for (const expected of [
    "System Administrator",
    "Dispatcher",
    "Station Operator",
    "Invite staff",
    "Return to ramp",
    "Operations dashboard",
    "Print WO",
    "Aircraft Per Landing",
    "On Call",
    "protected system records",
  ]) {
    assert.match(data, new RegExp(expected, "i"));
  }

  assert.match(page, /Search the user manual/);
  assert.match(page, /FlightModeGuide/);
  assert.match(page, /PersonaGuide/);
  assert.match(page, /Captured from the running applications/);
  assert.doesNotMatch(page, /phone-frame|mobile-guide|Training-safe capture/);
  assert.match(layout, /NAGS Operations Field Guide/);
  assert.match(packageJson, /"name": "nags-operations-field-guide"/);
  assert.doesNotMatch(packageJson, /react-loading-skeleton/);
});

test("manual ships the real portal and mobile lifecycle captures", async () => {
  const requiredCaptures = [
    "portal-flights-populated.png",
    "portal-flight-staff-assignment.png",
    "portal-invite-employee.png",
    "portal-work-orders-populated.png",
    "portal-work-order-approved.png",
    "portal-work-order-return-dialog.png",
    "portal-returned-work-order-edit.png",
    "portal-per-landing-extraction.png",
    "portal-operations-dashboard-populated.png",
    "mobile-my-flights-assigned.png",
    "mobile-invite-teammates.png",
    "mobile-work-order-signature-submit.png",
    "mobile-return-to-ramp.png",
    "mobile-sync-center.png",
  ];

  await Promise.all(
    requiredCaptures.map((capture) =>
      access(new URL(`../public/screenshots/${capture}`, import.meta.url)),
    ),
  );
});
