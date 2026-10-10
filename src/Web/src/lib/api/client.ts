import type { ApiErrorCode, ProblemDetails } from '@/lib/api/contracts'

/**
 * Minimal same-origin JSON client for the `/api/v1` surface (`03-api-design.md`).
 *
 * The SPA is served same-origin with the API (in dev/e2e via the Vite `/api`
 * proxy configured in `vite.config.ts`; in production Caddy fronts both under one
 * host). Responses are language-neutral (`03` §1.1): the client renders all
 * user-facing text from i18n catalogs, never from the payload.
 */

/** API prefix from `03-api-design.md` §1. */
export const API_BASE = '/api/v1'

/**
 * Error raised for a non-2xx response. Carries the RFC 7807 `code`/`status` so
 * the UI can render plain-language copy without leaking technical detail
 * (UXR-G-003).
 */
export class ApiRequestError extends Error {
  readonly status: number
  readonly code: ApiErrorCode | 'UNKNOWN'

  constructor(status: number, code: ApiErrorCode | 'UNKNOWN', message: string) {
    super(message)
    this.name = 'ApiRequestError'
    this.status = status
    this.code = code
  }
}

/** True when the request was aborted (component unmount / query cancel). */
export function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

/** GET a typed JSON resource, throwing a typed {@link ApiRequestError} on failure. */
export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: { Accept: 'application/json' },
    signal,
  })

  if (!response.ok) {
    let code: ApiErrorCode | 'UNKNOWN' = 'UNKNOWN'
    try {
      const problem = (await response.json()) as Partial<ProblemDetails>
      if (typeof problem.code === 'string') code = problem.code
    } catch {
      // Non-JSON error body — keep UNKNOWN; the status still drives the copy.
    }
    throw new ApiRequestError(response.status, code, `HTTP ${response.status}`)
  }

  return (await response.json()) as T
}
