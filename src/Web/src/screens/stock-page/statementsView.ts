import type { TFunction } from 'i18next'

import type {
  EvFact,
  EvHeader,
  FilingMetadata,
  FinancialPeriod,
  StatementFigure,
  StatementLines,
} from '@/lib/api/contracts'
import { formatAsOfDate } from '@/lib/format'

/**
 * The SCR-005 statements view's pure model (TKT-foundation-095).
 *
 * The user's v0 spec (rendered verbatim in the ticket) is the row set; this module
 * is the **structure only** — which served code each row binds to, how a period is
 * labelled, which badges a filing licenses, and how a value is formatted. It holds
 * no arithmetic over served figures: every `[R]` row binds to a served `lines` entry
 * and every `[C]` row to a served `derived` value (BR-RES-004, SCR-005 §6 — the page
 * computes nothing). A row the serving surface does not carry renders the honest
 * no-data dash.
 */

/** The period toggle's two bases (the spec's Yıllık | Çeyreklik). */
export type StatementBasis = 'annual' | 'quarterly'

/** The period-type vocabulary the API serves (`FY` annual, `Q` cumulative interim). */
const ANNUAL_PERIOD_TYPE = 'FY'
const QUARTER_PERIOD_TYPE = 'Q'

export type StatementType = 'IS' | 'BS' | 'CF'

/** How a row's number is displayed (units are the client's, never recomputed). */
export type StatementValueFormat = 'money' | 'percent' | 'perShare'

/**
 * Where a row's figure comes from. `unserved` is a row the spec names but no served
 * canonical code (or derived row) carries yet: it renders an honest dash, never a
 * graph of zeros. When the serving surface grows the code, the row lights up.
 */
export type StatementRowSource =
  | { kind: 'line'; statement: StatementType; code: string }
  | { kind: 'derived'; code: string }
  | { kind: 'period'; field: 'revenue' | 'ebitda' | 'ebit' | 'netIncome' | 'fcf' }
  | { kind: 'unserved' }

export interface StatementRow {
  /** Stable row id — the DOM hook (`data-row`) and the React key. */
  key: string
  labelKey: string
  format: StatementValueFormat
  source: StatementRowSource
}

export interface StatementRowGroup {
  key: string
  /** A group heading (the balance sheet's blocks); omitted for a flat section. */
  labelKey?: string
  rows: StatementRow[]
}

export interface StatementSection {
  key: StatementType
  titleKey: string
  groups: StatementRowGroup[]
}

function line(
  key: string,
  labelKey: string,
  statement: StatementType,
  code: string,
  format: StatementValueFormat = 'money',
): StatementRow {
  return { key, labelKey, format, source: { kind: 'line', statement, code } }
}

function derived(
  key: string,
  labelKey: string,
  code: string,
  format: StatementValueFormat = 'money',
): StatementRow {
  return { key, labelKey, format, source: { kind: 'derived', code } }
}

function periodField(
  key: string,
  labelKey: string,
  field: 'revenue' | 'ebitda' | 'ebit' | 'netIncome' | 'fcf',
): StatementRow {
  return { key, labelKey, format: 'money', source: { kind: 'period', field } }
}

/** A spec row with no served canonical code today — always the honest dash. */
function unserved(
  key: string,
  labelKey: string,
  format: StatementValueFormat = 'money',
): StatementRow {
  return { key, labelKey, format, source: { kind: 'unserved' } }
}

/**
 * The income statement's rows in the spec's order. Rows the spec states as one
 * line but the serving surface carries as two codes ("Other Op. Inc/Exp Net",
 * "Investing Inc/Exp Net", "Financing Inc/Exp Net") are **two rows**: the page
 * may not manufacture a net the filing never states (BR-RES-004).
 */
