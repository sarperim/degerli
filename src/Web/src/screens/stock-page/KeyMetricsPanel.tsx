import { Fragment } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'

import {
  AdjustedNotice,
  NotMeaningful,
  RestatedMarker,
} from '@/components/honest-data'
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import type {
  CapitalAction,
  CapitalTracker,
  CompanyMedianBlock,
  KeyMetricEntry,
} from '@/lib/api/contracts'
import { formatAsOfDate, formatDecimal, formatNumber, formatPercent } from '@/lib/format'
import { SectionBody } from '@/screens/stock-page/SectionBody'
import { SectionMarkers } from '@/screens/stock-page/SectionMarkers'
import { fetchStockKeyMetrics, stockPageQueryKeys } from '@/screens/stock-page/api'
import {
  corporateActionKindKey,
  formatKeyMetric,
  hasAdjustedMetric,
  hasRegimeBreak,
  monetaryGainAlert,
  visibleKeyMetricGroups,
  type KeyMetricFormat,
} from '@/screens/stock-page/keyMetricsView'

/**
 * SCR-005 — the KEY VALUE METRICS & MOAT INDICATORS panel and the CAPITAL &
 * DILUTION TRACKER (TKT-foundation-097), rendered from TKT-foundation-096's
 * `GET /stocks/{symbol}/key-metrics` payload.
 *
 * **The page computes nothing** (BR-RES-004): every figure is a served fact —
 * the current canonical value, the 5Y company median (with its depth and
 * observation disclosure), the sector median with its peer/exclusion counts,
 * and the capital tracker's stored block. A `null` value is an explicit honest
 * gap (UXR-RES-022/025): `—`, never a zero.
 *
 * **Basis honesty.** The served `roe` and `eps_cagr_*` are group/consolidated
 * basis; their labels say so and never claim the spec's "Parent" basis (DR-3
 * routing from the 096 review).
 */

/** The honest dash for a served-null value (never a zero). */
function Dash() {
  const { t } = useTranslation()
  return (
    <span data-slot="no-value" title={t('honestData.noDataTitle')}>
      —
    </span>
  )
}

/** One served current value (with its restatement marker when served restated). */
function CurrentValue({
  entry,
  format,
  locale,
}: {
  entry: KeyMetricEntry | undefined
  format: KeyMetricFormat
  locale: string
}) {
  if (entry === undefined || entry.value === null) {
    return <Dash />
  }
  return (
    <span className="tabular-nums">
      {formatKeyMetric(locale, format, entry.value)}
      {entry.isRestated ? (
        <>
          {' '}
          <RestatedMarker />
        </>
      ) : null}
    </span>
  )
}

/**
 * The served 5Y company median and its honesty disclosure: the nominal window,
 * the observations behind it and the depth the stored history actually reaches.
 * A withheld median (below the served floor) is a dash — never a fabricated
 * statistic wearing the 5Y label (FR-MDF-014).
 */
function CompanyMedian({
  median,
  format,
  locale,
}: {
  median: CompanyMedianBlock | undefined
  format: KeyMetricFormat
  locale: string
}) {
  const { t } = useTranslation()
  if (median === undefined) {
    return <Dash />
  }
  return (
    <span className="flex flex-col gap-0.5">
      <span className="tabular-nums">
        {median.value === null ? (
          <Dash />
        ) : (
          formatKeyMetric(locale, format, median.value)
        )}
      </span>
      <span
        data-slot="company-median-disclosure"
        className="text-xs text-muted-foreground"
      >
        {t('stockPage.keyMetrics.medianDisclosure', {
          window: median.windowYears,
          observations: median.observations,
          depth: formatDecimal(locale, median.depthYears, 2),
          minimum: median.minimumObservations,
        })}
      </span>
    </span>
  )
}

