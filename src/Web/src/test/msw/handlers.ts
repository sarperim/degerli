import { http, HttpResponse } from 'msw'

import {
  API_ERROR_STATUS,
  API_ERROR_TITLE,
  type ApiErrorCode,
  type MacroResponse,
  type ProblemDetails,
} from '@/lib/api/contracts'

/** API prefix from `03-api-design.md` §1. */
export const API_BASE = '/api/v1'

/** Canonical problem `type` base (`03` §7). */
export const ERROR_TYPE_BASE = 'https://degerli.test/api/v1/errors'

type HttpMethod = 'get' | 'post' | 'put' | 'patch' | 'delete'

/** Deterministic fixture: a fresh macro envelope with both inflation measures. */
export const FRESH_MACRO_FIXTURE: MacroResponse = {
  asOf: '2026-10-06',
  stale: false,
  series: [
    {
      code: 'TUIK_CPI',
      value: 58.4,
      unit: '%',
      source: 'TUIK',
      asOf: '2026-10-03',
      stale: false,
      state: 'available',
    },
    {
      code: 'ENAG_CPI',
      value: 61.2,
      unit: '%',
      source: 'ENAG',
      asOf: '2026-10-03',
      stale: false,
      state: 'available',
    },
  ],
}

/** Deterministic fixture: a stale envelope with one unavailable series. */
export const STALE_MACRO_FIXTURE: MacroResponse = {
  asOf: '2026-10-06',
  stale: true,
  series: [
    {
      code: 'GOLD',
      value: 2450.75,
      unit: 'TRY/g',
      source: 'CBRT',
      asOf: '2026-09-26',
      stale: true,
      state: 'available',
    },
    {
      code: 'ENAG_CPI',
      value: null,
      unit: '%',
      source: 'ENAG',
      asOf: '2026-09-03',
      stale: true,
      state: 'unavailable',
    },
  ],
}

/** Convert an error code to the URL slug used in the problem `type`. */
export function errorTypeSlug(code: ApiErrorCode): string {
  return code.toLowerCase().replace(/_/g, '-')
}

/**
 * Build a typed RFC 7807 `application/problem+json` response from the `03` §7
 * error-code contract — status and title are derived from the code, never
 * hand-written per handler.
 */
export function problemResponse(
  code: ApiErrorCode,
  options: { params?: Record<string, unknown>; traceId?: string } = {},
): HttpResponse<ProblemDetails> {
  const status = API_ERROR_STATUS[code]
  const body: ProblemDetails = {
    type: `${ERROR_TYPE_BASE}/${errorTypeSlug(code)}`,
    title: API_ERROR_TITLE[code],
    status,
    code,
    ...(options.params ? { params: options.params } : {}),
    traceId: options.traceId ?? '00-test-0000000000000000-01',
  }

  return HttpResponse.json(body, {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

/** Register a request handler that always answers with the given problem. */
export function problemHandler(
  path: string,
  code: ApiErrorCode,
  options: {
    params?: Record<string, unknown>
    traceId?: string
    method?: HttpMethod
  } = {},
) {
  const resolve = () => problemResponse(code, options)

  switch (options.method ?? 'get') {
    case 'post':
      return http.post(path, resolve)
    case 'put':
      return http.put(path, resolve)
    case 'patch':
      return http.patch(path, resolve)
    case 'delete':
      return http.delete(path, resolve)
    case 'get':
    default:
      return http.get(path, resolve)
  }
}

/** `GET /api/v1/market/macro` handler, typed against `MacroResponse`. */
export function marketMacroHandler(body: MacroResponse = FRESH_MACRO_FIXTURE) {
  return http.get(`${API_BASE}/market/macro`, () => HttpResponse.json(body))
}
