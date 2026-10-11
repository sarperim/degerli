import type {
  ApiErrorCode,
  CsrfTokenResponse,
  ProblemDetails,
} from '@/lib/api/contracts'

/**
 * Same-origin JSON client for the `/api/v1` surface (`03-api-design.md`).
 *
 * The SPA is served same-origin with the API (in dev/e2e via the Vite `/api`
 * proxy configured in `vite.config.ts`; in production Caddy fronts both under one
 * host). Responses are language-neutral (`03` §1.1): the client renders all
 * user-facing text from i18n catalogs, never from the payload.
 *
 * Two entry points share this module: {@link apiGet} — the minimal GET helper the
 * read-only research surfaces (stock list / search, TKT-res-007) use — and the
 * general {@link apiFetch}, which also carries the antiforgery token for the
 * identity mutations (`03` §2/§6, TKT-acc-007).
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

/** Methods the antiforgery middleware validates (`03` §2); safe methods are exempt. */
const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

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

/*
 * Antiforgery (`03` §2): `CsrfProtectionMiddleware` rejects every unsafe `/api/v1`
 * request without a valid `X-CSRF-Token`. The token is issued by the anonymous
 * `GET /api/v1/auth/csrf-token` and validated against the caller's identity, so it
 * is cached and refetched after a mutation changes that identity.
 */
let csrfToken: CsrfTokenResponse | null = null
let csrfTokenRequest: Promise<CsrfTokenResponse> | null = null

/** Drop the cached antiforgery token so the next unsafe call refetches it. */
export function clearCsrfToken(): void {
  csrfToken = null
  csrfTokenRequest = null
}

/** Fetch and cache the antiforgery token + header name (`GET /auth/csrf-token`). */
export async function fetchCsrfToken(): Promise<CsrfTokenResponse> {
  if (csrfToken) {
    return csrfToken
  }
  csrfTokenRequest ??= (async () => {
    const response = await fetch(`${API_BASE}/auth/csrf-token`, {
      credentials: 'same-origin',
    })
    if (!response.ok) {
      throw new ApiError(await parseProblem(response), response.status)
    }
    const token = (await response.json()) as CsrfTokenResponse
    csrfToken = token
    return token
  })()

  try {
    return await csrfTokenRequest
  } finally {
    csrfTokenRequest = null
  }
}

/** True when a problem is the middleware's CSRF rejection (`reason:"csrf"`). */
function isCsrfRejection(problem: ProblemDetails | null): boolean {
  return problem?.code === 'VALIDATION_FAILED' && problem.params?.reason === 'csrf'
}

/**
 * Fetch a JSON API resource, throwing {@link ApiError} on a non-2xx response.
 *
 * Unsafe methods carry the antiforgery header automatically; a `reason:"csrf"`
 * rejection invalidates the (now identity-stale) token, refetches it and retries
 * once — the case after a mutation such as register signs the caller in.
 *
 * `204 no-content` and empty bodies resolve to `undefined`; callers that expect a
 * payload pass the same call to a typed wrapper.
 */
export async function apiFetch<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const method = (init.method ?? 'GET').toUpperCase()
  const needsCsrf = UNSAFE_METHODS.has(method)

  const send = async (): Promise<Response> => {
    const headers = new Headers(init.headers)
    if (init.body && !headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json')
    }
    if (needsCsrf) {
      const { token, headerName } = await fetchCsrfToken()
      headers.set(headerName, token)
    }
    return fetch(`${API_BASE}${path}`, {
      ...init,
      credentials: 'same-origin',
      headers,
    })
  }

  let response = await send()
  let problem: ProblemDetails | null = null

  if (needsCsrf && response.status === 400) {
    problem = await parseProblem(response)
    if (isCsrfRejection(problem)) {
      clearCsrfToken()
      problem = null
      response = await send()
    }
  }

  if (!response.ok) {
    throw new ApiError(problem ?? (await parseProblem(response)), response.status)
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}
