import { defineConfig } from 'orval'

// Generates typed API client + TanStack Query hooks from the backend OpenAPI document.
// Run with the API running locally: `npm run generate` (see package.json).
// The current hand-written client under src/shared/api + src/features/**/api.ts is the
// bootstrap; migrate features to the generated client incrementally.
export default defineConfig({
  operations: {
    input: 'http://localhost:5080/openapi/v1.json',
    output: {
      mode: 'tags-split',
      target: 'src/shared/api/generated',
      client: 'react-query',
      httpClient: 'axios',
      override: {
        mutator: {
          path: 'src/shared/api/client.ts',
          name: 'api',
        },
      },
    },
  },
})
