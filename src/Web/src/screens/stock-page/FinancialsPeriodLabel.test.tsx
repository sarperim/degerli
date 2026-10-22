import { beforeEach, describe, expect, it } from 'vitest'
import { act, screen, within } from '@testing-library/react'

import type { FinancialPeriod, StockFinancialsResponse } from '@/lib/api/contracts'
import { formatAsOfDate } from '@/lib/format'
import { server } from '@/test/msw/server'
import { catalogValue } from '@/test/i18n'
import {
  fill,
  QUARTER_FINANCIALS_FIXTURE,
  renderStockPage,
  resetStockPageTest,
  stockFinancialsHandler,
} from '@/screens/stock-page/testSupport'

/**
 * SCR-005 financials period labels — TKT-foundation-057(i).
 *
 * The API serves `periodType` (`FY` | `Q`) with `periodEndDate`; the label is a
 * claim about the data (FR-RES-017), so it must be derived from those fields and
 * never fabricate a fiscal year for a quarter. Expected copy is read from the
 * catalogs (§8.3), not hard-coded here.
 *
 * Red before the fix: every row rendered `FY{{year}}`, so AEFES's Q1-2023 revenue
 * (24.5B) appeared as "FY2023" beside FY2022 (90.5B) — a fabricated −73% collapse.
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

function financialsRegion(): HTMLElement {
  return screen.getByRole('region', { name: TR('stockPage.sections.financials') })
}

function fyLabel(year: number): string {
  return fill(TR('stockPage.financials.period'), { year })
}

function quarterLabel(quarter: number, year: number): string {
  return fill(TR('stockPage.financials.periodQuarter'), { quarter, year })
}

function endingLabel(periodEndDate: string): string {
  return fill(TR('stockPage.financials.periodEnding'), {
    date: formatAsOfDate('tr', periodEndDate),
  })
}

/** A one-period financials payload built from the pinned period shape. */
function financialsWith(period: FinancialPeriod): StockFinancialsResponse {
  return { asOf: '2026-10-06', stale: false, periods: [period] }
}

const FY_PERIOD: FinancialPeriod = {
  periodType: 'FY',
  fiscalYear: 2025,
  periodEndDate: '2025-12-31',
  revenue: { value: 400, restated: false },
  ebitda: { value: 140, restated: false },
  ebit: { value: 120, restated: false },
  netIncome: { value: 60, restated: false },
  fcf: { value: 50, restated: false },
}

beforeEach(resetStockPageTest)

describe('TKT-foundation-057(i) — period-type-aware financials labels', () => {
  it('labels a Q period as the quarter derived from periodEndDate, not a fiscal year (TR)', async () => {
    server.use(stockFinancialsHandler(QUARTER_FINANCIALS_FIXTURE))
    renderStockPage()

    const financials = financialsRegion()
    const quarterRow = await within(financials).findByText(quarterLabel(1, 2023))

    expect(quarterRow.tagName).toBe('TH')
    // The FY row beside it keeps its own label.
    expect(within(financials).getByText(fyLabel(2022))).toBeInTheDocument()
    // No row claims a fiscal year for the Q period.
    expect(within(financials).queryByText(fyLabel(2023))).not.toBeInTheDocument()
  })

  it('labels the same Q period in the EN catalog without refetching', async () => {
    server.use(stockFinancialsHandler(QUARTER_FINANCIALS_FIXTURE))
    const { i18n } = renderStockPage()

    const financials = financialsRegion()
    expect(await within(financials).findByText(quarterLabel(1, 2023))).toBeInTheDocument()

    await act(async () => {
      await i18n.changeLanguage('en')
    })

    expect(
      await within(financials).findByText(
        fill(EN('stockPage.financials.periodQuarter'), { quarter: 1, year: 2023 }),
      ),
    ).toBeInTheDocument()
    expect(
      within(financials).getByText(fill(EN('stockPage.financials.period'), { year: 2022 })),
    ).toBeInTheDocument()
    expect(
      within(financials).queryByText(
        fill(EN('stockPage.financials.period'), { year: 2023 }),
      ),
    ).not.toBeInTheDocument()
  })

  it('derives Q2, Q3 and Q4 from the period-end month', async () => {
    const quarters: Array<{ periodEndDate: string; quarter: number }> = [
      { periodEndDate: '2023-06-30', quarter: 2 },
      { periodEndDate: '2023-09-29', quarter: 3 },
      { periodEndDate: '2023-12-29', quarter: 4 },
    ]
    server.use(
      stockFinancialsHandler({
        asOf: '2026-10-06',
        stale: false,
        periods: quarters.map(({ periodEndDate }) => ({
          ...FY_PERIOD,
          periodType: 'Q',
          fiscalYear: 2023,
          periodEndDate,
        })),
      }),
    )
    renderStockPage()

    const financials = financialsRegion()
    for (const { quarter } of quarters) {
      expect(await within(financials).findByText(quarterLabel(quarter, 2023))).toBeInTheDocument()
      // The quarter's own fiscal year is never rendered as a fiscal-year label.
      expect(within(financials).queryByText(fyLabel(2023))).not.toBeInTheDocument()
    }
  })

  it('keeps the fiscal-year label for an FY period', async () => {
    server.use(stockFinancialsHandler(financialsWith(FY_PERIOD)))
    renderStockPage()

    expect(await within(financialsRegion()).findByText(fyLabel(2025))).toBeInTheDocument()
  })

  it('degrades honestly when periodType is absent — no fiscal year, no quarter', async () => {
    const untyped = { ...FY_PERIOD } as Record<string, unknown>
    delete untyped.periodType
    server.use(stockFinancialsHandler(financialsWith(untyped as unknown as FinancialPeriod)))
    renderStockPage()

    const financials = financialsRegion()
    expect(
      await within(financials).findByText(endingLabel(FY_PERIOD.periodEndDate)),
    ).toBeInTheDocument()
    expect(within(financials).queryByText(fyLabel(2025))).not.toBeInTheDocument()
    expect(within(financials).queryByText(quarterLabel(4, 2025))).not.toBeInTheDocument()
  })

  it('degrades honestly for an unexpected periodType', async () => {
    server.use(
      stockFinancialsHandler(
        financialsWith({ ...FY_PERIOD, periodType: 'H1', periodEndDate: '2025-06-30' }),
      ),
    )
    renderStockPage()

    const financials = financialsRegion()
    expect(await within(financials).findByText(endingLabel('2025-06-30'))).toBeInTheDocument()
    expect(within(financials).queryByText(quarterLabel(2, 2025))).not.toBeInTheDocument()
    expect(within(financials).queryByText(fyLabel(2025))).not.toBeInTheDocument()
  })
})
