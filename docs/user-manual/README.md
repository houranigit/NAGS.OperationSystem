# NAGS Operations Field Guide

A searchable user manual for the Operations System portal and Android
application. The manual source, genuine application screenshots, and portable
release files all live in this project so they can be versioned and updated with
the product.

The guide covers:

- flight scheduling, assignment, invitation, and lifecycle;
- Completion and Cancellation work-order authoring, approval, return, merge,
  Return to Ramp, printing, and offline delivery;
- regular, Per Landing, On Call, and Ad Hoc operations;
- roles, permissions, dashboards, reports, and protected system records;
- the complete master-data catalog.

## Ready-to-use files

Run:

```bash
npm install
npm run export:manual
```

The command creates two release formats:

| Output | Use |
| --- | --- |
| `release/nags-operations-field-guide.html` | Portal release resource and one-file email attachment |
| `release/manual/` | Temporary static folder for any ordinary web server |

Commit the single-file HTML with the source. The intermediate `release/manual/`
folder is reproducible and ignored to avoid committing a second copy of every
screenshot.

### Add it to the portal release

The Blazor portal embeds `release/nags-operations-field-guide.html` into its
assembly. Every normal `dotnet build` or `dotnet publish` therefore includes the
manual without requiring Node.js on the release server.

After sign-in, users can open **User manual** from the main navigation or account
menu. The portal serves the embedded copy at:

- `/manual` — open the searchable manual;
- `/manual/index.html` — equivalent direct path;
- `/manual/download` — download the email-ready HTML file.

The manual endpoint is part of the portal host, but it is not a permission gate:
anyone who can reach the portal URL can reach `/manual`. Deploy it within the
same network boundary as the portal.

For a different portal or static web server, build the folder directly into its
dedicated manual directory:

```bash
npm run export:static -- --out-dir /absolute/path/to/wwwroot/manual
```

The exporter empties the exact output directory before rebuilding it, so point
`--out-dir` only at the dedicated manual directory.

### Send it by email

In the portal account menu, select **Download manual**, or attach
`release/nags-operations-field-guide.html` directly from the repository. The
recipient should download the attachment and open it in Chrome, Edge, Safari, or
Firefox. It requires no internet connection or application sign-in because the
styling, search application, screenshots, and sample work-order PDF are embedded
in the file.

Email applications normally do not run interactive HTML inside the message
preview; the attachment must be opened in a browser. The file is approximately
11 MB, so confirm the recipient's email attachment limit.

To create the email file from an existing static export at a custom location:

```bash
node scripts/inline-portable.mjs \
  --input-dir /absolute/path/to/manual \
  --output-file /absolute/path/to/nags-operations-field-guide.html
```

## Update the manual

1. Update guide content in `app/manual-data.ts` and shared rendering in
   `app/page.tsx`.
2. Replace or add screenshots in `public/screenshots/` using direct captures
   from the current portal or Android application. Do not create simulated
   application screens.
3. Update the screenshot references in `app/page.tsx`.
4. Run `npm run lint` and `npm run test`.
5. Open `release/nags-operations-field-guide.html` in a browser and spot-check
   search, navigation, screenshots, and the sample PDF.
6. Commit the source, genuine screenshots, and refreshed single-file HTML
   together. A subsequent portal build or publish embeds that exact file.

The portable entry point in `portable/` reuses `app/page.tsx`,
`app/manual-data.ts`, and `app/globals.css`; it is not a second copy of the
manual. `scripts/inline-portable.mjs` deterministically converts the static
build into the single-file attachment.

## Run the hosted development version

Requires Node.js `>=22.13.0`.

```bash
npm install
npm run dev
```

Open `http://localhost:3000`.

## Validate

```bash
npm run lint
npm test
```

For a faster portable-export-only check:

```bash
npm run test:portable
```

The manual is intentionally read-only and contains no application credentials
or production operational data. Every interface image under
`public/screenshots/` is a direct capture from the running local portal or
Android application using demo records; none of the application screens are
illustrated or simulated.
