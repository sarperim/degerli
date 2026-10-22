import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'

import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import {
  DIVIDENDS_UNAVAILABLE_FIXTURE,
  LEGACY_DIVIDENDS_FIXTURE,
  ZERO_DIVIDENDS_FIXTURE,
  renderStockPage,
  resetStockPageTest,
  stockDividendsHandler,
} from '@/screens/stock-page/testSupport'

/**
 * TC-RES-038 — SCR-005 §7 dividends section: three honest states (TKT-foundation-052).
 *
 * The un-fragmented expected result (`.pipeline/testing/stock-research.md`):
 * a dividends payload that reports the lane's data available and carries rows renders
 * the dated history; the same signal with zero rows renders the **backed** "never paid"
 * wording; no signal (or `dividendsAvailable: false`) renders the neutral "no data yet"
 * state and never the never-paid claim. An absent `ttmPerShare` is never shown as 0.
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

function dividendsRegion(name: string): HTMLElement {
  return screen.getByRole('region', { name })
}

beforeEach(resetStockPageTest)

describe('TC-RES-038 dividends section honesty states', () => {
  it('renders the dated history when the data is available and rows exist', async () => {
    renderStockPage(['/stocks/ALFA'], { language: 'tr' })
    const dividends = dividendsRegion(TR('stockPage.sections.dividends'))

    // The history table renders with the served row.
    expect(
      await within(dividends).findByText(TR('stockPage.dividends.columns.exDate')),
    ).toBeInTheDocument()
    expect(within(dividends).getByText('2026-04-01')).toBeInTheDocument()

    // TTM DPS is the server's value, not a client recomputation.
    expect(
      within(dividends).getByText(TR('stockPage.dividends.ttm'), { exact: false }),
    ).toBeInTheDocument()

    // Neither empty state appears over a populated table.
    expect(
      within(dividends).queryByText(TR('stockPage.dividends.neverPaidTitle')),
    ).not.toBeInTheDocument()
    expect(
      within(dividends).queryByText(TR('stockPage.dividends.unavailableTitle')),
    ).not.toBeInTheDocument()
  })

  it('renders the backed never-paid state only when the server reports the data available', async () => {
    server.use(stockDividendsHandler(ZERO_DIVIDENDS_FIXTURE))
    renderStockPage(['/stocks/NEWP'], { language: 'tr' })
    const dividends = dividendsRegion(TR('stockPage.sections.dividends'))

    // Processed and genuinely zero: the existing never-paid wording is now backed.
    expect(
      await within(dividends).findByText(TR('stockPage.dividends.neverPaidTitle')),
    ).toBeInTheDocument()
    expect(
      within(dividends).getByText(
        new RegExp(TR('stockPage.dividends.neverPaidBody')),
      ),
    ).toBeInTheDocument()

    // The neutral state and a fabricated TTM 0 are both absent.
    expect(
      within(dividends).queryByText(TR('stockPage.dividends.unavailableTitle')),
    ).not.toBeInTheDocument()
    expect(
      within(dividends).queryByText(TR('stockPage.dividends.ttm'), { exact: false }),
    ).not.toBeInTheDocument()
  })

  it('renders the neutral no-data state when the dividends lane has no coverage', async () => {
    server.use(stockDividendsHandler(DIVIDENDS_UNAVAILABLE_FIXTURE))
    renderStockPage(['/stocks/AEFES'], { language: 'tr' })
    const dividends = dividendsRegion(TR('stockPage.sections.dividends'))

    // The honest neutral state — a statement about the platform's data, not the company.
    expect(
      await within(dividends).findByText(TR('stockPage.dividends.unavailableTitle')),
    ).toBeInTheDocument()
    expect(
      within(dividends).getByText(
        new RegExp(TR('stockPage.dividends.unavailableBody')),
      ),
    ).toBeInTheDocument()

    // The unbacked claim is impossible here.
    expect(
      within(dividends).queryByText(TR('stockPage.dividends.neverPaidTitle')),
    ).not.toBeInTheDocument()
    expect(
      within(dividends).queryByText(TR('stockPage.dividends.neverPaidBody')),
    ).not.toBeInTheDocument()
  })

  it('degrades a payload without the availability signal to the neutral state', async () => {
    // A pre-052 payload carries no signal: absence of evidence must never render as
    // evidence of absence (the degradation case).
    server.use(stockDividendsHandler(LEGACY_DIVIDENDS_FIXTURE))
    renderStockPage(['/stocks/AEFES'], { language: 'tr' })
    const dividends = dividendsRegion(TR('stockPage.sections.dividends'))

    expect(
      await within(dividends).findByText(TR('stockPage.dividends.unavailableTitle')),
    ).toBeInTheDocument()
    expect(
      within(dividends).queryByText(TR('stockPage.dividends.neverPaidTitle')),
    ).not.toBeInTheDocument()
  })

  it('serves both empty states in English as well', async () => {
    server.use(stockDividendsHandler(ZERO_DIVIDENDS_FIXTURE))
    const backed = renderStockPage(['/stocks/NEWP'], { language: 'en' })
    expect(
      await within(
        dividendsRegion(EN('stockPage.sections.dividends')),
      ).findByText(EN('stockPage.dividends.neverPaidTitle')),
    ).toBeInTheDocument()
    backed.unmount()

    server.use(stockDividendsHandler(DIVIDENDS_UNAVAILABLE_FIXTURE))
    renderStockPage(['/stocks/AEFES'], { language: 'en' })
    expect(
      await within(
        dividendsRegion(EN('stockPage.sections.dividends')),
      ).findByText(EN('stockPage.dividends.unavailableTitle')),
    ).toBeInTheDocument()
    expect(
      screen.queryByText(EN('stockPage.dividends.neverPaidTitle')),
    ).not.toBeInTheDocument()
  })
})
