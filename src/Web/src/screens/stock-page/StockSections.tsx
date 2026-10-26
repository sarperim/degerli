import type { ReactNode } from 'react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'

import { NotMeaningful, RestatedMarker } from '@/components/honest-data'
import { EmptyState } from '@/components/ui/empty-state'
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import type { FinancialFigure, FinancialPeriod } from '@/lib/api/contracts'
import {
  formatAsOfDate,
  formatCurrency,
  formatNumber,
  formatPercent,
} from '@/lib/format'
import { SectionBody } from '@/screens/stock-page/SectionBody'
import { SectionMarkers } from '@/screens/stock-page/SectionMarkers'
import {
  fetchStockBalanceSheet,
  fetchStockDividends,
  fetchStockFinancials,
  fetchStockGrowth,
  fetchStockOverview,
  fetchStockProfitability,
  isDividendsMissing,
  isMissingData,
  stockPageQueryKeys,
} from '@/screens/stock-page/api'

/** Shared section chrome: heading + labelled body. */
export function SectionShell({
  section,
  titleKey,
  children,
}: {
  section: string
  titleKey: string
  children: ReactNode
}) {
  const { t } = useTranslation()
  const headingId = `stock-section-${section}`
  return (
    <section
      data-section={section}
      aria-labelledby={headingId}
      className="flex scroll-mt-24 flex-col gap-4 rounded-lg border bg-background p-4"
    >
      <h2 id={headingId} className="text-h3 font-semibold text-foreground">
        {t(titleKey)}
      </h2>
      {children}
    </section>
  )
}

/** A canonical figure: value + restatement asterisk, or an explicit no-data dash. */
function FigureValue({
  figure,
  locale,
  percent = false,
  unit,
}: {
  figure: FinancialFigure
  locale: string
  percent?: boolean
  /** Localized unit label appended to the value (`TKT-foundation-072`). */
  unit?: string
}) {
  const { t } = useTranslation()
  if (figure.value === null) {
    return (
      <span data-slot="no-value" title={t('honestData.noDataTitle')}>
        —
      </span>
    )
  }
  return (
    <span className="tabular-nums">
      {percent
        ? formatPercent(locale, figure.value, 2)
        : formatNumber(locale, figure.value)}
      {unit ? <span data-slot="unit"> {unit}</span> : null}
      {figure.restated ? (
        <>
          {' '}
          <RestatedMarker />
        </>
      ) : null}
    </span>
  )
}

const FINANCIAL_COLUMNS = [
  'revenue',
  'ebitda',
  'ebit',
  'netIncome',
  'fcf',
] as const satisfies readonly (keyof FinancialPeriod)[]

const FINANCIAL_COLUMN_KEYS: Record<(typeof FINANCIAL_COLUMNS)[number], string> = {
  revenue: 'stockPage.financials.columns.revenue',
  ebitda: 'stockPage.financials.columns.ebitda',
  ebit: 'stockPage.financials.columns.ebit',
  netIncome: 'stockPage.financials.columns.netIncome',
  fcf: 'stockPage.financials.columns.fcf',
}

