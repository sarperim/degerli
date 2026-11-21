import { beforeEach, describe, expect, it } from 'vitest'
import { act, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'

import { formatAsOfDate } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import { problemHandler } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import {
  BALANCE_SHEET_FIXTURE,
  fill,
  GROWTH_FIXTURE,
  PREPARING_OVERVIEW,
  PUBLISHED_OVERVIEW,
  renderStockPage,
  resetStockPageTest,
  stockBalanceSheetHandler,
  stockFinancialsHandler,
  stockOverviewHandler,
} from '@/screens/stock-page/testSupport'

/**
 * SCR-005 Stock Page component tests (TKT-res-008).
 *
 * TC-RES-023 — Section independence and per-section retry (L3).
 * TC-RES-026 — Description rendering and preparing state (L3).
 * TC-RES-027 — Active-section preservation (L3).
 *
 * Expected results are the un-fragmented cases from
 * `.pipeline/testing/stock-research.md`; copy is read from the catalogs (§8.3).
 */

const TR = (key: string) => catalogValue('tr', key)

function region(name: string): HTMLElement {
  return screen.getByRole('region', { name })
}

beforeEach(resetStockPageTest)

describe('TC-RES-023 — section independence and per-section retry', () => {
  it('shows one failing section’s own error while the other six render', async () => {
    const user = userEvent.setup()
    server.use(problemHandler('/api/v1/stocks/:symbol/financials', 'INTERNAL'))
    renderStockPage()

    // The screen mounted and the overview rendered.
    expect(await screen.findByText(PUBLISHED_OVERVIEW.textTr!)).toBeInTheDocument()
    expect(
      within(region(TR('stockPage.sections.overview'))).getByText(
        PUBLISHED_OVERVIEW.textTr!,
      ),
    ).toBeInTheDocument()

    // The failing section owns its plain-language error and retry…
    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.financials') }),
    )
    const financials = region(TR('stockPage.sections.financials'))
    expect(await within(financials).findByRole('alert')).toBeInTheDocument()
    expect(
      within(financials).getByText(TR('stockPage.sectionErrorTitle')),
    ).toBeInTheDocument()
    expect(
      within(financials).getByText(TR('stockPage.sectionErrorBody')),
    ).toBeInTheDocument()
    expect(
      within(financials).getByRole('button', { name: TR('common.retry') }),
    ).toBeInTheDocument()

    // …while the other sections render normally when selected.
    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.valuation') }),
    )
    expect(
      await within(region(TR('stockPage.sections.valuation'))).findByText(
        TR('stockPage.valuation.strip.title'),
      ),
    ).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.profitability') }),
    )
    expect(
      await within(region(TR('stockPage.sections.profitability'))).findByText(
        TR('stockPage.profitability.metrics.roic'),
      ),
    ).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.growth') }),
    )
    expect(
      await within(region(TR('stockPage.sections.growth'))).findByText(
        TR('stockPage.growth.families.rev'),
      ),
    ).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.balanceSheet') }),
    )
    expect(
      await within(region(TR('stockPage.sections.balanceSheet'))).findByText(
        TR('stockPage.balanceSheet.bookValue'),
      ),
    ).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.dividends') }),
    )
    expect(
      await within(region(TR('stockPage.sections.dividends'))).findByText(
        TR('stockPage.dividends.columns.exDate'),
      ),
    ).toBeInTheDocument()
  })

  it('recovers a failed section on retry once the handler is restored', async () => {
    const user = userEvent.setup()
    server.use(problemHandler('/api/v1/stocks/:symbol/financials', 'INTERNAL'))
    renderStockPage()

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.financials') }),
    )
    const financials = region(TR('stockPage.sections.financials'))
    await within(financials).findByRole('alert')

    server.use(stockFinancialsHandler())
    await user.click(
      within(financials).getByRole('button', { name: TR('common.retry') }),
    )

    expect(
      await within(financials).findByText(
        TR('stockPage.financials.columns.revenue'),
      ),
    ).toBeInTheDocument()
    expect(within(financials).queryByRole('alert')).not.toBeInTheDocument()
  })

  it('loads sections with independent loading states', async () => {
    const user = userEvent.setup()
    // Growth is slow; every other section resolves immediately.
    server.use(
      http.get('/api/v1/stocks/:symbol/growth', async () => {
        await delay(120)
        return HttpResponse.json(GROWTH_FIXTURE)
      }),
    )
    renderStockPage()

    await screen.findByText(PUBLISHED_OVERVIEW.textTr!)

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.growth') }),
    )
    const growth = region(TR('stockPage.sections.growth'))
    expect(within(growth).getByRole('status')).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.balanceSheet') }),
    )
    const balanceSheet = region(TR('stockPage.sections.balanceSheet'))
    expect(within(balanceSheet).queryByRole('status')).not.toBeInTheDocument()
    expect(
      await within(balanceSheet).findByText(TR('stockPage.balanceSheet.bookValue')),
    ).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.growth') }),
    )
    expect(
      await within(region(TR('stockPage.sections.growth'))).findByText(
        TR('stockPage.growth.families.rev'),
      ),
    ).toBeInTheDocument()
  })
})

