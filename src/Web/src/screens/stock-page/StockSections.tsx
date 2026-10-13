import type { ReactNode } from 'react'
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
  isMissingData,
  stockPageQueryKeys,
} from '@/screens/stock-page/api'

/** Shared section chrome: heading + labelled body. */
function SectionShell({
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
}: {
  figure: FinancialFigure
  locale: string
  percent?: boolean
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

/**
 * SCR-005 §2 — Valuation placeholder.
 *
 * The Valuation section UI (current/historical multiples, the vs.-sector strip)
 * is delivered by TKT-res-009; this screen only reserves the navigable section so
 * the seven-section contract holds. The adjusted-data disclaimer lives there.
 */
export function ValuationSectionPlaceholder() {
  const { t } = useTranslation()
  return (
    <SectionShell section="valuation" titleKey="stockPage.sections.valuation">
      <p className="text-sm text-muted-foreground">
        {t('stockPage.valuationPlaceholder')}
      </p>
    </SectionShell>
  )
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
                        {t('stockPage.financials.period', {
                          year: period.fiscalYear,
                        })}
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
  sharesOutstanding: 'stockPage.balanceSheet.lines.sharesOutstanding',
}

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
                <div key={code} className="flex items-center justify-between gap-3">
                  <dt className="text-xs text-muted-foreground">
                    {t(BALANCE_SHEET_LINE_KEYS[code])}
                  </dt>
                  <dd className="text-sm font-medium text-foreground">
                    <FigureValue
                      figure={
                        sectionData.lines[code] ?? { value: null, restated: false }
                      }
                      locale={locale}
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

/** SCR-005 §7 — Dividends (FR-RES-012), incl. the explicit never-paid state. */
export function DividendsSection({ symbol }: { symbol: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.sections.dividends')
  const query = useQuery({
    queryKey: stockPageQueryKeys.dividends(symbol),
    queryFn: ({ signal }) => fetchStockDividends(symbol, signal),
  })

  const data = query.data
  const missingData = data !== undefined && isMissingData(data) ? data : null
  const sectionData = data !== undefined && !isMissingData(data) ? data : null

  const coverageBoundary = missingData?.coverage?.availableFrom
    ? t('honestData.coverageBoundary', {
        date: formatAsOfDate(locale, missingData.coverage.availableFrom),
      })
    : undefined

  return (
    <SectionShell section="dividends" titleKey="stockPage.sections.dividends">
      <SectionBody
        label={label}
        isPending={query.isPending}
        isError={query.isError}
        onRetry={() => void query.refetch()}
      >
        {missingData ? (
          <EmptyState
            data-testid="dividends-never-paid"
            data-state={missingData.state}
            title={t('stockPage.dividends.neverPaidTitle')}
            description={[t('stockPage.dividends.neverPaidBody'), coverageBoundary]
              .filter(Boolean)
              .join(' ')}
          />
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
