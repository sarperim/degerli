import { describe, expect, it } from 'vitest'

import { ApiError, ApiRequestError } from '@/lib/api/client'
import type { ProblemDetails } from '@/lib/api/contracts'
import { shouldRetryQuery } from '@/query/queryClient'

function makeProblem(code: ProblemDetails['code'], status: number): ProblemDetails {
  return {
    type: `https://degerli.test/api/v1/errors/${code.toLowerCase()}`,
    title: code,
    status,
    code,
    traceId: '00-test-0000000000000000-01',
  }
}

describe('shouldRetryQuery', () => {
  it('does not retry ApiError on 429, RATE_LIMITED, 401, or 403', () => {
    expect(shouldRetryQuery(0, new ApiError(null, 429))).toBe(false)
    expect(
      shouldRetryQuery(0, new ApiError(makeProblem('RATE_LIMITED', 503), 503)),
    ).toBe(false)
    expect(shouldRetryQuery(0, new ApiError(null, 401))).toBe(false)
    expect(shouldRetryQuery(0, new ApiError(null, 403))).toBe(false)
  })

  it('does not retry ApiRequestError on 429, 401, or 403', () => {
    expect(shouldRetryQuery(0, new ApiRequestError(429, 'RATE_LIMITED', 'HTTP 429'))).toBe(
      false,
    )
    expect(
      shouldRetryQuery(0, new ApiRequestError(401, 'UNAUTHENTICATED', 'HTTP 401')),
    ).toBe(false)
    expect(shouldRetryQuery(0, new ApiRequestError(403, 'FORBIDDEN', 'HTTP 403'))).toBe(
      false,
    )
  })

  it('retries once on 500 at failureCount 0 and stops at failureCount 1', () => {
    expect(shouldRetryQuery(0, new ApiError(null, 500))).toBe(true)
    expect(shouldRetryQuery(0, new ApiRequestError(500, 'INTERNAL', 'HTTP 500'))).toBe(
      true,
    )
    expect(shouldRetryQuery(1, new ApiError(null, 500))).toBe(false)
    expect(shouldRetryQuery(1, new ApiRequestError(500, 'INTERNAL', 'HTTP 500'))).toBe(
      false,
    )
  })
})
