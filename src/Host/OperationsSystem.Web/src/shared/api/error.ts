import { isAxiosError } from 'axios'

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

/** Extracts a human-readable message from a ProblemDetails error response. */
export function extractApiError(error: unknown, fallback = 'Something went wrong.'): string {
  if (isAxiosError(error)) {
    const data = error.response?.data as ProblemDetails | undefined
    if (data?.errors) {
      const first = Object.values(data.errors)[0]
      if (Array.isArray(first) && first.length > 0) return first[0]
    }
    return data?.detail ?? data?.title ?? fallback
  }
  return fallback
}