const INCOME_STATEMENT: StatementSection = {
  key: 'IS',
  titleKey: 'stockPage.financials.tables.incomeStatement',
  groups: [
    {
      key: 'is',
      rows: [
        periodField('rev', 'stockPage.financials.rows.rev', 'revenue'),
        derived('revenueGrowthYoy', 'stockPage.financials.rows.revenueGrowthYoy', 'revenueGrowthYoy', 'percent'),
        line('cogs', 'stockPage.financials.rows.cogs', 'IS', 'COGS'),
        line('grossProfit', 'stockPage.financials.rows.grossProfit', 'IS', 'GROSS_PROFIT'),
        derived('grossMargin', 'stockPage.financials.rows.grossMargin', 'grossMargin', 'percent'),
        line('rnd', 'stockPage.financials.rows.rnd', 'IS', 'RND'),
        line('sga', 'stockPage.financials.rows.sga', 'IS', 'SGA'),
        line('opIncomeOther', 'stockPage.financials.rows.opIncomeOther', 'IS', 'OP_INCOME_OTHER'),
        line('opExpenseOther', 'stockPage.financials.rows.opExpenseOther', 'IS', 'OP_EXPENSE_OTHER'),
        periodField('ebit', 'stockPage.financials.rows.ebit', 'ebit'),
        derived('operatingMargin', 'stockPage.financials.rows.operatingMargin', 'operatingMargin', 'percent'),
        periodField('ebitda', 'stockPage.financials.rows.ebitda', 'ebitda'),
        derived('ebitdaMargin', 'stockPage.financials.rows.ebitdaMargin', 'ebitdaMargin', 'percent'),
        line('investIncome', 'stockPage.financials.rows.investIncome', 'IS', 'INVEST_INCOME'),
        line('investExpense', 'stockPage.financials.rows.investExpense', 'IS', 'INVEST_EXPENSE'),
        line('finIncome', 'stockPage.financials.rows.finIncome', 'IS', 'FIN_INCOME'),
        line('finExpense', 'stockPage.financials.rows.finExpense', 'IS', 'FIN_EXP'),
        line('monetaryGl', 'stockPage.financials.rows.monetaryGl', 'IS', 'NET_MONETARY_GL'),
        line('pretaxIncome', 'stockPage.financials.rows.pretaxIncome', 'IS', 'PRETAX_INC'),
        line('taxExpense', 'stockPage.financials.rows.taxExpense', 'IS', 'TAX_EXP'),
        derived('effectiveTaxRate', 'stockPage.financials.rows.effectiveTaxRate', 'effectiveTaxRate', 'percent'),
        periodField('netIncome', 'stockPage.financials.rows.netIncome', 'netIncome'),
        line('netIncomeParent', 'stockPage.financials.rows.netIncomeParent', 'IS', 'NI_PARENT'),
        derived('netMarginParent', 'stockPage.financials.rows.netMarginParent', 'netMarginParent', 'percent'),
      ],
    },
  ],
}

/**
 * The balance sheet's rows. The current / non-current structure is this view's
 * presentation concern over the flat served code map (TKT-foundation-094's
 * amendment: serving keeps no block logic), so it is expressed here as groups.
 */
