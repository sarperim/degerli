import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import type { FinancialPeriod, StockFinancialsResponse } from '@/lib/api/contracts'
import { formatAsOfDate, formatNumber, formatPercent } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import {
  BANK_FINANCIALS_FIXTURE,
  fill,
  renderStockPage,
  resetStockPageTest,
  STATEMENTS_FINANCIALS_FIXTURE,
  stockFinancialsHandler,
} from '@/screens/stock-page/testSupport'

/**
 * TC-RES-039 — the SCR-005 financial-statements view (L3), the user's v0 spec
 * (supplied 2026-10-28) rendered on TKT-foundation-094's served payload.
 *
 * Cases (`.pipeline/testing/stock-research.md` §7):
 *  (a) the full IS/BS/CF tables bind to the served line set and the served [C] rows;
 *  (b) a served-null derived row is the honest dash — the page never computes;
 *  (c) a line the surface does not carry is a dash, never a zero;
 *  (d) the period toggle filters to FY by default;
 *  (e) a cumulative interim is labelled by its served span; no span degrades to the
 *      quarter label, never a fabricated range;
 *  (f) the chosen basis survives navigation (UXR-G-021);
 *  (g) each filing badge renders only on its own served evidence;
 *  (h) a financial-sector filing renders the layout notice, never industrial tables;
 *  (i) per-share rows render "—" where no served share count exists (exclusion #4);
 *  (j) the EV header renders the served operands with their own as-of and does not
 *      compute a total (BR-RES-004);
 *  (k) a pre-094 payload keeps the legacy five-figure summary;
 *  (l) TR and EN both label the row set from the catalogs.
 *
 * Copy is read from the catalogs, never hardcoded (§8.3).
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

function financialsRegion(): HTMLElement {
  return screen.getByRole('region', { name: TR('stockPage.sections.financials') })
}

function table(region: HTMLElement, statement: string): HTMLElement {
  const found = region.querySelector(`[data-statement="${statement}"]`)
  expect(found).not.toBeNull()
  return found as HTMLElement
}

function row(tableElement: HTMLElement, rowKey: string): HTMLElement {
  const found = tableElement.querySelector(`[data-row="${rowKey}"]`)
  expect(found).not.toBeNull()
  return found as HTMLElement
}

function cell(rowElement: HTMLElement, periodEndDate: string): HTMLElement {
  const found = rowElement.querySelector(`[data-period="${periodEndDate}"]`)
  expect(found).not.toBeNull()
  return found as HTMLElement
}

function fyLabel(year: number): string {
  return fill(TR('stockPage.financials.period'), { year })
}

function quarterLabel(quarter: number, year: number): string {
  return fill(TR('stockPage.financials.periodQuarter'), { quarter, year })
}

function spanLabel(year: number, months: number, range: string): string {
  return fill(TR('stockPage.financials.spanLabel'), { year, months, range })
}

function money(value: number, locale = 'tr'): string {
  return formatNumber(locale, value, { maximumFractionDigits: 0 })
}

beforeEach(resetStockPageTest)

describe('TC-RES-039 (a) — the served statements render as three full tables', () => {
  it('binds the income statement to the served line set and the served [C] rows', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.incomeStatement'))

    const income = table(region, 'IS')
    // [R] rows from the served `lines` map.
    expect(cell(row(income, 'rev'), '2025-12-31')).toHaveTextContent(money(1_000_000))
    expect(cell(row(income, 'cogs'), '2025-12-31')).toHaveTextContent(money(600_000))
    expect(cell(row(income, 'pretaxIncome'), '2025-12-31')).toHaveTextContent(
      money(315_500),
    )
    expect(cell(row(income, 'netIncomeParent'), '2025-12-31')).toHaveTextContent(
      money(240_000),
    )
    // [C] rows from the served `derived` map — rendered as the served fraction.
    expect(cell(row(income, 'grossMargin'), '2025-12-31')).toHaveTextContent(
      formatPercent('tr', 0.4, 2),
    )
    expect(cell(row(income, 'revenueGrowthYoy'), '2025-12-31')).toHaveTextContent(
      formatPercent('tr', 0.11, 2),
    )

    // The spec's single "Other Op. Inc/Exp Net", "Investing Inc/Exp Net" and
    // "Financing Inc/Exp Net" lines are served as TWO codes each: both rows render
    // their own served figure and never a client-computed net (BR-RES-004). A fold
    // into one net row necessarily loses one of each pair's distinct values.
    const otherIncome = cell(row(income, 'opIncomeOther'), '2025-12-31')
    const otherExpense = cell(row(income, 'opExpenseOther'), '2025-12-31')
    expect(otherIncome).toHaveTextContent(money(5_000))
    expect(otherExpense).toHaveTextContent(money(2_000))
    expect(otherIncome.textContent).not.toBe(otherExpense.textContent)
    expect(cell(row(income, 'investIncome'), '2025-12-31')).toHaveTextContent(
      money(1_000),
    )
    expect(cell(row(income, 'investExpense'), '2025-12-31')).toHaveTextContent(
      money(500),
    )
    expect(cell(row(income, 'finIncome'), '2025-12-31')).toHaveTextContent(
      money(2_000),
    )
    expect(cell(row(income, 'finExpense'), '2025-12-31')).toHaveTextContent(
      money(60_000),
    )

    // The balance-sheet blocks are structure over the flat served code map.
    const balance = table(region, 'BS')
    expect(cell(row(balance, 'cash'), '2025-12-31')).toHaveTextContent(money(100_000))
    expect(cell(row(balance, 'paidInCapital'), '2025-12-31')).toHaveTextContent(
      money(100_000),
    )
    // DR-2's fence: gross debt is the served derived row, not a widened client sum —
    // the served figure (100,500) differs from both the line sum (100,000) and the
    // lease-inclusive sum (111,000), so only a served read can produce it.
    expect(cell(row(balance, 'grossDebt'), '2025-12-31')).toHaveTextContent(
      money(100_500),
    )

    // The cash-flow table carries the statement's own lines and the served FCF.
    const cashFlow = table(region, 'CF')
    expect(cell(row(cashFlow, 'ocf'), '2025-12-31')).toHaveTextContent(money(343_000))
    expect(cell(row(cashFlow, 'fcf'), '2025-12-31')).toHaveTextContent(money(250_000))
    expect(cell(row(cashFlow, 'fcfPerShare'), '2025-12-31')).toHaveTextContent(
      formatNumber('tr', 2.5, { maximumFractionDigits: 2 }),
    )
  })

  it('binds each table to its as-of date (UXR-RES-021)', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.balanceSheet'))
    const caption = fill(TR('stockPage.financials.tableCaption'), {
      date: formatAsOfDate('tr', STATEMENTS_FINANCIALS_FIXTURE.asOf),
    })
    for (const statement of ['IS', 'BS', 'CF']) {
      expect(
        within(table(region, statement)).getByText(caption),
      ).toBeInTheDocument()
    }
  })
})

describe('TC-RES-039 (b)(c) — the page computes nothing and invents no zero', () => {
  it('renders a served-null derived row as the honest dash beside its served operands', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    const user = userEvent.setup()
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.incomeStatement'))
    await user.click(
      within(region).getByRole('button', {
        name: TR('stockPage.financials.basis.quarterly'),
      }),
    )

    const income = table(region, 'IS')
    // The operands are served…
    expect(cell(row(income, 'rev'), '2026-06-30')).toHaveTextContent(money(520_000))
    expect(cell(row(income, 'grossProfit'), '2026-06-30')).toHaveTextContent(
      money(208_000),
    )
    // …and the served margin is null, so the cell is the dash — never 40%.
    const marginCell = cell(row(income, 'grossMargin'), '2026-06-30')
    expect(within(marginCell).getByText('—')).toBeInTheDocument()
    expect(marginCell.textContent).toBe('—')
    expect(marginCell).not.toHaveTextContent('%')
  })

  it('renders an unserved row and an absent code as the honest dash, never a zero', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    const user = userEvent.setup()
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.incomeStatement'))
    await user.click(
      within(region).getByRole('button', {
        name: TR('stockPage.financials.basis.quarterly'),
      }),
    )

    const income = table(region, 'IS')
    // `RND` is absent from that interim's served line set.
    const rndCell = cell(row(income, 'rnd'), '2026-03-31')
    expect(rndCell.textContent).toBe('—')
    expect(rndCell).not.toHaveTextContent('0')

    // The spec's short-term-investments row has no served canonical code at all.
    const balance = table(region, 'BS')
    const investmentsCell = cell(row(balance, 'stInvestments'), '2026-03-31')
    expect(investmentsCell.textContent).toBe('—')
    // FCF margin has no served derived row today — the dash, not a computed ratio.
    const cashFlow = table(region, 'CF')
    expect(cell(row(cashFlow, 'fcfMargin'), '2026-03-31').textContent).toBe('—')
  })
})

describe('TC-RES-039 (d)(e)(f) — the period toggle and its honest labels', () => {
  it('defaults to Yıllık and filters the served interims out', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.incomeStatement'))
    expect(within(table(region, 'IS')).getByText(fyLabel(2025))).toBeInTheDocument()

    expect(
      within(region).getByRole('button', {
        name: TR('stockPage.financials.basis.annual'),
      }),
    ).toHaveAttribute('aria-pressed', 'true')
    // No interim column under the annual basis.
    expect(within(region).queryByText(quarterLabel(2, 2026))).not.toBeInTheDocument()
    expect(region.querySelector('[data-period="2026-06-30"]')).toBeNull()
  })

  it('labels a cumulative interim by its served span and degrades honestly without one', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    const user = userEvent.setup()
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.incomeStatement'))
    expect(within(table(region, 'IS')).getByText(fyLabel(2025))).toBeInTheDocument()

    await user.click(
      within(region).getByRole('button', {
        name: TR('stockPage.financials.basis.quarterly'),
      }),
    )

    const income = table(region, 'IS')
    // The declared six-month range is rendered verbatim — cumulative, not a quarter.
    expect(
      await within(income).findByText(spanLabel(2026, 6, '01.01–30.06')),
    ).toBeInTheDocument()
    // The unspanned interim keeps today's quarter label, never a fabricated range.
    expect(within(income).getByText(quarterLabel(1, 2026))).toBeInTheDocument()
    // The annual period is filtered out under this basis.
    expect(within(income).queryByText(fyLabel(2025))).not.toBeInTheDocument()
  })

  it('preserves the chosen basis across navigation away and back (UXR-G-021)', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    const user = userEvent.setup()
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.incomeStatement'))
    expect(within(table(region, 'IS')).getByText(fyLabel(2025))).toBeInTheDocument()

    await user.click(
      within(region).getByRole('button', {
        name: TR('stockPage.financials.basis.quarterly'),
      }),
    )
    await user.click(screen.getByRole('link', { name: TR('stockPage.dcfHandoff') }))
    await screen.findByTestId('dcf-stub')
    await user.click(screen.getByRole('link', { name: 'back-to-stock' }))

    await within(table(financialsRegion(), 'IS')).findByText(
      spanLabel(2026, 6, '01.01–30.06'),
    )
    expect(
      within(financialsRegion()).getByRole('button', {
        name: TR('stockPage.financials.basis.quarterly'),
      }),
    ).toHaveAttribute('aria-pressed', 'true')
  })
})

describe('TC-RES-039 (g) — badges render only on served evidence', () => {
  it('renders Konsolide, TMS 29 and KAP Kaynak from the served filing', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.incomeStatement'))

    expect(region.querySelector('[data-badge="consolidated"]')).toHaveTextContent(
      TR('stockPage.financials.badges.consolidated'),
    )
    expect(region.querySelector('[data-badge="tms29"]')).toHaveTextContent(
      TR('stockPage.financials.badges.tms29'),
    )
    expect(region.querySelector('[data-badge="kapSource"]')).toHaveTextContent(
      TR('stockPage.financials.badges.kapSource'),
    )
    expect(region.querySelector('[data-badge="solo"]')).toBeNull()
  })

  it('renders Solo from a served false flag and suppresses the rest', async () => {
    server.use(
      stockFinancialsHandler({
        ...STATEMENTS_FINANCIALS_FIXTURE,
        filing: {
          periodEndDate: '2025-12-31',
          sourceRef: 'vyk',
          consolidated: false,
          tms29: false,
          kapSource: false,
        },
      }),
    )
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.incomeStatement'))

    expect(region.querySelector('[data-badge="solo"]')).toHaveTextContent(
      TR('stockPage.financials.badges.solo'),
    )
    expect(region.querySelector('[data-badge="consolidated"]')).toBeNull()
    expect(region.querySelector('[data-badge="tms29"]')).toBeNull()
    expect(region.querySelector('[data-badge="kapSource"]')).toBeNull()
  })

  it('suppresses every badge when no filing is served (an absent flag is not a guess)', async () => {
    server.use(
      stockFinancialsHandler({ ...STATEMENTS_FINANCIALS_FIXTURE, filing: null }),
    )
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.incomeStatement'))
    expect(region.querySelector('[data-testid="filing-badges"]')).toBeNull()
    for (const badge of ['consolidated', 'solo', 'tms29', 'kapSource']) {
      expect(region.querySelector(`[data-badge="${badge}"]`)).toBeNull()
    }
  })
})

describe('TC-RES-039 (h) — the financial-sector layout notice', () => {
  it('renders the served notice instead of half-filled industrial tables', async () => {
    server.use(stockFinancialsHandler(BANK_FINANCIALS_FIXTURE))
    renderStockPage()

    const region = financialsRegion()
    const notice = await within(region).findByTestId('layout-notice')
    expect(notice).toHaveAttribute('data-state', 'not-applicable')
    expect(notice).toHaveTextContent(TR('stockPage.financials.layoutNotice.body'))

    // No industrial row set, no EV header, no table skeleton.
    expect(region.querySelector('[data-statement]')).toBeNull()
    expect(within(region).queryByTestId('ev-header')).toBeNull()
    expect(within(region).queryByText(TR('stockPage.financials.tables.incomeStatement'))).toBeNull()
    // The basis toggle is table chrome — inert without tables, so it is suppressed;
    // the served filing badges are facts and still render.
    expect(within(region).queryByTestId('statements-controls')).toBeNull()
    expect(region.querySelector('[data-badge="consolidated"]')).not.toBeNull()
  })
})

describe('TC-RES-039 (i) — per-share rows are honest where no count exists', () => {
  it('renders BVPS and FCF/share as the dash while the served count is absent', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    const user = userEvent.setup()
    renderStockPage()

    const region = financialsRegion()
    await within(region).findByText(TR('stockPage.financials.tables.incomeStatement'))

    // FY has a served count → the served per-share value renders (the annual basis).
    const balance = table(region, 'BS')
    expect(cell(row(balance, 'bvps'), '2025-12-31')).toHaveTextContent(
      formatNumber('tr', 12.5, { maximumFractionDigits: 2 }),
    )

    await user.click(
      within(region).getByRole('button', {
        name: TR('stockPage.financials.basis.quarterly'),
      }),
    )

    // The interim serves no count → "—" (exclusion #4, never a paid-in-capital guess).
    expect(cell(row(balance, 'bvps'), '2026-06-30').textContent).toBe('—')
    expect(cell(row(balance, 'tbvPerShare'), '2026-06-30').textContent).toBe('—')
    expect(cell(row(table(region, 'CF'), 'fcfPerShare'), '2026-06-30').textContent).toBe(
      '—',
    )
  })
})

describe('TC-RES-039 (j) — the market & enterprise value header', () => {
  it('renders the served operands with their own as-of and computes no total', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    renderStockPage()

    const header = await screen.findByTestId('ev-header')
    const sharePrice = header.querySelector('[data-ev="sharePrice"]') as HTMLElement
    expect(sharePrice).toHaveTextContent(
      formatNumber('tr', 42.5, { maximumFractionDigits: 2 }),
    )
    expect(sharePrice).toHaveTextContent(
      fill(TR('honestData.asOf'), {
        date: formatAsOfDate('tr', '2026-10-05'),
      }),
    )
    // The spec's split-adjusted share count is served by no EV field (094's `evHeader`
    // carries price/cap/debt/cash/NCI only) → the honest dash, never a paid-in-capital
    // derived count and never a silently omitted row (exclusion #4).
    const splitAdjShares = header.querySelector(
      '[data-ev="splitAdjShares"]',
    ) as HTMLElement
    expect(splitAdjShares).toHaveTextContent(
      TR('stockPage.financials.ev.splitAdjShares'),
    )
    expect(
      splitAdjShares.querySelector('[data-slot="no-value"]'),
    ).toHaveTextContent('—')
    // The debt operand is dated by the statement period, not the price date.
    const grossDebt = header.querySelector('[data-ev="grossDebt"]') as HTMLElement
    expect(grossDebt).toHaveTextContent(
      fill(TR('honestData.asOf'), {
        date: formatAsOfDate('tr', '2026-06-30'),
      }),
    )
    // The total is a server fact and is not served → the honest dash, never a sum.
    const total = header.querySelector('[data-ev="enterpriseValue"]') as HTMLElement
    expect(total.textContent).toContain('—')
  })

  it('serves no EV header for a financial-sector layout', async () => {
    server.use(stockFinancialsHandler(BANK_FINANCIALS_FIXTURE))
    renderStockPage()

    await within(financialsRegion()).findByTestId('layout-notice')
    expect(screen.queryByTestId('ev-header')).toBeNull()
  })
})

describe('TC-RES-039 (k) — a pre-094 payload keeps the legacy summary', () => {
  it('renders the five-figure table when the payload serves no line set', async () => {
    const legacy: StockFinancialsResponse = {
      asOf: '2026-10-06',
      stale: false,
      periods: [
        {
          periodType: 'FY',
          fiscalYear: 2025,
          periodEndDate: '2025-12-31',
          revenue: { value: 400, restated: false },
          ebitda: { value: 140, restated: false },
          ebit: { value: 120, restated: false },
          netIncome: { value: 60, restated: false },
          fcf: { value: 50, restated: false },
        } satisfies FinancialPeriod,
      ],
    }
    server.use(stockFinancialsHandler(legacy))
    renderStockPage()

    const region = financialsRegion()
    expect(
      await within(region).findByText(TR('stockPage.financials.columns.revenue')),
    ).toBeInTheDocument()
    expect(region.querySelector('[data-statement]')).toBeNull()
    // The legacy period label is unchanged.
    expect(within(region).getByText(fyLabel(2025))).toBeInTheDocument()
  })
})

describe('TC-RES-039 (l) — both catalogs label the served row set', () => {
  it('renders the EN row labels and the EN period vocabulary', async () => {
    server.use(stockFinancialsHandler(STATEMENTS_FINANCIALS_FIXTURE))
    renderStockPage(['/stocks/ALFA'], { language: 'en' })

    const region = screen.getByRole('region', {
      name: EN('stockPage.sections.financials'),
    })
    await within(region).findByText(EN('stockPage.financials.tables.incomeStatement'))

    expect(
      within(table(region, 'IS')).getByText(EN('stockPage.financials.rows.rev')),
    ).toBeInTheDocument()
    expect(
      within(table(region, 'BS')).getByText(EN('stockPage.financials.rows.cash')),
    ).toBeInTheDocument()
    expect(
      within(table(region, 'CF')).getByText(EN('stockPage.financials.rows.ocf')),
    ).toBeInTheDocument()
    expect(
      within(region).getByRole('button', {
        name: EN('stockPage.financials.basis.annual'),
      }),
    ).toBeInTheDocument()
    expect(
      within(table(region, 'IS')).getByText(
        fill(EN('stockPage.financials.period'), { year: 2025 }),
      ),
    ).toBeInTheDocument()
  })
})
