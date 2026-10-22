import { beforeEach, describe, expect, it } from 'vitest'
import { act, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'

import type { StockOverviewResponse } from '@/lib/api/contracts'
import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import {
  fill,
  PUBLISHED_OVERVIEW,
  renderStockPage,
  resetStockPageTest,
  UNNAMED_OVERVIEW,
} from '@/screens/stock-page/testSupport'

/**
 * SCR-005 header identity anchor — TKT-foundation-057(ii).
 *
 * With several tabs open the page must say which stock it belongs to. The identity
 * (symbol + official company name, e.g. "ALFA — Alfa Teknoloji A.Ş.") is read from
 * the overview payload the page already loads, so it costs no extra request and can
 * never disagree with the section data. A payload without a name degrades to the
 * symbol alone — the header never invents a company name.
 *
 * Expected copy is read from the catalogs (§8.3), not hard-coded.
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

const ALFA_NAME = PUBLISHED_OVERVIEW.name!

function identity(): HTMLElement {
  return screen.getByTestId('stock-page-identity')
}

/**
 * The header shares the overview query with the description section, so waiting for
 * the section's text proves the payload has settled before the identity is read —
 * otherwise the assertion could pass against the honest loading state (symbol alone).
 */
async function settledIdentity(): Promise<HTMLElement> {
  await screen.findByText(PUBLISHED_OVERVIEW.textTr!)
  return identity()
}

function withName(symbol: string, name: string): string {
  return fill(TR('stockPage.identity.withName'), { symbol, name })
}

function overviewHandler(body: StockOverviewResponse) {
  return http.get('/api/v1/stocks/:symbol/overview', () => HttpResponse.json(body))
}

beforeEach(resetStockPageTest)

describe('TKT-foundation-057(ii) — which-stock header', () => {
  it('shows the symbol and the official company name from the overview payload', async () => {
    const { i18n } = renderStockPage()

    expect(await settledIdentity()).toHaveTextContent(withName('ALFA', ALFA_NAME))

    // The identity is language-neutral data: both catalogs render the same anchor.
    await act(async () => {
      await i18n.changeLanguage('en')
    })
    expect(identity()).toHaveTextContent(
      fill(EN('stockPage.identity.withName'), { symbol: 'ALFA', name: ALFA_NAME }),
    )
  })

  it('uses the canonical symbol from the payload, not the route’s casing', async () => {
    renderStockPage(['/stocks/alfa'])

    expect(await settledIdentity()).toHaveTextContent(withName('ALFA', ALFA_NAME))
  })

  it('degrades to the symbol alone when the payload carries no name', async () => {
    server.use(overviewHandler(UNNAMED_OVERVIEW))
    renderStockPage()

    const settled = await settledIdentity()
    expect(settled).toHaveTextContent(/^ALFA$/)
    expect(settled.textContent).not.toContain('—')
  })

  it('degrades to the symbol alone when the payload has no name field at all', async () => {
    const unnamed = { ...PUBLISHED_OVERVIEW } as Record<string, unknown>
    delete unnamed.name
    server.use(overviewHandler(unnamed as unknown as StockOverviewResponse))
    renderStockPage()

    expect(await settledIdentity()).toHaveTextContent(/^ALFA$/)
  })

  it('reads the identity from the overview payload the page already loads — one request', async () => {
    let overviewCalls = 0
    server.use(
      http.get('/api/v1/stocks/:symbol/overview', () => {
        overviewCalls += 1
        return HttpResponse.json(PUBLISHED_OVERVIEW)
      }),
    )
    renderStockPage()

    expect(await settledIdentity()).toHaveTextContent(withName('ALFA', ALFA_NAME))
    // The description section renders from the same payload, and the header did not
    // cost a second overview call.
    expect(overviewCalls).toBe(1)
  })
})
