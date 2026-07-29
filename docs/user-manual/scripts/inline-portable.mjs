import { readFile, readdir, writeFile } from "node:fs/promises";
import { extname, join, relative, resolve, sep } from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const projectDirectory = fileURLToPath(new URL("../", import.meta.url));
const defaultInputDirectory = resolve(projectDirectory, "release/manual");
const defaultOutputFile = resolve(
  projectDirectory,
  "release/nags-operations-field-guide.html",
);

const mediaTypes = new Map([
  [".avif", "image/avif"],
  [".css", "text/css"],
  [".gif", "image/gif"],
  [".html", "text/html"],
  [".jpeg", "image/jpeg"],
  [".jpg", "image/jpeg"],
  [".js", "text/javascript"],
  [".pdf", "application/pdf"],
  [".png", "image/png"],
  [".svg", "image/svg+xml"],
  [".webp", "image/webp"],
]);

function readOption(name) {
  const optionIndex = process.argv.indexOf(name);
  if (optionIndex === -1) {
    return undefined;
  }

  const value = process.argv[optionIndex + 1];
  if (!value || value.startsWith("--")) {
    throw new Error(`${name} requires a path.`);
  }

  return value;
}

async function listFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = await Promise.all(
    entries
      .sort((left, right) => left.name.localeCompare(right.name))
      .map(async (entry) => {
        const path = join(directory, entry.name);
        return entry.isDirectory() ? listFiles(path) : [path];
      }),
  );

  return files.flat();
}

function toWebPath(path) {
  return path.split(sep).join("/");
}

function encodeDataUrl(path, content) {
  const mediaType = mediaTypes.get(extname(path).toLowerCase());
  if (!mediaType) {
    throw new Error(`No media type is registered for ${path}.`);
  }

  return `data:${mediaType};base64,${content.toString("base64")}`;
}

async function inlineStyleSheets(html, inputDirectory) {
  const pattern =
    /<link\b(?=[^>]*\brel=["']stylesheet["'])(?=[^>]*\bhref=["']([^"']+)["'])[^>]*>/gi;
  const links = [...html.matchAll(pattern)];
  let result = html;

  for (const link of links) {
    const href = link[1];
    const cssPath = resolve(inputDirectory, href);
    const css = await readFile(cssPath, "utf8");
    result = result.replace(link[0], () => `<style>${css}</style>`);
  }

  return result;
}

async function inlineScripts(html, inputDirectory) {
  const pattern =
    /<script\b(?=[^>]*\bsrc=["']([^"']+)["'])[^>]*>\s*<\/script>/gi;
  const scripts = [...html.matchAll(pattern)];
  let result = html;
  const inlineScripts = [];

  for (const script of scripts) {
    const source = script[1];
    const scriptPath = resolve(inputDirectory, source);
    const javascript = (await readFile(scriptPath, "utf8")).replaceAll(
      "</script",
      "<\\/script",
    );
    result = result.replace(script[0], "");
    inlineScripts.push(`<script>${javascript}</script>`);
  }

  if (inlineScripts.length > 0) {
    const closingBodyIndex = result.lastIndexOf("</body>");
    if (closingBodyIndex === -1) {
      throw new Error("The static export is missing a closing body tag.");
    }

    result =
      result.slice(0, closingBodyIndex) +
      `${inlineScripts.join("\n")}\n` +
      result.slice(closingBodyIndex);
  }

  return result;
}

async function inlinePublicAssets(html, inputDirectory) {
  const files = await listFiles(inputDirectory);
  const publicAssets = files.filter((path) => {
    const relativePath = toWebPath(relative(inputDirectory, path));
    return (
      !relativePath.startsWith("assets/") &&
      relativePath !== "index.html"
    );
  });
  let result = html;

  for (const assetPath of publicAssets) {
    const relativePath = toWebPath(relative(inputDirectory, assetPath));
    const dataUrl = encodeDataUrl(assetPath, await readFile(assetPath));
    const references = [
      `./${relativePath}`,
      `/${relativePath}`,
      `"${relativePath}"`,
      `'${relativePath}'`,
      `\`${relativePath}\``,
    ];

    for (const reference of references) {
      const replacement =
        reference[0] === `"` || reference[0] === `'` || reference[0] === "`"
          ? `${reference[0]}${dataUrl}${reference[0]}`
          : dataUrl;
      result = result.split(reference).join(replacement);
    }
  }

  return result;
}

const requestedInputDirectory = readOption("--input-dir");
const requestedOutputFile = readOption("--output-file");
const inputDirectory = requestedInputDirectory
  ? resolve(process.cwd(), requestedInputDirectory)
  : defaultInputDirectory;
const outputFile = requestedOutputFile
  ? resolve(process.cwd(), requestedOutputFile)
  : defaultOutputFile;
const inputFile = resolve(inputDirectory, "index.html");

let html = await readFile(inputFile, "utf8");
html = await inlineStyleSheets(html, inputDirectory);
html = await inlineScripts(html, inputDirectory);
html = await inlinePublicAssets(html, inputDirectory);

const unresolvedAssetReference =
  /(?:src|href)=["'](?:\.?\/)?(?:assets|screenshots|downloads)\/|["'`](?:\.?\/)?(?:screenshots|downloads)\/[^"'`]+/i;

const unresolvedReference = html.match(unresolvedAssetReference)?.[0];

if (unresolvedReference) {
  throw new Error(
    `The standalone file still contains a local asset reference: ${unresolvedReference}`,
  );
}

await writeFile(outputFile, html);
process.stdout.write(`Email manual: ${outputFile}\n`);