/** The served sector median with its peer count and exclusion disclosure (UXR-RES-024/025). */
function SectorMedian({
  entry,
  format,
  locale,
}: {
  entry: KeyMetricEntry | undefined
  format: KeyMetricFormat
  locale: string
}) {
  const { t } = useTranslation()
  if (entry === undefined) {
    return <Dash />
  }
  return (
    <span className="flex flex-col gap-0.5">
      <span data-slot="sector-median" className="tabular-nums">
        {entry.sectorMedian === null ? (
          <Dash />
        ) : (
          formatKeyMetric(locale, format, entry.sectorMedian)
        )}
      </span>
      {entry.peerCount !== null && entry.peerCount > 0 ? (
        <span data-slot="peer-count" className="text-xs text-muted-foreground">
          {t('stockPage.keyMetrics.peerCount', { count: entry.peerCount })}
        </span>
      ) : null}
      {entry.excludedCount !== null && entry.excludedCount > 0 ? (
        <span data-slot="exclusion-count" className="text-xs text-muted-foreground">
          {t('stockPage.keyMetrics.exclusionCount', { count: entry.excludedCount })}
        </span>
      ) : null}
    </span>
  )
}

/** A distinguishable regime-break marker (UXR-RES-026/027), never the restatement `*`. */
function RegimeBreakMarker() {
  const { t } = useTranslation()
  return (
    <span
      data-slot="regime-break-marker"
      className="font-semibold text-foreground"
      title={t('stockPage.keyMetrics.regimeBreakMarker')}
    >
      <span aria-hidden="true">†</span>
      <span className="sr-only"> ({t('stockPage.keyMetrics.regimeBreakMarker')})</span>
    </span>
  )
}

