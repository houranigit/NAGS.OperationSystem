import { resolve } from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { build } from "vite";

const projectDirectory = fileURLToPath(new URL("../", import.meta.url));
const defaultOutputDirectory = resolve(projectDirectory, "release/manual");

function readOption(name) {
  const optionIndex = process.argv.indexOf(name);
  if (optionIndex === -1) {
    return undefined;
  }

  const value = process.argv[optionIndex + 1];
  if (!value || value.startsWith("--")) {
    throw new Error(`${name} requires a directory path.`);
  }

  return value;
}

const requestedOutputDirectory = readOption("--out-dir");
const outputDirectory = requestedOutputDirectory
  ? resolve(process.cwd(), requestedOutputDirectory)
  : defaultOutputDirectory;

process.env.NAGS_MANUAL_OUTPUT_DIRECTORY = outputDirectory;

await build({
  configFile: resolve(projectDirectory, "vite.portable.config.ts"),
});

process.stdout.write(`Static manual: ${outputDirectory}\n`);
