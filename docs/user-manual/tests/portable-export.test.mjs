import assert from "node:assert/strict";
import { access, readFile, readdir, stat } from "node:fs/promises";
import { join } from "node:path";
import test from "node:test";

const releaseDirectory = new URL("../release/", import.meta.url);
const staticDirectory = new URL("manual/", releaseDirectory);
const standaloneFile = new URL(
  "nags-operations-field-guide.html",
  releaseDirectory,
);

test("static export is relocatable beneath /manual/", async () => {
  const html = await readFile(new URL("index.html", staticDirectory), "utf8");

  assert.match(html, /NAGS Operations Field Guide/);
  assert.match(html, /src="\.\/assets\/manual-[^"]+\.js"/);
  assert.match(html, /href="\.\/assets\/manual-[^"]+\.css"/);
  assert.doesNotMatch(html, /(?:src|href)="\/(?!\/)/);

  const assets = await readdir(new URL("assets/", staticDirectory));
  const javascriptFile = assets.find((name) => name.endsWith(".js"));
  assert.ok(javascriptFile, "The static export must include its application bundle.");

  const javascript = await readFile(
    new URL(`assets/${javascriptFile}`, staticDirectory),
    "utf8",
  );
  assert.match(javascript, /screenshots\/portal-flights-populated\.png/);
  assert.match(javascript, /downloads\/work-order-KAI-0001\.pdf/);
  assert.doesNotMatch(javascript, /["'`]\/(?:screenshots|downloads)\//);

  await Promise.all([
    access(new URL("screenshots/portal-flights-populated.png", staticDirectory)),
    access(new URL("screenshots/mobile-return-to-ramp.png", staticDirectory)),
    access(new URL("downloads/work-order-KAI-0001.pdf", staticDirectory)),
  ]);
});

test("email export is a single classic-script HTML document", async () => {
  const [html, fileDetails] = await Promise.all([
    readFile(standaloneFile, "utf8"),
    stat(standaloneFile),
  ]);

  assert.ok(
    fileDetails.size > 5_000_000,
    "The standalone document should contain the real screenshot data.",
  );
  assert.match(html, /<style>[\s\S]+<\/style>/);
  assert.match(html, /<script>[\s\S]+<\/script>/);
  assert.match(html, /data:image\/png;base64,/);
  assert.match(html, /data:image\/svg\+xml;base64,/);
  assert.match(html, /data:application\/pdf;base64,/);
  assert.match(html, /Captured from the running applications/);
  assert.ok(
    html.indexOf('<div id="root"></div>') < html.indexOf("<script>"),
    "The classic script must run after the manual root element is parsed.",
  );
  assert.doesNotMatch(html, /<script\b[^>]*\bsrc=/i);
  assert.doesNotMatch(html, /<script\b[^>]*\btype=["']module["']/i);
  assert.doesNotMatch(html, /<link\b[^>]*\brel=["']stylesheet["']/i);
  assert.doesNotMatch(
    html,
    /["'`](?:\.?\/)?(?:screenshots|downloads)\/[^"'`]+/,
  );
});

test("static export contains every source screenshot", async () => {
  const sourceScreenshots = (
    await readdir(new URL("../public/screenshots/", import.meta.url))
  ).sort();
  const exportedScreenshots = (
    await readdir(new URL("screenshots/", staticDirectory))
  ).sort();

  assert.deepEqual(exportedScreenshots, sourceScreenshots);

  for (const screenshot of exportedScreenshots) {
    const details = await stat(
      join(
        new URL("screenshots/", staticDirectory).pathname,
        screenshot,
      ),
    );
    assert.ok(details.size > 0, `${screenshot} must not be empty.`);
  }
});