/** The capital & dilution tracker block (the user's spec row set, served). */
function CapitalTrackerBlock({
  tracker,
  locale,
}: {
  tracker: CapitalTracker
  locale: string
}) {
  const { t } = useTranslation()
  const money = (value: number) =>
    `${formatNumber(locale, value, { maximumFractionDigits: 0 })} ₺`
  const count = (value: number) =>
    formatNumber(locale, value, { maximumFractionDigits: 0 })

  const actionLabel = (action: CapitalAction): string => {
    const key = corporateActionKindKey(action.kind)
    return key === null ? action.type : t(key)
  }

  return (
    <section
      data-testid="capital-tracker"
      aria-label={t('stockPage.capitalTracker.title')}
      className="flex flex-col gap-3"
    >
      <h3 className="text-sm font-semibold text-foreground">
        {t('stockPage.capitalTracker.title')}
      </h3>
      <dl className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        <div data-row="paidInCapital" className="flex flex-col gap-0.5 rounded-md border p-2">
          <dt className="text-xs text-muted-foreground">
            {t('stockPage.capitalTracker.rows.paidInCapital')}
          </dt>
          <dd className="text-sm font-medium text-foreground">
            {tracker.paidInCapital === null ? (
              <Dash />
            ) : (
              <span className="tabular-nums">{money(tracker.paidInCapital)}</span>
            )}
            {tracker.paidInCapitalAsOf === null ? null : (
              <span data-slot="as-of" className="text-xs text-muted-foreground">
                {' '}
                {formatAsOfDate(locale, tracker.paidInCapitalAsOf)}
              </span>
            )}
          </dd>
        </div>

        <div data-row="sharesOutstanding" className="flex flex-col gap-0.5 rounded-md border p-2">
          <dt className="text-xs text-muted-foreground">
            {t('stockPage.capitalTracker.rows.sharesOutstanding')}
          </dt>
          <dd className="text-sm font-medium text-foreground">
            {tracker.sharesOutstanding === null ? (
              <Dash />
            ) : (
              <span className="tabular-nums">{count(tracker.sharesOutstanding)}</span>
            )}
            {tracker.sharesOutstandingAsOf === null ? null : (
              <span data-slot="as-of" className="text-xs text-muted-foreground">
                {' '}
                {formatAsOfDate(locale, tracker.sharesOutstandingAsOf)}
              </span>
            )}
            {tracker.sharesOutstandingBasis === 'price-derived' ? (
              <span data-slot="share-basis" className="text-xs text-muted-foreground">
                {' '}
                · {t('stockPage.capitalTracker.priceDerivedBasis')}
              </span>
            ) : null}
          </dd>
        </div>

        <div data-row="shareCountChangeYoy" className="flex flex-col gap-0.5 rounded-md border p-2">
          <dt className="text-xs text-muted-foreground">
            {t('stockPage.capitalTracker.rows.shareCountChangeYoy')}
          </dt>
          <dd className="text-sm font-medium text-foreground">
            {tracker.shareCountChangeYoy.value === null ? <Dash /> : null}
            {tracker.shareCountChangeYoy.value === null ? (
              <span data-slot="not-meaningful">
                {' '}
                <NotMeaningful />
              </span>
            ) : (
              <span className="tabular-nums">
                {formatPercent(locale, tracker.shareCountChangeYoy.value, 2)}
              </span>
            )}
            {tracker.shareCountChangeYoy.state === 'no-count-history' ? (
              <span data-slot="state-note" className="text-xs text-muted-foreground">
                {' '}
                · {t('stockPage.capitalTracker.noCountHistory')}
              </span>
            ) : null}
          </dd>
        </div>

        <div data-row="payoutRatio" className="flex flex-col gap-0.5 rounded-md border p-2">
          <dt className="text-xs text-muted-foreground">
            {t('stockPage.capitalTracker.rows.payoutRatio')}
          </dt>
          <dd className="text-sm font-medium text-foreground">
            {tracker.payoutRatio.value === null ? (
              <Dash />
            ) : (
              <span className="tabular-nums">
                {formatPercent(locale, tracker.payoutRatio.value, 2)}
              </span>
            )}
            {tracker.payoutRatio.asOf ? (
              <span data-slot="as-of" className="text-xs text-muted-foreground">
                {' '}
                {formatAsOfDate(locale, tracker.payoutRatio.asOf)}
              </span>
            ) : null}
          </dd>
        </div>
      </dl>

      <div data-slot="corporate-actions" className="flex flex-col gap-2">
        <p className="text-xs font-medium text-muted-foreground">
          {t('stockPage.capitalTracker.rows.bedelliBedelsizHistory')}
        </p>
        {tracker.corporateActions.length === 0 ? (
          <p data-slot="no-corporate-actions" className="text-sm text-muted-foreground">
            {t('stockPage.capitalTracker.noActions')}
          </p>
        ) : (
          <ul className="flex flex-col gap-1">
            {tracker.corporateActions.map((action) => (
              <li
                key={`${action.date}-${action.type}`}
                data-action-date={action.date}
                className="flex flex-wrap items-center gap-x-2 text-sm text-foreground"
              >
                <span data-slot="action-date">
                  {formatAsOfDate(locale, action.date)}
                </span>
                <span>{actionLabel(action)}</span>
                <span data-slot="action-percent" className="tabular-nums">
                  {action.percent === null ? (
                    <Dash />
                  ) : (
                    `${formatDecimal(locale, action.percent, 2)}%`
                  )}
                </span>
              </li>
            ))}
          </ul>
        )}
        {tracker.corporateActionsBasis === 'corporate-action-adjusted' &&
        tracker.corporateActions.length > 0 ? (
          <AdjustedNotice>
            {t('stockPage.capitalTracker.adjustedDisclaimer')}
          </AdjustedNotice>
        ) : null}
      </div>
    </section>
  )
}

/**
 * The two blocks rendered inside the Financials section, after the statements.
 * The panel owns its own query, so a failing `/key-metrics` call never blanks the
 * statements (and vice versa) — the section-independence rule at panel granularity.
 */