const BALANCE_SHEET: StatementSection = {
  key: 'BS',
  titleKey: 'stockPage.financials.tables.balanceSheet',
  groups: [
    {
      key: 'currentAssets',
      labelKey: 'stockPage.financials.groups.currentAssets',
      rows: [
        line('cash', 'stockPage.financials.rows.cash', 'BS', 'CASH'),
        unserved('stInvestments', 'stockPage.financials.rows.stInvestments'),
        line('tradeReceivables', 'stockPage.financials.rows.tradeReceivables', 'BS', 'ST_TRADE_RECV'),
        line('inventory', 'stockPage.financials.rows.inventory', 'BS', 'INVENTORY'),
        line('otherCurrentAssets', 'stockPage.financials.rows.otherCurrentAssets', 'BS', 'CUR_ASSETS_OTHER'),
        line('currentAssets', 'stockPage.financials.rows.currentAssets', 'BS', 'CUR_ASSETS'),
      ],
    },
    {
      key: 'nonCurrentAssets',
      labelKey: 'stockPage.financials.groups.nonCurrentAssets',
      rows: [
        line('ppeNet', 'stockPage.financials.rows.ppeNet', 'BS', 'PPE_NET'),
        line('rouAsset', 'stockPage.financials.rows.rouAsset', 'BS', 'ROU_ASSET'),
        line('goodwill', 'stockPage.financials.rows.goodwill', 'BS', 'GOODWILL'),
        line('intangibles', 'stockPage.financials.rows.intangibles', 'BS', 'INTANGIBLES_NET'),
        line('otherNonCurrentAssets', 'stockPage.financials.rows.otherNonCurrentAssets', 'BS', 'NONCUR_ASSETS_OTHER'),
        line('totalAssets', 'stockPage.financials.rows.totalAssets', 'BS', 'TOTAL_ASSETS'),
      ],
    },
    {
      key: 'liabilities',
      labelKey: 'stockPage.financials.groups.liabilities',
      rows: [
        line('shortTermDebt', 'stockPage.financials.rows.shortTermDebt', 'BS', 'ST_DEBT'),
        line('stLease', 'stockPage.financials.rows.stLease', 'BS', 'ST_LEASE_LIAB'),
        line('tradePayables', 'stockPage.financials.rows.tradePayables', 'BS', 'ST_TRADE_PAY'),
        line('deferredRevenue', 'stockPage.financials.rows.deferredRevenue', 'BS', 'ST_DEFERRED_REV'),
        line('otherCurrentLiabilities', 'stockPage.financials.rows.otherCurrentLiabilities', 'BS', 'CUR_LIAB_OTHER'),
        line('currentLiabilities', 'stockPage.financials.rows.currentLiabilities', 'BS', 'CUR_LIAB'),
        line('longTermDebt', 'stockPage.financials.rows.longTermDebt', 'BS', 'LT_DEBT'),
        line('ltLease', 'stockPage.financials.rows.ltLease', 'BS', 'LT_LEASE_LIAB'),
        line('otherNonCurrentLiabilities', 'stockPage.financials.rows.otherNonCurrentLiabilities', 'BS', 'NONCUR_LIAB_OTHER'),
        line('totalLiabilities', 'stockPage.financials.rows.totalLiabilities', 'BS', 'TOTAL_LIAB'),
      ],
    },
    {
      key: 'equity',
      labelKey: 'stockPage.financials.groups.equity',
      rows: [
        line('paidInCapital', 'stockPage.financials.rows.paidInCapital', 'BS', 'PAID_IN_CAPITAL'),
        unserved('tms29Adjustment', 'stockPage.financials.rows.tms29Adjustment'),
        line('retainedEarnings', 'stockPage.financials.rows.retainedEarnings', 'BS', 'RETAINED_EARNINGS'),
        line('equityParent', 'stockPage.financials.rows.equityParent', 'BS', 'EQUITY_PARENT'),
        line('minorityInterest', 'stockPage.financials.rows.minorityInterest', 'BS', 'MINORITY_INT'),
        line('totalEquity', 'stockPage.financials.rows.totalEquity', 'BS', 'TOTAL_EQUITY'),
      ],
    },
    {
      key: 'derived',
      labelKey: 'stockPage.financials.groups.derived',
      rows: [
        derived('netWorkingCapital', 'stockPage.financials.rows.netWorkingCapital', 'netWorkingCapital'),
        derived('grossDebt', 'stockPage.financials.rows.grossDebt', 'grossDebt'),
        derived('netDebt', 'stockPage.financials.rows.netDebt', 'netDebt'),
        derived('tangibleBookValue', 'stockPage.financials.rows.tangibleBookValue', 'tangibleBookValue'),
        derived('bvps', 'stockPage.financials.rows.bvps', 'bvps', 'perShare'),
        derived('tbvPerShare', 'stockPage.financials.rows.tbvPerShare', 'tbvPerShare', 'perShare'),
      ],
    },
  ],
}

/**
 * The cash-flow rows. `FCF` is the served period-level compat figure (the same
 * `CanonicalMetrics.FreeCashFlow` seam), `FCF Margin %` has no served derived row
 * today and therefore renders the dash until the serving surface carries it.
 */
