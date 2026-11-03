import assert from "node:assert/strict";
import { access, readFile } from "node:fs/promises";
import test from "node:test";
import ts from "typescript";

async function render() {
  const workerUrl = new URL("../dist/server/index.js", import.meta.url);
  workerUrl.searchParams.set("test", `${process.pid}-${Date.now()}`);
  const { default: worker } = await import(workerUrl.href);
  return worker.fetch(
    new Request("http://localhost/", { headers: { accept: "text/html" } }),
    { ASSETS: { fetch: async () => new Response("Not found", { status: 404 }) } },
    { waitUntil() {}, passThroughOnException() {} },
  );
}

test("server-renders the current scheduling guide", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);
  const html = await response.text();
  assert.match(html, /NAGS Operations Field Guide/i);
  assert.match(html, /Search the user manual/i);
  assert.match(html, /Flight scheduling baseline/i);
  assert.match(html, /Android flight access/i);
  assert.match(html, /employee invitations/i);
  assert.doesNotMatch(html, /Mobile is not supported|Android source uses the earlier operational API/i);
  assert.doesNotMatch(html, /Follow the flight lifecycle|codex-preview|Your site is taking shape/i);
});

test("manual navigation resolves every category and article exactly once", async () => {
  const source = await readFile(new URL("../app/manual-data.ts", import.meta.url), "utf8");
  const javascript = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext },
  }).outputText;
  const { manualSections, manualCategories, manualArticles } = await import(
    `data:text/javascript;base64,${Buffer.from(javascript).toString("base64")}`
  );
  const allIds = [...manualSections, ...manualCategories, ...manualArticles].map(item => item.id);
  assert.equal(new Set(allIds).size, allIds.length);
  for (const section of manualSections) {
    for (const id of section.categoryIds) {
      assert.equal(manualCategories.filter(item => item.id === id && item.sectionId === section.id).length, 1);
    }
  }
  for (const category of manualCategories) {
    assert.ok(manualSections.some(item => item.id === category.sectionId && item.categoryIds.includes(category.id)));
    for (const id of category.articleIds) {
      assert.equal(manualArticles.filter(item => item.id === id && item.categoryId === category.id && item.sectionId === category.sectionId).length, 1);
    }
  }
  for (const article of manualArticles) {
    assert.ok(manualCategories.some(item => item.id === article.categoryId && item.articleIds.includes(article.id)));
  }
  for (const id of ["current-scope", "schedule-flight", "schedule-many-flights", "find-edit-flight", "android-browse-flights", "android-invite-employees", "android-account-notifications", "flight-access", "master-data-overview"]) {
    assert.ok(manualArticles.some(item => item.id === id));
  }
});

test("all screenshot references point to existing genuine captures", async () => {
  const page = await readFile(new URL("../app/page.tsx", import.meta.url), "utf8");
  const captures = [...page.matchAll(/src: "(screenshots\/[^\"]+)"/g)].map(match => match[1]);
  assert.ok(captures.length > 0);
  await Promise.all(captures.map(capture => access(new URL(`../public/${capture}`, import.meta.url))));
});
