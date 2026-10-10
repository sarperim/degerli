import { beforeEach, describe, expect, it } from 'vitest'
import { http, HttpResponse } from 'msw'

import { ApiError, apiFetch, clearCsrfToken } from '@/lib/api/client'
import {
  API_BASE,
  CSRF_TOKEN_FIXTURE,
  problemResponse,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Antiforgery contract (`03` §2; B3). `CsrfProtectionMiddleware` rejects every
 * unsafe `/api/v1` request without a valid `X-CSRF-Token`; these tests pin the
 * client's fetch/cache/attach and the post-sign-in refetch-and-retry-once.
 */
describe('apiFetch antiforgery', () => {
  beforeEach(() => {
    clearCsrfToken()
  })

  it('fetches and attaches X-CSRF-Token to unsafe requests', async () => {
    let seen: string | null = null
    server.use(
      http.post(`${API_BASE}/auth/resend-verification`, ({ request }) => {
        seen = request.headers.get(CSRF_TOKEN_FIXTURE.headerName)
        return HttpResponse.json({ verified: false })
      }),
    )

    await apiFetch('/auth/resend-verification', { method: 'POST' })

    expect(seen).toBe(CSRF_TOKEN_FIXTURE.token)
  })

  it('does not attach the token to safe requests', async () => {
    let seen: string | null = 'unset'
    server.use(
      http.get(`${API_BASE}/auth/session`, ({ request }) => {
        seen = request.headers.get(CSRF_TOKEN_FIXTURE.headerName)
        return HttpResponse.json({ authenticated: false })
      }),
    )

    await apiFetch('/auth/session')

    expect(seen).toBeNull()
  })

  it('refetches the token and retries once after a reason:"csrf" rejection', async () => {
    let csrfCalls = 0
    server.use(
      http.get(`${API_BASE}/auth/csrf-token`, () => {
        csrfCalls += 1
        return HttpResponse.json({
          token: `token-${csrfCalls}`,
          headerName: 'X-CSRF-Token',
        })
      }),
      http.post(`${API_BASE}/auth/verify-email`, ({ request }) => {
        if (request.headers.get('X-CSRF-Token') === 'token-1') {
          return problemResponse('VALIDATION_FAILED', { params: { reason: 'csrf' } })
        }
        return HttpResponse.json({ verified: true })
      }),
    )

    const result = await apiFetch<{ verified: boolean }>('/auth/verify-email', {
      method: 'POST',
      body: JSON.stringify({ token: 't' }),
    })

    expect(result).toEqual({ verified: true })
    expect(csrfCalls).toBe(2)
  })

  it('surfaces a persistent csrf rejection after the single retry', async () => {
    server.use(
      http.post(`${API_BASE}/auth/verify-email`, () =>
        problemResponse('VALIDATION_FAILED', { params: { reason: 'csrf' } }),
      ),
    )

    await expect(
      apiFetch('/auth/verify-email', {
        method: 'POST',
        body: JSON.stringify({ token: 't' }),
      }),
    ).rejects.toBeInstanceOf(ApiError)
  })
})
