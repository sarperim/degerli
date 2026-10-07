import { describe, expect, it } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'

import type { MacroResponse, ProblemDetails } from '@/lib/api/contracts'
import { catalogValue } from '@/test/i18n'
import {
  API_BASE,
  marketMacroHandler,
  problemHandler,
  STALE_MACRO_FIXTURE,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'

async function fetchMacro(): Promise<MacroResponse> {
  const response = await fetch(`${API_BASE}/market/macro`)
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`)
  }
  return (await response.json()) as MacroResponse
}

/** Minimal consumer of the API contract — the SPA's real query layer arrives with domain tickets. */
function MacroStrip() {
  const { t } = useTranslation()
  const { data, isPending, isError } = useQuery({
    queryKey: ['market', 'macro'],
    queryFn: fetchMacro,
  })

  if (isPending) {
    return <span>{t('common.loading')}</span>
  }
  if (isError) {
    return <span role="alert">{t('common.error')}</span>
  }

  return (
    <ul>
      {data.series.map((series) => (
        <li key={series.code} data-testid={`macro-${series.code}`}>
          {series.code}: {series.value}
          {series.unit}
        </li>
      ))}
    </ul>
  )
}

/**
 * Sample L3 MSW-backed query test (test strategy §5.3, decision B).
 *
 * Exercises the TanStack Query test wrapper against typed MSW handlers that
 * mirror the `03-api-design.md` payload shapes and the honest-data envelope.
 */
describe('MSW-backed query (harness sample)', () => {
  it('resolves a typed honest-data-envelope response through the query wrapper', async () => {
    server.use(marketMacroHandler())

    renderWithProviders(<MacroStrip />)

    expect(await screen.findByTestId('macro-TUIK_CPI')).toHaveTextContent(
      'TUIK_CPI: 58.4%',
    )
    expect(screen.getByTestId('macro-ENAG_CPI')).toHaveTextContent(
      'ENAG_CPI: 61.2%',
    )
  })

  it('reflects a stale envelope served by a handler override', async () => {
    server.use(marketMacroHandler(STALE_MACRO_FIXTURE))

    renderWithProviders(<MacroStrip />)

    const row = await screen.findByTestId('macro-GOLD')
    expect(row).toHaveTextContent('GOLD: 2450.75')
    expect(STALE_MACRO_FIXTURE.stale).toBe(true)
  })

  it('renders catalog error copy for a typed RFC 7807 problem response', async () => {
    server.use(problemHandler(`${API_BASE}/market/macro`, 'INTERNAL'))

    renderWithProviders(<MacroStrip />)

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(
        catalogValue('tr', 'common.error'),
      ),
    )
  })

  it('serves the error-code contract as typed problem+json', async () => {
    server.use(
      problemHandler(`${API_BASE}/market/macro`, 'RATE_LIMITED', {
        params: { retryAfter: 30 },
      }),
    )

    const response = await fetch(`${API_BASE}/market/macro`)
    const body = (await response.json()) as ProblemDetails

    expect(response.status).toBe(429)
    expect(response.headers.get('content-type')).toContain('application/problem+json')
    expect(body.code).toBe('RATE_LIMITED')
    expect(body.params).toMatchObject({ retryAfter: 30 })
    expect(body.traceId).toBeTruthy()
  })
})