/** SCR-005 §1 — Overview & Business Description (FR-RES-005/022/026). */
export function OverviewSection({ symbol }: { symbol: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.sections.overview')
  const query = useQuery({
    queryKey: stockPageQueryKeys.overview(symbol),
    queryFn: ({ signal }) => fetchStockOverview(symbol, signal),
  })

  return (
    <SectionShell section="overview" titleKey="stockPage.sections.overview">
      <SectionBody
        label={label}
        isPending={query.isPending}
        isError={query.isError}
        onRetry={() => void query.refetch()}
      >
        {query.data ? (
          query.data.status === 'preparing' ? (
            <div className="flex flex-col gap-3">
              <SectionMarkers
                asOf={query.data.asOf}
                stale={query.data.stale}
                state="preparing"
              />
            </div>
          ) : (
            <div className="flex flex-col gap-3">
              <p className="max-w-prose text-sm text-foreground">
                {locale === 'en' ? query.data.textEn : query.data.textTr}
              </p>
              {query.data.lastReviewedAt ? (
                <p className="text-xs text-muted-foreground">
                  {t('stockPage.descriptionLastReviewed', {
                    date: formatAsOfDate(locale, query.data.lastReviewedAt),
                  })}
                </p>
              ) : null}
              <SectionMarkers asOf={query.data.asOf} stale={query.data.stale} />
            </div>
          )
        ) : null}
      </SectionBody>
    </SectionShell>
  )
}

/** Splits an ISO `periodEndDate` prefix into its year and month, or `null`. */
function parsePeriodEnd(periodEndDate: string): { year: number; month: number } | null {
  const match = /^(\d{4})-(\d{2})/.exec(periodEndDate ?? '')
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  if (!Number.isInteger(year) || month < 1 || month > 12) return null
  return { year, month }
}

/**
 * The financials row's period label, derived from what the API actually sends
 * (TKT-foundation-057; FR-RES-017 — the label is a claim about the data).
 *
 * `FY` → the fiscal-year label; `Q` → the quarter from the period-end month
 * (Jan–Mar → Q1 … Oct–Dec → Q4) and that period's year; anything else — absent or
 * unexpected `periodType`, or an unparseable `periodEndDate` — degrades to the
 * period-end date itself: no fabricated quarter, no implied fiscal year.
 */
function financialPeriodLabel(
  period: FinancialPeriod,
  t: TFunction,
  locale: string,
): string {
  const periodType = period.periodType?.trim().toUpperCase()

  if (periodType === 'FY') {
    return t('stockPage.financials.period', { year: period.fiscalYear })
  }

  const periodEnd = parsePeriodEnd(period.periodEndDate)
  if (periodType === 'Q' && periodEnd) {
    return t('stockPage.financials.periodQuarter', {
      quarter: Math.ceil(periodEnd.month / 3),
      year: periodEnd.year,
    })
  }

  return t('stockPage.financials.periodEnding', {
    date: formatAsOfDate(locale, period.periodEndDate),
  })
}

/** SCR-005 §3 — Financials (FR-RES-008/014). */
export function FinancialsSection({ symbol }: { symbol: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.sections.financials')
  const query = useQuery({
    queryKey: stockPageQueryKeys.financials(symbol),
    queryFn: ({ signal }) => fetchStockFinancials(symbol, signal),
  })

  const data = query.data
  const missingData = data !== undefined && isMissingData(data) ? data : null
  const sectionData = data !== undefined && !isMissingData(data) ? data : null
  const anyRestated = sectionData
    ? sectionData.periods.some((period) =>
        FINANCIAL_COLUMNS.some((column) => period[column].restated),
      )
    : false

  return (
    <SectionShell section="financials" titleKey="stockPage.sections.financials">
      <SectionBody
        label={label}
        isPending={query.isPending}
        isError={query.isError}
        onRetry={() => void query.refetch()}
      >
        {missingData ? (
          <SectionMarkers
            asOf={missingData.asOf}
            stale={missingData.stale}
            state={missingData.state}
            coverage={missingData.coverage}
          />
        ) : null}

        {sectionData ? (
          <div className="flex flex-col gap-3">
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('stockPage.financials.columns.period')}</TableHead>
                    {FINANCIAL_COLUMNS.map((column) => (
                      <TableHead key={column}>
                        {t(FINANCIAL_COLUMN_KEYS[column])}
                      </TableHead>
                    ))}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {sectionData.periods.map((period) => (
                    <TableRow key={`${period.periodType}-${period.periodEndDate}`}>
                      <TableHead scope="row" className="font-medium text-foreground">
                        {financialPeriodLabel(period, t, locale)}
                      </TableHead>
                      {FINANCIAL_COLUMNS.map((column) => (
                        <TableCell key={column}>
                          <FigureValue figure={period[column]} locale={locale} />
                        </TableCell>
                      ))}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
            <SectionMarkers
              asOf={sectionData.asOf}
              stale={sectionData.stale}
              restated={anyRestated}
            />
          </div>
        ) : null}
      </SectionBody>
    </SectionShell>
  )
}

const PROFITABILITY_METRICS = [
  'roic',
  'roe',
  'gross_margin',
  'operating_margin',
] as const

const PROFITABILITY_LABEL_KEYS: Record<
  (typeof PROFITABILITY_METRICS)[number],
  string
> = {
  roic: 'stockPage.profitability.metrics.roic',
  roe: 'stockPage.profitability.metrics.roe',
  gross_margin: 'stockPage.profitability.metrics.gross_margin',
  operating_margin: 'stockPage.profitability.metrics.operating_margin',
}

/** SCR-005 §4 — Profitability (FR-RES-009). */
export function ProfitabilitySection({ symbol }: { symbol: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.sections.profitability')
  const query = useQuery({
    queryKey: stockPageQueryKeys.profitability(symbol),
    queryFn: ({ signal }) => fetchStockProfitability(symbol, signal),
  })

  const data = query.data
  const missingData = data !== undefined && isMissingData(data) ? data : null
  const sectionData = data !== undefined && !isMissingData(data) ? data : null

  return (
    <SectionShell
      section="profitability"
      titleKey="stockPage.sections.profitability"
    >
      <SectionBody
        label={label}
        isPending={query.isPending}
        isError={query.isError}
        onRetry={() => void query.refetch()}
      >
        {missingData ? (
          <SectionMarkers
            asOf={missingData.asOf}
            stale={missingData.stale}
            state={missingData.state}
            coverage={missingData.coverage}
          />
        ) : null}

        {sectionData ? (
          <div className="flex flex-col gap-3">
            <dl className="grid gap-3 sm:grid-cols-2">
              {PROFITABILITY_METRICS.map((code) => {
                const metric = sectionData.metrics[code]
                const title = t(PROFITABILITY_LABEL_KEYS[code])
                return (
                  <div key={code} className="flex flex-col gap-1">
                    <dt className="text-xs text-muted-foreground">{title}</dt>
                    <dd className="text-sm font-medium text-foreground">
                      {metric === undefined ? (
                        <span
                          data-slot="no-value"
                          title={t('honestData.noDataTitle')}
                        >
                          —
                        </span>
                      ) : metric.notMeaningful || metric.value === null ? (
                        <NotMeaningful />
                      ) : (
                        <span className="tabular-nums">
                          {formatPercent(locale, metric.value, 2)}
                        </span>
                      )}
                    </dd>
                  </div>
                )
              })}
            </dl>
            <SectionMarkers asOf={sectionData.asOf} stale={sectionData.stale} />
          </div>
        ) : null}
      </SectionBody>
    </SectionShell>
  )
}

const GROWTH_FAMILIES = ['rev', 'eps', 'fcf', 'div'] as const

const GROWTH_FAMILY_KEYS: Record<(typeof GROWTH_FAMILIES)[number], string> = {
  rev: 'stockPage.growth.families.rev',
  eps: 'stockPage.growth.families.eps',
  fcf: 'stockPage.growth.families.fcf',
  div: 'stockPage.growth.families.div',
}

/** SCR-005 §5 — Growth (FR-RES-010/016). */
export function GrowthSection({ symbol }: { symbol: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.sections.growth')
  const query = useQuery({
    queryKey: stockPageQueryKeys.growth(symbol),
    queryFn: ({ signal }) => fetchStockGrowth(symbol, signal),
  })

  const data = query.data
  const missingData = data !== undefined && isMissingData(data) ? data : null
  const sectionData = data !== undefined && !isMissingData(data) ? data : null

  return (
    <SectionShell section="growth" titleKey="stockPage.sections.growth">
      <SectionBody
        label={label}
        isPending={query.isPending}
        isError={query.isError}
        onRetry={() => void query.refetch()}
      >
        {missingData ? (
          <SectionMarkers
            asOf={missingData.asOf}
            stale={missingData.stale}
            state={missingData.state}
            coverage={missingData.coverage}
          />
        ) : null}

        {sectionData ? (
          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-3">
              {GROWTH_FAMILIES.map((family) => {
                const windows = sectionData.metrics[family] ?? []
                return (
                  <div key={family} className="flex flex-col gap-1.5">
                    <p className="text-xs font-medium text-muted-foreground">
                      {t(GROWTH_FAMILY_KEYS[family])}
                    </p>
                    <ul className="flex flex-wrap gap-2">
                      {windows.map((window) => (
                        <li
                          key={window.window}
                          data-slot="growth-window"
                          className="flex flex-col rounded-md border px-2 py-1 text-xs"
                        >
                          <span className="font-medium text-foreground">
                            {t('stockPage.growth.window', { years: window.window })}
                          </span>
                          {window.value === null ? (
                            <span
                              data-slot="no-value"
                              title={t('honestData.noDataTitle')}
                            >
                              —
                            </span>
                          ) : (
                            <span className="tabular-nums">
                              {formatPercent(locale, window.value, 2)}
                            </span>
                          )}
                          {window.windowYears !== null &&
                          window.windowYears < window.window ? (
                            <span
                              data-slot="actual-window"
                              className="text-muted-foreground"
                            >
                              {t('stockPage.growth.actualWindow', {
                                years: window.windowYears,
                              })}
                            </span>
                          ) : null}
                        </li>
                      ))}
                    </ul>
                  </div>
                )
              })}
            </div>
            <SectionMarkers asOf={sectionData.asOf} stale={sectionData.stale} />
          </div>
        ) : null}
      </SectionBody>
    </SectionShell>
  )
}

const BALANCE_SHEET_LINES = [
  'cash',
  'shortTermDebt',
  'longTermDebt',
  'totalEquity',
  'currentAssets',
  'currentLiabilities',
  'sharesDiluted',
  'sharesOutstanding',
] as const

const BALANCE_SHEET_LINE_KEYS: Record<
  (typeof BALANCE_SHEET_LINES)[number],
  string
> = {
  cash: 'stockPage.balanceSheet.lines.cash',
  shortTermDebt: 'stockPage.balanceSheet.lines.shortTermDebt',
  longTermDebt: 'stockPage.balanceSheet.lines.longTermDebt',
  totalEquity: 'stockPage.balanceSheet.lines.totalEquity',
  currentAssets: 'stockPage.balanceSheet.lines.currentAssets',
  currentLiabilities: 'stockPage.balanceSheet.lines.currentLiabilities',
  sharesDiluted: 'stockPage.balanceSheet.lines.sharesDiluted',
  // TKT-foundation-086 (reverting the 072 mislabel, 065 consequence 2): this line is
  // the statement's `SHARES_OUT` row — a COUNT of shares, not money. The paid-in-capital
  // figure lives in the `shareCount.paidInCapital` block and never on this row; the unit
  // says a count. No statements decoder emits `SHARES_OUT` today, so the row stays an
  // honest dash until one does.
  sharesOutstanding: 'stockPage.balanceSheet.lines.sharesOutstanding',
}

/** The balance-sheet line carrying a share count — the unit is a count, never TRY. */
const SHARES_OUTSTANDING_LINE: (typeof BALANCE_SHEET_LINES)[number] = 'sharesOutstanding'

/** SCR-005 §6 — Balance Sheet (FR-RES-011/027). */
export function BalanceSheetSection({ symbol }: { symbol: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.sections.balanceSheet')
  const query = useQuery({
    queryKey: stockPageQueryKeys.balanceSheet(symbol),
    queryFn: ({ signal }) => fetchStockBalanceSheet(symbol, signal),
  })

  const data = query.data
  const missingData = data !== undefined && isMissingData(data) ? data : null
  const sectionData = data !== undefined && !isMissingData(data) ? data : null

  return (
    <SectionShell section="balanceSheet" titleKey="stockPage.sections.balanceSheet">
      <SectionBody
        label={label}
        isPending={query.isPending}
        isError={query.isError}
        onRetry={() => void query.refetch()}
      >
        {missingData ? (
          <SectionMarkers
            asOf={missingData.asOf}
            stale={missingData.stale}
            state={missingData.state}
            coverage={missingData.coverage}
          />
        ) : null}

        {sectionData ? (
          <div className="flex flex-col gap-3">
            <p className="text-xs text-muted-foreground">
              {t('stockPage.balanceSheet.period', { year: sectionData.fiscalYear })}
            </p>
            <dl className="grid gap-3 sm:grid-cols-2">
              {BALANCE_SHEET_LINES.map((code) => (
                <div
                  key={code}
                  data-line={code}
                  className="flex items-center justify-between gap-3"
                >
                  <dt className="text-xs text-muted-foreground">
                    {t(BALANCE_SHEET_LINE_KEYS[code])}
                  </dt>
                  <dd className="text-sm font-medium text-foreground">
                    <FigureValue
                      figure={
                        sectionData.lines[code] ?? { value: null, restated: false }
                      }
                      locale={locale}
                      unit={code === SHARES_OUTSTANDING_LINE ? t('units.count') : undefined}
                    />
                  </dd>
                </div>
              ))}
              <div className="flex items-center justify-between gap-3">
                <dt className="text-xs text-muted-foreground">
                  {t('stockPage.balanceSheet.bookValue')}
                </dt>
                <dd className="text-sm font-medium text-foreground">
                  <FigureValue figure={sectionData.bookValue} locale={locale} />
                </dd>
              </div>
              <div className="flex items-center justify-between gap-3">
                <dt className="text-xs text-muted-foreground">
                  {t('stockPage.balanceSheet.bookValuePerShare')}
                </dt>
                <dd className="text-sm font-medium text-foreground">
                  <FigureValue figure={sectionData.bookValuePerShare} locale={locale} />
                </dd>
              </div>
            </dl>
            <SectionMarkers
              asOf={sectionData.asOf}
              stale={sectionData.stale}
              restated={sectionData.restated}
            />
          </div>
        ) : null}
      </SectionBody>
    </SectionShell>
  )
}

/** SCR-005 §7 — Dividends (FR-RES-012), with the three honest states (TKT-foundation-052). */
export function DividendsSection({ symbol }: { symbol: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.sections.dividends')
  const query = useQuery({
    queryKey: stockPageQueryKeys.dividends(symbol),
    queryFn: ({ signal }) => fetchStockDividends(symbol, signal),
  })

  const data = query.data
  const missingData = data !== undefined && isDividendsMissing(data) ? data : null
  const sectionData = data !== undefined && !isDividendsMissing(data) ? data : null

  const coverageBoundary = missingData?.coverage?.availableFrom
    ? t('honestData.coverageBoundary', {
        date: formatAsOfDate(locale, missingData.coverage.availableFrom),
      })
    : undefined

  // The server's availability signal decides which empty state is honest: the
  // never-paid wording only over data the lane has actually covered. No signal (a
  // pre-052 payload) degrades to the neutral state, never to the claim.
  const dividendsAvailable = missingData?.dividendsAvailable === true

  return (
    <SectionShell section="dividends" titleKey="stockPage.sections.dividends">
      <SectionBody
        label={label}
        isPending={query.isPending}
        isError={query.isError}
        onRetry={() => void query.refetch()}
      >
        {missingData ? (
          missingData.state !== 'no-data' ? (
            // Preparing / unavailable are the server's own honest states — rendered as-is.
            <SectionMarkers
              asOf={missingData.asOf}
              stale={missingData.stale}
              state={missingData.state}
              coverage={missingData.coverage}
            />
          ) : dividendsAvailable ? (
            <EmptyState
              data-testid="dividends-never-paid"
              data-state={missingData.state}
              title={t('stockPage.dividends.neverPaidTitle')}
              description={[t('stockPage.dividends.neverPaidBody'), coverageBoundary]
                .filter(Boolean)
                .join(' ')}
            />
          ) : (
            <EmptyState
              data-testid="dividends-unavailable"
              data-state={missingData.state}
              title={t('stockPage.dividends.unavailableTitle')}
              description={[
                t('stockPage.dividends.unavailableBody'),
                coverageBoundary,
              ]
                .filter(Boolean)
                .join(' ')}
            />
          )
        ) : null}

        {sectionData ? (
          <div className="flex flex-col gap-3">
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('stockPage.dividends.columns.exDate')}</TableHead>
                    <TableHead>{t('stockPage.dividends.columns.payDate')}</TableHead>
                    <TableHead>{t('stockPage.dividends.columns.amount')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {sectionData.dividends.map((dividend) => (
                    <TableRow key={`${dividend.exDate}-${dividend.payDate}`}>
                      <TableCell>{dividend.exDate}</TableCell>
                      <TableCell>{dividend.payDate}</TableCell>
                      <TableCell className="tabular-nums">
                        {formatCurrency(
                          locale,
                          dividend.amountPerShare,
                          dividend.currency,
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
            {sectionData.ttmPerShare !== undefined ? (
              <p className="text-xs text-muted-foreground">
                {t('stockPage.dividends.ttm')}:{' '}
                <span className="tabular-nums">
                  {formatNumber(locale, sectionData.ttmPerShare)}
                </span>
              </p>
            ) : null}
            <SectionMarkers asOf={sectionData.asOf} stale={sectionData.stale} />
          </div>
        ) : null}
      </SectionBody>
    </SectionShell>
  )
}
