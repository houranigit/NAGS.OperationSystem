import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";

const projectRoot = fileURLToPath(new URL(".", import.meta.url));
const portableRoot = fileURLToPath(new URL("./portable", import.meta.url));
const publicDirectory = fileURLToPath(new URL("./public", import.meta.url));
const defaultOutputDirectory = fileURLToPath(
  new URL("./release/manual", import.meta.url),
);

export default defineConfig({
  root: portableRoot,
  base: "./",
  publicDir: publicDirectory,
  plugins: [react()],
  build: {
    outDir:
      process.env.NAGS_MANUAL_OUTPUT_DIRECTORY ?? defaultOutputDirectory,
    emptyOutDir: true,
    cssCodeSplit: false,
    rollupOptions: {
      output: {
        codeSplitting: false,
        format: "iife",
        entryFileNames: "assets/manual-[hash].js",
        assetFileNames: "assets/manual-[hash][extname]",
      },
    },
  },
  resolve: {
    alias: {
      "@manual": projectRoot,
    },
  },
});