export function KeyMetricsPanel({ symbol }: { symbol: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.keyMetrics.title')
  const query = useQuery({
    queryKey: stockPageQueryKeys.keyMetrics(symbol),
    queryFn: ({ signal }) => fetchStockKeyMetrics(symbol, signal),
  })

  const data = query.data
  const financialProfile = data?.financialSector !== null && data?.financialSector !== undefined
  const groups = data ? visibleKeyMetricGroups(data.metrics, financialProfile) : []
  const regimeBreak = data ? hasRegimeBreak(data.metrics) : false
  const adjusted = data ? hasAdjustedMetric(data.metrics) : false
  const alert = data ? monetaryGainAlert(data.metrics) : null

  return (
    <SectionBody
      label={label}
      isPending={query.isPending}
      isError={query.isError}
      onRetry={() => void query.refetch()}
    >
      {data ? (
        <div className="flex flex-col gap-4">
          <section
            data-testid="key-metrics-panel"
            aria-label={label}
            className="flex flex-col gap-3"
          >
            <h3 className="text-sm font-semibold text-foreground">{label}</h3>

            {financialProfile ? (
              <p
                data-testid="key-metrics-sector-notice"
                className="max-w-prose text-sm text-muted-foreground"
              >
                {t('stockPage.keyMetrics.financialNotice', {
                  profile: data.financialSector?.profile ?? '',
                })}
              </p>
            ) : null}

            <TableContainer>
              <Table>
                <TableCaption>
                  {t('stockPage.keyMetrics.tableCaption', {
                    date: formatAsOfDate(locale, data.asOf),
                  })}
                </TableCaption>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('stockPage.keyMetrics.columns.metric')}</TableHead>
                    <TableHead className="text-right">
                      {t('stockPage.keyMetrics.columns.current')}
                    </TableHead>
                    <TableHead className="text-right">
                      {t('stockPage.keyMetrics.columns.companyMedian')}
                    </TableHead>
                    <TableHead className="text-right">
                      {t('stockPage.keyMetrics.columns.sectorMedian')}
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {groups.map((group) => (
                    <Fragment key={group.key}>
                      <TableRow data-group={group.key}>
                        <TableHead
                          scope="rowgroup"
                          colSpan={4}
                          className="text-xs uppercase tracking-wide text-muted-foreground"
                        >
                          {t(group.labelKey)}
                        </TableHead>
                      </TableRow>
                      {group.rows.map((metric) => {
                        const entry = data.metrics[metric.code]
                        return (
                          <TableRow key={metric.code} data-metric={metric.code}>
                            <TableHead scope="row" className="font-normal text-foreground">
                              {t(metric.labelKey)}
                              {entry?.regimeBreak === true ? (
                                <>
                                  {' '}
                                  <RegimeBreakMarker />
                                </>
                              ) : null}
                            </TableHead>
                            <TableCell className="text-right">
                              <CurrentValue
                                entry={entry}
                                format={metric.format}
                                locale={locale}
                              />
                            </TableCell>
                            <TableCell className="text-right">
                              <CompanyMedian
                                median={entry?.companyMedian5y}
                                format={metric.format}
                                locale={locale}
                              />
                            </TableCell>
                            <TableCell className="text-right">
                              <SectorMedian
                                entry={entry}
                                format={metric.format}
                                locale={locale}
                              />
                            </TableCell>
                          </TableRow>
                        )
                      })}
                    </Fragment>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>

            {alert ? (
              <div
                data-testid="tms29-alert"
                role="note"
                className="flex flex-col gap-0.5 rounded-md border border-adjusted-foreground/20 bg-adjusted/60 p-2 text-xs text-adjusted-foreground"
              >
                <span className="font-medium">
                  {t('stockPage.keyMetrics.tms29Alert.title')}
                </span>
                <span>
                  {t('stockPage.keyMetrics.tms29Alert.body', {
                    value: formatPercent(locale, alert.value ?? 0, 2),
                  })}
                </span>
              </div>
            ) : null}

            {regimeBreak ? (
              <p data-slot="regime-break-notice" className="text-xs text-muted-foreground">
                {t('stockPage.keyMetrics.regimeBreakNotice')}
              </p>
            ) : null}
            {adjusted ? <AdjustedNotice /> : null}
          </section>

          <CapitalTrackerBlock tracker={data.capitalTracker} locale={locale} />

          <SectionMarkers asOf={data.asOf} stale={data.stale} />
        </div>
      ) : null}
    </SectionBody>
  )
}