describe('TC-RES-026 — description rendering and preparing state', () => {
  it('renders the published description in the selected language with its last-reviewed date', async () => {
    let overviewCalls = 0
    server.use(
      http.get('/api/v1/stocks/:symbol/overview', () => {
        overviewCalls += 1
        return HttpResponse.json(PUBLISHED_OVERVIEW)
      }),
    )

    const { i18n } = renderStockPage()

    expect(await screen.findByText(PUBLISHED_OVERVIEW.textTr!)).toBeInTheDocument()
    expect(
      screen.getByText(
        fill(TR('stockPage.descriptionLastReviewed'), {
          date: formatAsOfDate('tr', PUBLISHED_OVERVIEW.lastReviewedAt!),
        }),
      ),
    ).toBeInTheDocument()

    // Switching language re-renders both pre-served texts without a refetch.
    await act(async () => {
      await i18n.changeLanguage('en')
    })
    expect(await screen.findByText(PUBLISHED_OVERVIEW.textEn!)).toBeInTheDocument()
    expect(screen.queryByText(PUBLISHED_OVERVIEW.textTr!)).not.toBeInTheDocument()
    expect(overviewCalls).toBe(1)
  })

  it('shows the preparing state while the numbers sections stay fully populated', async () => {
    const user = userEvent.setup()
    server.use(stockOverviewHandler(PREPARING_OVERVIEW))
    renderStockPage()

    const overview = await screen.findByRole('region', {
      name: TR('stockPage.sections.overview'),
    })
    expect(
      await within(overview).findByText(TR('honestData.preparingTitle')),
    ).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.financials') }),
    )
    expect(
      await within(region(TR('stockPage.sections.financials'))).findByText(
        TR('stockPage.financials.columns.revenue'),
      ),
    ).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.profitability') }),
    )
    expect(
      await within(region(TR('stockPage.sections.profitability'))).findByText(
        TR('stockPage.profitability.metrics.roic'),
      ),
    ).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: TR('stockPage.sections.balanceSheet') }),
    )
    expect(
      await within(region(TR('stockPage.sections.balanceSheet'))).findByText(
        TR('stockPage.balanceSheet.bookValue'),
      ),
    ).toBeInTheDocument()
  })
})

describe('TC-RES-027 — active-section preservation', () => {
  it('preserves the active section across navigation away and back', async () => {
    const user = userEvent.setup()
    renderStockPage()
    await screen.findByText(PUBLISHED_OVERVIEW.textTr!)

    await user.click(
      screen.getByRole('button', {
        name: TR('stockPage.sections.balanceSheet'),
      }),
    )
    expect(
      screen.getByRole('button', {
        name: TR('stockPage.sections.balanceSheet'),
      }),
    ).toHaveAttribute('aria-current', 'true')

    // Navigate away via the DCF hand-off, then return.
    await user.click(
      screen.getByRole('link', { name: TR('stockPage.dcfHandoff') }),
    )
    expect(await screen.findByTestId('dcf-stub')).toBeInTheDocument()
    await user.click(screen.getByRole('link', { name: 'back-to-stock' }))

    await within(region(TR('stockPage.sections.balanceSheet'))).findByText(
      TR('stockPage.balanceSheet.bookValue'),
    )
    expect(
      screen.getByRole('button', {
        name: TR('stockPage.sections.balanceSheet'),
      }),
    ).toHaveAttribute('aria-current', 'true')
  })
})

/**
 * TC-MDF-115 (v) — the balance-sheet `SHARES_OUT` row is a share COUNT again.
 *
 * TKT-foundation-086 reverts the 072 (iii) mislabel (TKT-foundation-065's consequence 2):
 * the row carries the statement's `SHARES_OUT` line — a count of shares — while the
 * paid-in-capital figure lives in the `shareCount.paidInCapital` block, never here. The
 * label and the unit therefore say a count, never TRY; the row is an honest dash until a
 * statements decoder actually emits `SHARES_OUT` (none does today).
 */
