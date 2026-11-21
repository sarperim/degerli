import type { ReactNode } from 'react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'

import {
  AdjustedNotice,
  AsOfDate,
  NotMeaningful,
  RestatedMarker,
  StaleBadge,
} from '@/components/honest-data'
import { Button } from '@/components/ui/button'
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
import { KeyMetricsPanel } from '@/screens/stock-page/KeyMetricsPanel'
import { LayoutNoticeBlock, StatementsView } from '@/screens/stock-page/StatementsView'
import { ValuationHistoryChart } from '@/screens/stock-page/ValuationSection'
import { servesStatements } from '@/screens/stock-page/statementsView'

import {
  fetchStockBalanceSheet,
  fetchStockDividends,
  fetchStockFinancials,
  fetchStockGrowth,
  fetchStockOverview,
  fetchStockProfitability,
  fetchStockValuation,
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
      className="flex scroll-mt-32 flex-col gap-4 border-t border-border pt-6"
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
export function OverviewSection({
  symbol,
  onOpenKapReports,
}: {
  symbol: string
  onOpenKapReports?: () => void
}) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.sections.overview')
  const query = useQuery({
    queryKey: stockPageQueryKeys.overview(symbol),
    queryFn: ({ signal }) => fetchStockOverview(symbol, signal),
  })
  const valuationQuery = useQuery({
    queryKey: stockPageQueryKeys.valuation(symbol),
    queryFn: ({ signal }) => fetchStockValuation(symbol, signal),
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
          <div className="flex flex-col gap-5">
            {valuationQuery.data && valuationQuery.data.historical.series.length > 0 ? (
              <div className="flex flex-col gap-2 border-b border-border pb-5">
                <ValuationHistoryChart historical={valuationQuery.data.historical} />
                {valuationQuery.data.historical.adjusted ? <AdjustedNotice /> : null}
                {query.data.status === 'preparing' ? (
                  <div className="flex flex-wrap items-center gap-3">
                    <AsOfDate asOf={valuationQuery.data.asOf} />
                    {valuationQuery.data.stale ? <StaleBadge /> : null}
                  </div>
                ) : null}
              </div>
            ) : null}

            {query.data.status === 'preparing' ? (
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
            )}

            {onOpenKapReports ? (
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="w-fit font-mono text-xs"
                onClick={onOpenKapReports}
              >
                {t('stockPage.overview.openKapReports')}
              </Button>
            ) : null}
          </div>
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
  // Defensive on a malformed served field: a non-string `periodType` degrades to the
  // period-end label instead of throwing (no ErrorBoundary above these sections).
  const periodType =
    typeof period.periodType === 'string' ? period.periodType.trim().toUpperCase() : undefined

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

/**
 * True when any of the five executive summary columns (`FINANCIAL_COLUMNS`) is
 * restated. Deep statement-only restatements only badge `08 KAP Reports`, never
 * `03 Financials` where those lines are not rendered.
 */
function anySummaryFigureRestated(periods: FinancialPeriod[]): boolean {
  return periods.some((period) =>
    // `?.` on the compat column: a malformed payload may omit a figure, and an absent
    // figure is not restated (it must not throw either — no ErrorBoundary above here).
    FINANCIAL_COLUMNS.some((column) => period[column]?.restated === true),
  )
}

/**
 * True when any served figure the statements view renders is restated — the compat
 * five figures, any served canonical line or the `OTHER_*` passthrough, or any [C]
 * row. The footnote is a claim about the display, so it covers the whole display.
 */
function anyServedFigureRestated(periods: FinancialPeriod[]): boolean {
  const restatedIn = (map: Record<string, { restated: boolean }> | null | undefined) =>
    map !== null && map !== undefined
      ? Object.values(map).some((figure) => figure?.restated === true)
      : false

  return (
    anySummaryFigureRestated(periods) ||
    periods.some((period) => {
      const blocks = [period.lines, period.otherLines]
      return (
        blocks.some(
          (lines) =>
            restatedIn(lines?.IS) || restatedIn(lines?.BS) || restatedIn(lines?.CF),
        ) ||
        restatedIn(period.derived)
      )
    })
  )
}

/** Shared five-figure executive summary table for Financials and pre-094 KAP fallback. */
function FinancialsSummaryTable({
  periods,
  t,
  locale,
}: {
  periods: FinancialPeriod[]
  t: TFunction
  locale: string
}) {
  return (
    <TableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{t('stockPage.financials.columns.period')}</TableHead>
            {FINANCIAL_COLUMNS.map((column) => (
              <TableHead key={column}>{t(FINANCIAL_COLUMN_KEYS[column])}</TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {periods.map((period) => (
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
  )
}

/**
 * SCR-005 §3 — Financials (FR-RES-008/014).
 *
 * Simplified executive view: FY grouped bar chart + five-figure summary table
 * + jump button to `08 KAP Reports` (`KapReportsSection`) for full KAP statements.
 */
export function FinancialsSection({
  symbol,
  onOpenKapReports,
}: {
  symbol: string
  onOpenKapReports?: () => void
}) {
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
  const notice = sectionData?.layoutNotice?.applies ? sectionData.layoutNotice : null
  const anyRestated =
    sectionData && !notice ? anySummaryFigureRestated(sectionData.periods) : false

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
          <div className="flex flex-col gap-5">
            {onOpenKapReports ? (
              <div className="flex flex-wrap items-center justify-end gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="w-fit font-mono text-xs"
                  onClick={onOpenKapReports}
                >
                  {t('stockPage.financials.openKapReports')}
                </Button>
              </div>
            ) : null}
            {notice ? <LayoutNoticeBlock notice={notice} /> : null}
            {!notice
              ? (() => {
                  const chartPeriods = sectionData.periods
                    .filter(
                      (p) =>
                        p.periodType?.trim().toUpperCase() === 'FY' &&
                        (p.revenue?.value != null ||
                          p.ebitda?.value != null ||
                          p.netIncome?.value != null),
                    )
                    .slice(0, 6)
                    .reverse()
                  if (chartPeriods.length < 2) return null
                  const maxVal = Math.max(
                    1,
                    ...chartPeriods.flatMap((p) => [
                      Math.abs(p.revenue?.value ?? 0),
                      Math.abs(p.ebitda?.value ?? 0),
                      Math.abs(p.netIncome?.value ?? 0),
                    ]),
                  )
                  const W = 540
                  const H = 136
                  const pB = 22
                  const pT = 10
                  const slotW = W / chartPeriods.length
                  const barW = Math.min(24, Math.max(10, Math.floor((slotW - 22) / 3)))
                  const barH = (v: number | null | undefined) =>
                    v === null || v === undefined
                      ? 0
                      : Math.max(2, (Math.abs(v) / maxVal) * (H - pB - pT))
                  return (
                    <div
                      data-slot="financials-grouped-chart"
                      className="border-b border-border pb-5"
                    >
                      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2 font-mono text-[11px] text-muted-foreground">
                        <span>
                          {t('stockPage.financials.columns.revenue')} ·{' '}
                          {t('stockPage.financials.columns.ebitda')} ·{' '}
                          {t('stockPage.financials.columns.netIncome')}
                        </span>
                        <span aria-hidden="true" className="flex items-center gap-3">
                          <span className="inline-flex items-center gap-1">
                            <span className="text-[var(--border-strong)]">■</span>
                            {` ${t('stockPage.financials.columns.revenue')}`}
                          </span>
                          <span className="inline-flex items-center gap-1">
                            <span className="text-[var(--brand-accent)]">■</span>
                            {` ${t('stockPage.financials.columns.ebitda')}`}
                          </span>
                          <span className="inline-flex items-center gap-1">
                            <span className="text-[var(--pos)]">■</span>
                            {` ${t('stockPage.financials.columns.netIncome')}`}
                          </span>
                        </span>
                      </div>
                      <svg
                        viewBox={`0 0 ${W} ${H}`}
                        width="100%"
                        height={H}
                        aria-hidden="true"
                      >
                        <line
                          x1={0}
                          y1={H - pB}
                          x2={W}
                          y2={H - pB}
                          stroke="var(--border-strong)"
                        />
                        {chartPeriods.map((period, idx) => {
                          const groupW = barW * 3 + 8
                          const x = idx * slotW + (slotW - groupW) / 2
                          const rH = barH(period.revenue?.value)
                          const eH = barH(period.ebitda?.value)
                          const nH = barH(period.netIncome?.value)
                          const eNeg = (period.ebitda?.value ?? 0) < 0
                          const nNeg = (period.netIncome?.value ?? 0) < 0
                          return (
                            <g key={`${period.periodType}-${period.periodEndDate}`}>
                              {rH > 0 ? (
                                <rect
                                  x={x}
                                  y={H - pB - rH}
                                  width={barW}
                                  height={rH}
                                  fill="var(--border-strong)"
                                />
                              ) : null}
                              {eH > 0 ? (
                                <rect
                                  x={x + barW + 4}
                                  y={H - pB - eH}
                                  width={barW}
                                  height={eH}
                                  fill={eNeg ? 'var(--neg)' : 'var(--brand-accent)'}
                                />
                              ) : null}
                              {nH > 0 ? (
                                <rect
                                  x={x + (barW + 4) * 2}
                                  y={H - pB - nH}
                                  width={barW}
                                  height={nH}
                                  fill={nNeg ? 'var(--neg)' : 'var(--pos)'}
                                />
                              ) : null}
                              <text
                                x={x + groupW / 2}
                                y={H - 5}
                                textAnchor="middle"
                                fill="var(--muted-foreground)"
                                fontFamily="var(--font-mono)"
                                fontSize="10"
                              >
                                {financialPeriodLabel(period, t, locale)}
                              </text>
                            </g>
                          )
                        })}
                      </svg>
                    </div>
                  )
                })()
              : null}
            {!notice && sectionData.periods.length > 0 ? (
              <FinancialsSummaryTable
                periods={sectionData.periods}
                t={t}
                locale={locale}
              />
            ) : null}
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

/**
 * SCR-005 §8 — KAP Reports (TKT-foundation-094/095/097).
 *
 * Dedicated home for full multi-period KAP financial statements (`StatementsView`),
 * key value metrics & moat indicators, and the capital & dilution tracker.
 */
export function KapReportsSection({ symbol }: { symbol: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.sections.kapReports')
  const query = useQuery({
    queryKey: stockPageQueryKeys.financials(symbol),
    queryFn: ({ signal }) => fetchStockFinancials(symbol, signal),
  })

  const data = query.data
  const missingData = data !== undefined && isMissingData(data) ? data : null
  const sectionData = data !== undefined && !isMissingData(data) ? data : null
  const anyRestated =
    sectionData && !sectionData.layoutNotice?.applies
      ? anyServedFigureRestated(sectionData.periods)
      : false
  const statementsView =
    sectionData !== null &&
    (servesStatements(sectionData.periods) || sectionData.layoutNotice?.applies === true)

  return (
    <SectionShell section="kapReports" titleKey="stockPage.sections.kapReports">
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
          <div className="flex flex-col gap-5">
            {statementsView ? (
              <StatementsView
                data={sectionData}
                asOf={sectionData.asOf}
                locale={locale}
              />
            ) : sectionData.periods.length > 0 ? (
              <FinancialsSummaryTable
                periods={sectionData.periods}
                t={t}
                locale={locale}
              />
            ) : null}
            <SectionMarkers
              asOf={sectionData.asOf}
              stale={sectionData.stale}
              restated={anyRestated}
            />
          </div>
        ) : null}
      </SectionBody>
      {/* The key value metrics & moat panel and the capital & dilution tracker
          (TKT-foundation-097) own their own `/key-metrics` query, so this panel
          loads/fails/retries independently of the statements above (M-5). */}
      <KeyMetricsPanel symbol={symbol} />
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
          <div className="flex flex-col gap-4">
            <dl className="flex flex-col divide-y divide-border border-y border-border">
              {PROFITABILITY_METRICS.map((code) => {
                const metric = sectionData.metrics[code]
                const title = t(PROFITABILITY_LABEL_KEYS[code])
                const hasVal =
                  metric !== undefined && !metric.notMeaningful && metric.value !== null
                const barWidth = hasVal
                  ? Math.min(100, Math.max(2, Math.abs(metric.value! * 100) * 2.2))
                  : 0
                return (
                  <div
                    key={code}
                    className="grid grid-cols-[minmax(120px,180px)_1fr_auto] items-center gap-4 py-3"
                  >
                    <dt className="font-mono text-xs text-muted-foreground">{title}</dt>
                    <div
                      aria-hidden="true"
                      className="relative h-2 w-full bg-secondary"
                    >
                      {hasVal ? (
                        <span
                          className={
                            metric.value! < 0
                              ? 'block h-full bg-[var(--neg)]'
                              : code === 'roic'
                                ? 'block h-full bg-[var(--brand-accent)]'
                                : 'block h-full bg-muted-foreground'
                          }
                          style={{ width: `${barWidth}%` }}
                        />
                      ) : null}
                    </div>
                    <dd className="min-w-[72px] text-right font-mono text-sm font-medium text-foreground">
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
                        <span
                          className={
                            metric.value < 0
                              ? 'tabular-nums text-[var(--neg)]'
                              : 'tabular-nums'
                          }
                        >
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
          <div className="flex flex-col gap-5">
            {(() => {
              const allValues = GROWTH_FAMILIES.flatMap(
                (f) => sectionData.metrics[f] ?? [],
              )
                .map((w) => w.value)
                .filter((v): v is number => v !== null && Number.isFinite(v))
              const maxAbsCagr = Math.max(0.4, ...allValues.map((v) => Math.abs(v)))
              const windowOrder = [3, 5, 10] as const
              const endYear = Number(sectionData.asOf.slice(0, 4)) || 2026
              const startYear = endYear - 10
              const W = 680
              const H = 88
              const pL = 52
              const pR = 24
              const xYr = (yr: number) =>
                pL + ((yr - startYear) / 10) * (W - pL - pR)
              const tmsX = xYr(2023.95)
              const revWindows = sectionData.metrics.rev ?? []
              const spanFor = (target: number) => {
                const found = revWindows.find((w) => w.window === target)
                if (!found || found.value === null) return null
                const yrs = found.windowYears ?? target
                return xYr(Math.max(startYear, endYear - yrs))
              }
              const w10 = spanFor(10)
              const w5 = spanFor(5)
              const w3 = spanFor(3)

              return (
                <>
                  <div
                    data-slot="growth-timeline"
                    className="border-b border-border pb-5"
                  >
                    <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2 font-mono text-[11px] text-muted-foreground">
                      <span>3Y / 5Y / 10Y · TMS 29 (2023-12-31)</span>
                      <span>
                        {startYear} → {endYear}
                      </span>
                    </div>
                    <svg
                      viewBox={`0 0 ${W} ${H}`}
                      width="100%"
                      height={H}
                      aria-hidden="true"
                    >
                      <line
                        x1={pL}
                        y1={68}
                        x2={W - pR}
                        y2={68}
                        stroke="var(--border-strong)"
                      />
                      {[0, 2, 4, 6, 8, 10].map((offset) => {
                        const yr = startYear + offset
                        return (
                          <g key={yr}>
                            <line
                              x1={xYr(yr)}
                              y1={64}
                              x2={xYr(yr)}
                              y2={72}
                              stroke="var(--border-strong)"
                            />
                            <text
                              x={xYr(yr)}
                              y={84}
                              textAnchor="middle"
                              fill="var(--muted-foreground)"
                              fontFamily="var(--font-mono)"
                              fontSize="9.5"
                            >
                              {yr}
                            </text>
                          </g>
                        )
                      })}
                      {tmsX >= pL && tmsX <= W - pR ? (
                        <>
                          <line
                            x1={tmsX}
                            y1={6}
                            x2={tmsX}
                            y2={68}
                            stroke="var(--regime)"
                            strokeWidth="1.2"
                            strokeDasharray="3 3"
                          />
                          <text
                            x={tmsX - 5}
                            y={13}
                            textAnchor="end"
                            fill="var(--regime)"
                            fontFamily="var(--font-mono)"
                            fontSize="9"
                          >
                            TMS 29 (2023-12-31)
                          </text>
                        </>
                      ) : null}
                      {[
                        { label: '10Y', y: 26, start: w10 },
                        { label: '5Y', y: 42, start: w5 },
                        { label: '3Y', y: 58, start: w3 },
                      ].map((row) => (
                        <g key={row.label}>
                          <text
                            x={0}
                            y={row.y + 3}
                            fill="var(--muted-foreground)"
                            fontFamily="var(--font-mono)"
                            fontSize="9.5"
                          >
                            {row.label}
                          </text>
                          {row.start !== null ? (
                            <>
                              <line
                                x1={row.start}
                                y1={row.y}
                                x2={xYr(endYear)}
                                y2={row.y}
                                stroke="var(--brand-accent)"
                                strokeWidth="2.5"
                              />
                              <circle
                                cx={row.start}
                                cy={row.y}
                                r="2.5"
                                fill="var(--brand-accent)"
                              />
                              <circle
                                cx={xYr(endYear)}
                                cy={row.y}
                                r="2.5"
                                fill="var(--brand-accent)"
                              />
                            </>
                          ) : null}
                        </g>
                      ))}
                    </svg>
                  </div>

                  <TableContainer className="rounded-none border-0">
                    <Table>
                      <TableHeader className="bg-transparent">
                        <TableRow className="hover:bg-transparent">
                          <TableHead className="pl-0">
                            {t('stockPage.sections.growth')}
                          </TableHead>
                          {windowOrder.map((yrs) => (
                            <TableHead key={yrs} className="last:pr-0">
                              {t('stockPage.growth.window', { years: yrs })}
                            </TableHead>
                          ))}
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {GROWTH_FAMILIES.map((family) => {
                          const windows = sectionData.metrics[family] ?? []
                          return (
                            <TableRow key={family}>
                              <TableHead
                                scope="row"
                                className="pl-0 align-middle font-semibold text-foreground"
                              >
                                {t(GROWTH_FAMILY_KEYS[family])}
                              </TableHead>
                              {windowOrder.map((yrs, colIdx) => {
                                const w = windows.find((item) => item.window === yrs)
                                // Avoid repeating an identical shorter-span CAGR (e.g. `actual 2Y`)
                                // across longer horizons (`5Y`, `10Y`) when an earlier column
                                // already displayed that exact available span.
                                const isDuplicateShorterSpan =
                                  w !== undefined &&
                                  w.value !== null &&
                                  w.windowYears !== null &&
                                  w.windowYears < w.window &&
                                  windowOrder.slice(0, colIdx).some((prevYrs) => {
                                    const prev = windows.find((item) => item.window === prevYrs)
                                    return (
                                      prev !== undefined &&
                                      prev.value !== null &&
                                      prev.windowYears === w.windowYears
                                    )
                                  })
                                const val = isDuplicateShorterSpan ? null : (w?.value ?? null)
                                const wPct =
                                  val !== null
                                    ? Math.min(
                                        100,
                                        Math.max(6, (Math.abs(val) / maxAbsCagr) * 100),
                                      )
                                    : 0
                                return (
                                  <TableCell
                                    key={yrs}
                                    data-slot="growth-window"
                                    className="align-top py-4 last:pr-0"
                                  >
                                    <span className="sr-only">
                                      {t('stockPage.growth.window', { years: yrs })}
                                    </span>
                                    {val === null ? (
                                      <span
                                        data-slot="no-value"
                                        title={t('honestData.noDataTitle')}
                                        className="font-mono text-sm text-muted-foreground"
                                      >
                                        —
                                      </span>
                                    ) : (
                                      <div className="flex flex-col gap-1.5">
                                        <span
                                          className={
                                            val < 0
                                              ? 'font-mono text-xl font-medium tracking-tight tabular-nums text-[var(--neg)]'
                                              : 'font-mono text-xl font-medium tracking-tight tabular-nums text-foreground'
                                          }
                                        >
                                          {formatPercent(locale, val, 2)}
                                        </span>
                                        <span
                                          aria-hidden="true"
                                          className="block h-[3px] w-full bg-secondary"
                                        >
                                          <span
                                            className={
                                              val < 0
                                                ? 'block h-full bg-[var(--neg)]'
                                                : 'block h-full bg-[var(--brand-accent)]'
                                            }
                                            style={{ width: `${wPct.toFixed(1)}%` }}
                                          />
                                        </span>
                                      </div>
                                    )}
                                    {!isDuplicateShorterSpan &&
                                    w &&
                                    w.windowYears !== null &&
                                    w.windowYears < w.window ? (
                                      <span
                                        data-slot="actual-window"
                                        className="mt-1 block font-mono text-[10px] text-muted-foreground"
                                      >
                                        {t('stockPage.growth.actualWindow', {
                                          years: w.windowYears,
                                        })}
                                      </span>
                                    ) : null}
                                  </TableCell>
                                )
                              })}
                            </TableRow>
                          )
                        })}
                      </TableBody>
                    </Table>
                  </TableContainer>
                </>
              )
            })()}
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
          <div className="flex flex-col gap-5">
            <p className="font-mono text-xs text-muted-foreground">
              {t('stockPage.balanceSheet.period', { year: sectionData.fiscalYear })}
            </p>
            <dl className="grid grid-cols-1 gap-6 border-b border-border pb-5 sm:grid-cols-2">
              <div className="flex flex-col gap-1">
                <dt className="font-mono text-[10px] font-medium uppercase tracking-[0.08em] text-muted-foreground">
                  {t('stockPage.balanceSheet.bookValue')}
                </dt>
                <dd className="font-mono text-2xl font-medium tracking-tight text-foreground">
                  <FigureValue figure={sectionData.bookValue} locale={locale} />
                </dd>
              </div>
              <div className="flex flex-col gap-1">
                <dt className="font-mono text-[10px] font-medium uppercase tracking-[0.08em] text-muted-foreground">
                  {t('stockPage.balanceSheet.bookValuePerShare')}
                </dt>
                <dd className="font-mono text-2xl font-medium tracking-tight text-[var(--stale-foreground)]">
                  <FigureValue figure={sectionData.bookValuePerShare} locale={locale} />
                </dd>
              </div>
            </dl>
            <dl className="grid gap-x-8 gap-y-3 sm:grid-cols-2">
              {BALANCE_SHEET_LINES.map((code) => (
                <div
                  key={code}
                  data-line={code}
                  className="flex items-center justify-between gap-3 border-b border-border pb-2"
                >
                  <dt className="text-xs text-muted-foreground">
                    {t(BALANCE_SHEET_LINE_KEYS[code])}
                  </dt>
                  <dd className="font-mono text-sm font-medium text-foreground">
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
              description={[
                t('stockPage.dividends.neverPaidBody'),
                // TKT-foundation-073: an absence claim states the period it is
                // made over, so "never paid" cannot be read as full history.
                t('stockPage.dividends.neverPaidCoverage'),
                coverageBoundary,
              ]
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
          <div className="flex flex-col gap-5">
            {sectionData.dividends.length > 0 ? (() => {
              const chartItems = [...sectionData.dividends].slice(0, 6).reverse()
              const maxAmt = Math.max(
                0.01,
                ...chartItems.map((d) => Math.abs(d.amountPerShare)),
              )
              const W = 420
              const H = 108
              const slot = W / chartItems.length
              const barW = Math.min(38, Math.max(16, Math.floor(slot * 0.45)))
              return (
                <div
                  data-slot="dividends-dps-chart"
                  className="border-b border-border pb-4"
                >
                  <svg
                    viewBox={`0 0 ${W} ${H}`}
                    width="100%"
                    height={H}
                    aria-hidden="true"
                  >
                    <line
                      x1={0}
                      y1={84}
                      x2={W}
                      y2={84}
                      stroke="var(--border-strong)"
                    />
                    {chartItems.map((d, i) => {
                      const h = Math.max(4, Math.round((Math.abs(d.amountPerShare) / maxAmt) * 66))
                      const x = i * slot + (slot - barW) / 2
                      const isLatest = i === chartItems.length - 1
                      return (
                        <g key={`${d.exDate}-${d.payDate}`}>
                          <rect
                            x={x}
                            y={84 - h}
                            width={barW}
                            height={h}
                            fill={isLatest ? 'var(--brand-accent)' : 'var(--border-strong)'}
                          />
                          <text
                            x={x + barW / 2}
                            y={78 - h}
                            textAnchor="middle"
                            fill="var(--foreground)"
                            fontFamily="var(--font-mono)"
                            fontSize="10"
                          >
                            {formatNumber(locale, d.amountPerShare, {
                              maximumFractionDigits: 2,
                            })}
                          </text>
                          <text
                            x={x + barW / 2}
                            y={101}
                            textAnchor="middle"
                            fill="var(--muted-foreground)"
                            fontFamily="var(--font-mono)"
                            fontSize="10"
                          >
                            {d.exDate.slice(0, 4)}
                          </text>
                        </g>
                      )
                    })}
                  </svg>
                </div>
              )
            })() : null}
            <TableContainer className="rounded-none border-0">
              <Table>
                <TableHeader className="bg-transparent">
                  <TableRow className="hover:bg-transparent">
                    <TableHead className="pl-0">{t('stockPage.dividends.columns.exDate')}</TableHead>
                    <TableHead>{t('stockPage.dividends.columns.payDate')}</TableHead>
                    <TableHead className="pr-0 text-right">{t('stockPage.dividends.columns.amount')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {sectionData.dividends.map((dividend) => (
                    <TableRow key={`${dividend.exDate}-${dividend.payDate}`}>
                      <TableCell className="pl-0 font-mono">{dividend.exDate}</TableCell>
                      <TableCell className="font-mono">{dividend.payDate}</TableCell>
                      <TableCell className="pr-0 text-right font-mono tabular-nums">
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
