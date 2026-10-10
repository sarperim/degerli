import type { ProblemDetails } from '@/lib/api/contracts'

/** API prefix from `03-api-design.md` §1. */
export const API_BASE = '/api/v1'

/**
 * Typed error for a non-2xx API response (`03` §7).
 *
 * Carries the parsed RFC 7807 body so callers can branch on the stable machine
 * `code` (e.g. `EMAIL_TAKEN`) rather than on status text; raw technical detail is
 * never rendered to users (UXR-G-003).
 */
export class ApiError extends Error {
  readonly problem: ProblemDetails | null

  constructor(problem: ProblemDetails | null, status: number) {
    super(problem?.title ?? `HTTP ${status}`)
    this.name = 'ApiError'
    this.problem = problem
  }

  /** Stable machine error code, or `null` for a non-contract failure. */
  get code(): ProblemDetails['code'] | null {
    return this.problem?.code ?? null
  }
}

/** True when `error` is an `ApiError` carrying the given machine code. */
export function isApiErrorCode(error: unknown, code: ProblemDetails['code']): boolean {
  return error instanceof ApiError && error.code === code
}

async function parseProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
}

/**
 * Fetch a JSON API resource, throwing {@link ApiError} on a non-2xx response.
 *
 * `204 no-content` and empty bodies resolve to `undefined`; callers that expect a
 * payload pass the same call to a typed wrapper.
 */
export async function apiFetch<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: {
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...init.headers,
    },
  })

  if (!response.ok) {
    throw new ApiError(await parseProblem(response), response.status)
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}