describe('TC-MDF-115 (v) — the balance-sheet shares row carries count semantics', () => {
  it('labels the row as a share count with a count unit, never as paid-in capital', async () => {
    renderStockPage(['/stocks/ALFA'], { section: 'balanceSheet' })

    const balanceSheet = region(TR('stockPage.sections.balanceSheet'))
    await within(balanceSheet).findByText(TR('stockPage.balanceSheet.bookValue'))

    const row = balanceSheet.querySelector('[data-line="sharesOutstanding"]')
    expect(row).not.toBeNull()
    expect(row as HTMLElement).toHaveTextContent(
      TR('stockPage.balanceSheet.lines.sharesOutstanding'),
    )
    expect(row as HTMLElement).toHaveTextContent(TR('units.count'))

    // The reverted money reading must be gone from this row.
    expect(row as HTMLElement).not.toHaveTextContent(TR('units.currencyTry'))
    expect(row as HTMLElement).not.toHaveTextContent(/ödenmiş sermaye/i)
    expect(row as HTMLElement).not.toHaveTextContent(/paid-in capital/i)
  })

  it('keeps the row an honest dash — no zero, no count unit — while no decoder emits SHARES_OUT', async () => {
    // The shipped production state: no statements decoder maps `SHARES_OUT` (0 rows), so the
    // served figure is null. The row must read as absent — the honest dash — and never as a
    // `0` count or a count unit attached to nothing.
    server.use(
      stockBalanceSheetHandler({
        ...BALANCE_SHEET_FIXTURE,
        lines: {
          ...BALANCE_SHEET_FIXTURE.lines,
          sharesOutstanding: { value: null, restated: false },
        },
      }),
    )
    renderStockPage(['/stocks/ALFA'], { section: 'balanceSheet' })

    const balanceSheet = region(TR('stockPage.sections.balanceSheet'))
    await within(balanceSheet).findByText(TR('stockPage.balanceSheet.bookValue'))

    const row = balanceSheet.querySelector(
      '[data-line="sharesOutstanding"]',
    ) as HTMLElement
    expect(row).not.toBeNull()
    expect(row).toHaveTextContent(TR('stockPage.balanceSheet.lines.sharesOutstanding'))

    // The value slot holds the dash and nothing else: no figure (so no `0`), no count unit,
    // no currency.
    const value = row.querySelector('dd') as HTMLElement
    const dash = value.querySelector('[data-slot="no-value"]')
    expect(dash).not.toBeNull()
    expect(dash!.textContent).toBe('—')
    expect(value.textContent).toBe('—')
    expect(value.textContent).not.toContain('0')
    expect(value.querySelector('[data-slot="unit"]')).toBeNull()
    expect(value).not.toHaveTextContent(TR('units.count'))
    expect(value).not.toHaveTextContent(TR('units.currencyTry'))
  })

  it('carries the English count label and count unit too', async () => {
    const EN = (key: string) => catalogValue('en', key)
    renderStockPage(['/stocks/ALFA'], { language: 'en', section: 'balanceSheet' })

    const balanceSheet = screen.getByRole('region', {
      name: EN('stockPage.sections.balanceSheet'),
    })
    await within(balanceSheet).findByText(EN('stockPage.balanceSheet.bookValue'))

    const row = balanceSheet.querySelector(
      '[data-line="sharesOutstanding"]',
    ) as HTMLElement
    expect(row).toHaveTextContent(EN('stockPage.balanceSheet.lines.sharesOutstanding'))
    expect(row).toHaveTextContent(EN('units.count'))
    expect(row).not.toHaveTextContent(EN('units.currencyTry'))
    expect(row).not.toHaveTextContent(/paid-in capital/i)
  })
})

describe('SLICE-033 — part-by-part tabs and KAP jump buttons', () => {
  it('mounts only the active section panel and unmounts previous panels on tab switch', async () => {
    const user = userEvent.setup()
    renderStockPage()

    expect(await screen.findByText(PUBLISHED_OVERVIEW.textTr!)).toBeInTheDocument()
    expect(
      screen.getByRole('region', { name: TR('stockPage.sections.overview') }),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('region', { name: TR('stockPage.sections.valuation') }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('region', { name: TR('stockPage.sections.financials') }),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('region', { name: TR('stockPage.sections.kapReports') }),
    ).not.toBeInTheDocument()

    await user.click(
      screen.getByRole('button', {
        name: TR('stockPage.sections.valuation'),
      }),
    )

    expect(
      await within(region(TR('stockPage.sections.valuation'))).findByText(
        TR('stockPage.valuation.strip.title'),
      ),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('region', { name: TR('stockPage.sections.overview') }),
    ).not.toBeInTheDocument()
  })

  it('activates the kapReports rail tab from both overview and financials jump buttons', async () => {
    const user = userEvent.setup()
    renderStockPage()

    expect(await screen.findByText(PUBLISHED_OVERVIEW.textTr!)).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', {
        name: TR('stockPage.overview.openKapReports'),
      }),
    )

    expect(
      screen.getByRole('button', {
        name: TR('stockPage.sections.kapReports'),
      }),
    ).toHaveAttribute('aria-current', 'true')
    expect(region(TR('stockPage.sections.kapReports'))).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', {
        name: TR('stockPage.sections.financials'),
      }),
    )
    const financials = region(TR('stockPage.sections.financials'))
    const financialsKapButton = await within(financials).findByRole('button', {
      name: TR('stockPage.financials.openKapReports'),
    })
    await user.click(financialsKapButton)

    expect(
      screen.getByRole('button', {
        name: TR('stockPage.sections.kapReports'),
      }),
    ).toHaveAttribute('aria-current', 'true')
    expect(region(TR('stockPage.sections.kapReports'))).toBeInTheDocument()
  })
})