const CASH_FLOW: StatementSection = {
  key: 'CF',
  titleKey: 'stockPage.financials.tables.cashFlow',
  groups: [
    {
      key: 'cf',
      rows: [
        line('cfNetIncome', 'stockPage.financials.rows.cfNetIncome', 'CF', 'NI'),
        line('deprAmort', 'stockPage.financials.rows.deprAmort', 'CF', 'DEPR_AMORT'),
        line('impairment', 'stockPage.financials.rows.impairment', 'CF', 'IMPAIRMENT'),
        line('provisions', 'stockPage.financials.rows.provisions', 'CF', 'PROVISIONS'),
        line('monetaryAdj', 'stockPage.financials.rows.monetaryAdj', 'CF', 'MONETARY_ADJ'),
        line('wcChange', 'stockPage.financials.rows.wcChange', 'CF', 'ΔWC'),
        line('ocf', 'stockPage.financials.rows.ocf', 'CF', 'OCF'),
        line('capex', 'stockPage.financials.rows.capex', 'CF', 'CAPEX'),
        periodField('fcf', 'stockPage.financials.rows.fcf', 'fcf'),
        derived('fcfMargin', 'stockPage.financials.rows.fcfMargin', 'fcfMargin', 'percent'),
        derived('fcfPerShare', 'stockPage.financials.rows.fcfPerShare', 'fcfPerShare', 'perShare'),
        line('assetSaleProceeds', 'stockPage.financials.rows.assetSaleProceeds', 'CF', 'ASSET_SALE_PROCEEDS'),
        line('acquisitionCash', 'stockPage.financials.rows.acquisitionCash', 'CF', 'ACQUISITION_CASH'),
        line('equityIssueCash', 'stockPage.financials.rows.equityIssueCash', 'CF', 'EQUITY_ISSUE_CASH'),
        line('buyback', 'stockPage.financials.rows.buyback', 'CF', 'BUYBACK'),
        line('dividendsPaid', 'stockPage.financials.rows.dividendsPaid', 'CF', 'DIVIDENDS_PAID'),
        line('interestPaid', 'stockPage.financials.rows.interestPaid', 'CF', 'INTEREST_PAID'),
        line('debtIssued', 'stockPage.financials.rows.debtIssued', 'CF', 'DEBT_ISSUED'),
        line('debtRepaid', 'stockPage.financials.rows.debtRepaid', 'CF', 'DEBT_REPAID'),
      ],
    },
  ],
}

/** The three statement sections, in the spec's order. */
export const STATEMENT_SECTIONS: StatementSection[] = [
  INCOME_STATEMENT,
  BALANCE_SHEET,
  CASH_FLOW,
]

/** The served figure a row binds to, or `null` when the period carries none. */
export function rowFigure(
  period: FinancialPeriod,
  row: StatementRow,
): StatementFigure | null {
  const source = row.source
  if (source.kind === 'unserved') return null
  if (source.kind === 'period') return period[source.field] ?? null
  if (source.kind === 'derived') {
    const derived = period.derived
    return derived && Object.hasOwn(derived, source.code) ? derived[source.code] : null
  }
  const lines: StatementLines | null | undefined = period.lines
  const block = lines ? lines[source.statement] : null
  return block && Object.hasOwn(block, source.code) ? block[source.code] : null
}

/** True when a payload carries the statements-view surface (TKT-foundation-094). */
export function servesStatements(periods: FinancialPeriod[]): boolean {
  return periods.some((period) => period.lines != null)
}

/**
 * The periods the toggle selects — newest first. A payload whose `periodType` is
 * outside the vocabulary renders under neither basis rather than being relabelled.
 */
export function periodsForBasis(
  periods: FinancialPeriod[],
  basis: StatementBasis,
): FinancialPeriod[] {
  const wanted = basis === 'annual' ? ANNUAL_PERIOD_TYPE : QUARTER_PERIOD_TYPE
  return periods
    .filter((period) => period.periodType?.trim().toUpperCase() === wanted)
    .slice()
    .sort((left, right) => right.periodEndDate.localeCompare(left.periodEndDate))
}

/** Splits an ISO date into its year and month/day parts, or `null`. */
function parseIsoDate(isoDate: string): { year: number; month: string; day: string } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(isoDate ?? '')
  if (!match) return null
  return { year: Number(match[1]), month: match[2], day: match[3] }
}

/** `2026-01-01` + `2026-06-30` → `01.01–30.06` (the filing's own declared range). */
export function spanRange(startDate: string, endDate: string): string | null {
  const start = parseIsoDate(startDate)
  const end = parseIsoDate(endDate)
  if (!start || !end) return null
  return `${start.day}.${start.month}–${end.day}.${end.month}`
}

/**
 * A period's label under the selected basis.
 *
 * - **Yıllık** — the fiscal-year label (`FY2025`), as the section has always said.
 * - **Çeyreklik with a served span** — the honest cumulative label built from the
 *   filing's own declared range (`2026 6 Aylık (01.01–30.06)`), closing 090's R-1
 *   on the surface: the stored interim value is cumulative, so the label says so.
 * - **Çeyreklik with no span** — today's quarter label, never a fabricated range.
 * - **Anything else** — the period-end date, as before.
 */
export function periodLabel(
  period: FinancialPeriod,
  basis: StatementBasis,
  t: TFunction,
  locale: string,
): string {
  const periodType = period.periodType?.trim().toUpperCase()

  if (periodType === ANNUAL_PERIOD_TYPE) {
    return t('stockPage.financials.period', { year: period.fiscalYear })
  }

  const periodEnd = parseIsoDate(period.periodEndDate)
  const span = period.span
  if (basis === 'quarterly' && span) {
    const range = spanRange(span.periodStartDate, period.periodEndDate)
    if (range) {
      return t('stockPage.financials.spanLabel', {
        year: periodEnd?.year ?? period.fiscalYear,
        months: span.durationMonths,
        range,
      })
    }
  }

  if (periodType === QUARTER_PERIOD_TYPE && periodEnd) {
    return t('stockPage.financials.periodQuarter', {
      quarter: Math.ceil(Number(periodEnd.month) / 3),
      year: periodEnd.year,
    })
  }

  return t('stockPage.financials.periodEnding', {
    date: formatAsOfDate(locale, period.periodEndDate),
  })
}

export type FilingBadgeId = 'consolidated' | 'solo' | 'tms29' | 'kapSource'

export interface FilingBadge {
  id: FilingBadgeId
  labelKey: string
}

/**
 * The badges the served filing licenses — and nothing else. `consolidated` is the
 * filing's own flag (`true` → Konsolide, `false` → Solo, `null`/absent → no badge);
 * `tms29` and `kapSource` render only when the server served `true`. No badge is
 * derived on the client.
 */
export function filingBadges(filing: FilingMetadata | null | undefined): FilingBadge[] {
  if (!filing) return []
  const badges: FilingBadge[] = []
  if (filing.consolidated === true) {
    badges.push({ id: 'consolidated', labelKey: 'stockPage.financials.badges.consolidated' })
  } else if (filing.consolidated === false) {
    badges.push({ id: 'solo', labelKey: 'stockPage.financials.badges.solo' })
  }
  if (filing.tms29 === true) {
    badges.push({ id: 'tms29', labelKey: 'stockPage.financials.badges.tms29' })
  }
  if (filing.kapSource === true) {
    badges.push({ id: 'kapSource', labelKey: 'stockPage.financials.badges.kapSource' })
  }
  return badges
}

export interface EvHeaderRow {
  key: 'sharePrice' | 'marketCap' | 'grossDebt' | 'cash' | 'minorityInterest'
  labelKey: string
  fact: EvFact | null
}

/**
 * The EV header's served operands, in the spec's order. The **total** is not among
 * them: no `enterpriseValue` is served, and the page may not compute it (BR-RES-004
 * — the EV arithmetic is a server fact). The header renders the total row as the
 * honest dash until the serving surface carries it.
 */
export function evHeaderRows(evHeader: EvHeader | null | undefined): EvHeaderRow[] {
  return [
    {
      key: 'sharePrice',
      labelKey: 'stockPage.financials.ev.sharePrice',
      fact: evHeader?.sharePrice ?? null,
    },
    {
      key: 'marketCap',
      labelKey: 'stockPage.financials.ev.marketCap',
      fact: evHeader?.marketCap ?? null,
    },
    {
      key: 'grossDebt',
      labelKey: 'stockPage.financials.ev.grossDebt',
      fact: evHeader?.grossDebt ?? null,
    },
    {
      key: 'cash',
      labelKey: 'stockPage.financials.ev.cash',
      fact: evHeader?.cash ?? null,
    },
    {
      key: 'minorityInterest',
      labelKey: 'stockPage.financials.ev.minorityInterest',
      fact: evHeader?.minorityInterest ?? null,
    },
  ]
}
