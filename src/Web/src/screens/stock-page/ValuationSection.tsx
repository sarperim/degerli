import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Line, LineChart, Tooltip, XAxis, YAxis } from 'recharts'
import type { TooltipContentProps } from 'recharts'

import { NotMeaningful } from '@/components/honest-data'
import { EmptyState } from '@/components/ui/empty-state'
import type {
  SectorStripEntry,
  StockValuationResponse,
  ValuationHistoricalSeries,
} from '@/lib/api/contracts'
import { formatAsOfDate, formatNumber, formatPercent } from '@/lib/format'
import { SectionBody } from '@/screens/stock-page/SectionBody'
import { SectionMarkers } from '@/screens/stock-page/SectionMarkers'
import { SectionShell } from '@/screens/stock-page/StockSections'
import { fetchStockValuation, stockPageQueryKeys } from '@/screens/stock-page/api'
import {
  isPercentValuationMetric,
  valuationHistoryPoints,
  VALUATION_METRIC_ORDER,
  type ValuationHistoryPoint,
  type ValuationMetricCode,
} from '@/screens/stock-page/valuation'

/**
 * SCR-005 §2 — Valuation (FR-RES-006/007/013, UXR-RES-012/013/024/025).
 *
 * The section shows the current canonical multiples, the historical price series
 * on its honest basis (adjusted when the served rows carry an adjusted close —
 * BR-MDF-010) with the adjusted-data disclaimer, and the vs.-sector strip: the
 * stock's five valuation-family multiples next to their sector medians, each with
 * its peer count and — for P/E — the loss-maker exclusion disclosure. A metric
 * with no meaningful own value shows the explicit not-meaningful state; a metric
 * with no available sector median degrades to an honest no-data state. Copy is
 * read from the catalogs; the chart is a Recharts line (architecture §5).
 */

/** Full label keys for the five strip metrics (literals, so i18n parity sees them). */
const VALUATION_METRIC_LABEL_KEYS: Record<ValuationMetricCode, string> = {
  pe: 'stockPage.valuation.metrics.pe',
  pb: 'stockPage.valuation.metrics.pb',
  ev_ebitda: 'stockPage.valuation.metrics.ev_ebitda',
  ev_fcf: 'stockPage.valuation.metrics.ev_fcf',
  fcf_yield: 'stockPage.valuation.metrics.fcf_yield',
}

/** The chart's fixed drawing area; the wrapper scrolls at narrow widths (UXR-G-025). */
const CHART_WIDTH = 640
const CHART_HEIGHT = 240

/** Format a valuation metric in its own unit (percent for FCF yield, ratio otherwise). */
function formatMetric(locale: string, code: string, value: number): string {
  return isPercentValuationMetric(code)
    ? formatPercent(locale, value, 2)
    : formatNumber(locale, value)
}

/**
 * The history chart's hover tooltip (TKT-res-012): the hovered point's date and
 * plotted close, formatted with the shared locale helpers (`formatAsOfDate` /
 * `formatNumber`). Recharts injects `active`/`payload`; an entry's `payload` is
 * the plotted `ValuationHistoryPoint` — the same basis-consistent `close` the line
 * draws — so the tooltip adds no basis claim beyond the figure caption
 * (UXR-RES-020). Exported so its output is testable without hover simulation.
 */
export function ValuationTooltipContent({
  active,
  payload,
}: Partial<TooltipContentProps<number, string>>) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'

  if (active !== true || payload === undefined || payload.length === 0) return null
  const point = payload[0].payload as ValuationHistoryPoint | undefined
  if (point === undefined) return null

  return (
    <div
      data-testid="valuation-tooltip"
      className="flex flex-col gap-0.5 rounded-md border bg-background px-2 py-1 text-xs shadow-md"
    >
      <span data-slot="tooltip-date" className="text-muted-foreground">
        {formatAsOfDate(locale, point.date)}
      </span>
      <span
        data-slot="tooltip-close"
        className="font-medium tabular-nums text-foreground"
      >
        {t('stockPage.valuation.history.close')}: {formatNumber(locale, point.close)}
      </span>
    </div>
  )
}

/** The historical price series as an SVG line on its honest basis (FR-RES-006/013). */
export function ValuationHistoryChart({
  historical,
}: {
  historical: ValuationHistoricalSeries
}) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const points = valuationHistoryPoints(historical)
  const basis = historical.adjusted ? 'adjusted' : 'raw'
  const caption = historical.adjusted
    ? t('stockPage.valuation.history.captionAdjusted', { count: points.length })
    : t('stockPage.valuation.history.captionRaw', { count: points.length })

  return (
    <figure
      data-testid="valuation-history-chart"
      data-slot="valuation-history"
      data-basis={basis}
      className="flex flex-col gap-2"
    >
      <figcaption className="text-xs font-medium text-muted-foreground">
        {t('stockPage.valuation.history.title')}
        {' — '}
        {caption}
      </figcaption>
      {points.length > 0 ? (
        <div className="overflow-x-auto" role="img" aria-label={caption}>
          <LineChart
            width={CHART_WIDTH}
            height={CHART_HEIGHT}
            data={points}
            accessibilityLayer
            margin={{ top: 8, right: 16, bottom: 8, left: 8 }}
          >
            <XAxis
              dataKey="date"
              tickFormatter={(value: string) => formatAsOfDate(locale, value)}
              tick={{ fontSize: 11 }}
            />
            <YAxis width={56} tick={{ fontSize: 11 }} />
            <Line
              type="monotone"
              dataKey="close"
              dot={false}
              isAnimationActive={false}
              strokeWidth={2}
            />
            <Tooltip
              content={<ValuationTooltipContent />}
              isAnimationActive={false}
            />
          </LineChart>
        </div>
      ) : null}
    </figure>
  )
}

/** One strip cell: own value, sector median, peer count (UXR-RES-013/024/025). */
function StripMetric({
  code,
  entry,
  locale,
}: {
  code: ValuationMetricCode
  entry: SectorStripEntry
  locale: string
}) {
  const { t } = useTranslation()
  const label = t(VALUATION_METRIC_LABEL_KEYS[code])
  return (
    <div
      data-metric={code}
      className="flex flex-col gap-1 rounded-md border p-2"
    >
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="flex flex-col gap-0.5">
        <span
          data-slot="stock-value"
          className="text-sm font-medium tabular-nums text-foreground"
        >
          {/*
            "Anlamsız" is a verdict about the metric (e.g. P/E on negative
            earnings), so it is rendered only when the server says so. An own
            value the server did not compute is missing data — a dash, never a
            fabricated verdict (TKT-foundation-055(c), the 052 doctrine).
          */}
          {entry.notMeaningful ? (
            <NotMeaningful />
          ) : entry.value === null ? (
            <span data-slot="no-value" title={t('honestData.noDataTitle')}>
              —
            </span>
          ) : (
            formatMetric(locale, code, entry.value)
          )}
        </span>
        <span data-slot="sector-median" className="text-xs text-muted-foreground">
          {entry.sectorMedian === null
            ? t('stockPage.valuation.strip.noMedian')
            : `${t('stockPage.valuation.strip.sectorMedian')}: ${formatMetric(
                locale,
                code,
                entry.sectorMedian,
              )}`}
        </span>
        {/* A median over zero peers is not a comparison basis — never "0 emsal". */}
        {entry.peerCount !== null && entry.peerCount > 0 ? (
          <span data-slot="peer-count" className="text-xs text-muted-foreground">
            {t('stockPage.valuation.strip.peerCount', { count: entry.peerCount })}
          </span>
        ) : null}
      </dd>
    </div>
  )
}

/**
 * The vs.-sector strip: the five valuation-family multiples, each next to its
 * sector median with the peer count behind it and the P/E loss-maker exclusion
 * disclosed inline (UXR-RES-013/024/025). When no metric has a served sector
 * median — the zero-shares world until 058's share counts land — the block
 * states one honest no-data empty instead of presenting absence as per-row
 * "not meaningful" verdicts (TKT-foundation-055(c)).
 */
export function VsSectorStrip({ strip }: { strip: Record<string, SectorStripEntry> }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const peExcluded = strip.pe?.excludedCount ?? 0

  /*
   * A comparison exists only where a sector median is served — the peers behind
   * it are what make the row a comparison. In the zero-shares world (TKT-
   * foundation-033 → 058) no peer has a computable metric, so every entry comes
   * back without a median: rendering the rows anyway produced the user-reported
   * wall of per-row "Anlamsız" and "0 emsal" verdicts over pure absence. The
   * block then states the honest empty once, and populates by itself as the
   * share counts land (TKT-foundation-055(c), the 052 doctrine).
   */
  const hasComparablePeers = VALUATION_METRIC_ORDER.some((code) => {
    const entry = strip[code]
    return entry !== undefined && entry.sectorMedian !== null
  })

  return (
    <div data-slot="vs-sector-strip" className="flex flex-col gap-2">
      <h3 className="text-xs font-medium text-muted-foreground">
        {t('stockPage.valuation.strip.title')}
      </h3>
      {hasComparablePeers ? (
        <>
          <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {VALUATION_METRIC_ORDER.map((code) => {
              const entry = strip[code]
              if (entry === undefined) return null
              return <StripMetric key={code} code={code} entry={entry} locale={locale} />
            })}
          </dl>
          {peExcluded > 0 ? (
            <p data-slot="pe-exclusion" className="text-xs text-muted-foreground">
              {t('stockPage.valuation.strip.exclusionDisclosure', { count: peExcluded })}
            </p>
          ) : null}
        </>
      ) : (
        <EmptyState
          data-testid="vs-sector-no-data"
          title={t('stockPage.valuation.strip.noSectorDataTitle')}
        />
      )}
    </div>
  )
}

/** SCR-005 §2 — the Valuation section (FR-RES-006/007/013). */
export function ValuationSection({ symbol }: { symbol: string }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const label = t('stockPage.sections.valuation')
  const query = useQuery({
    queryKey: stockPageQueryKeys.valuation(symbol),
    queryFn: ({ signal }) => fetchStockValuation(symbol, signal),
  })

  const data: StockValuationResponse | undefined = query.data

  return (
    <SectionShell section="valuation" titleKey="stockPage.sections.valuation">
      <SectionBody
        label={label}
        isPending={query.isPending}
        isError={query.isError}
        onRetry={() => void query.refetch()}
      >
        {data ? (
          <div className="flex flex-col gap-4">
            {/*
              The stock's own canonical multiples stay machine-addressable
              (`data-metric` / `data-slot="stock-value"`) regardless of the
              vs.-sector strip: the strip states an honest empty when no peer
              median is served, but the canonical own P/E and friends are still
              the figures TC-RES-028 / TC-XC-008 read off this region.
            */}
            <div className="flex flex-col gap-2">
              <h3 className="text-xs font-medium text-muted-foreground">
                {t('stockPage.valuation.current.title')}
              </h3>
              <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {VALUATION_METRIC_ORDER.map((code) => {
                  const value = data.current[code] ?? null
                  return (
                    <div
                      key={code}
                      data-metric={code}
                      className="flex items-center justify-between gap-3"
                    >
                      <dt className="text-xs text-muted-foreground">
                        {t(VALUATION_METRIC_LABEL_KEYS[code])}
                      </dt>
                      <dd
                        data-slot="stock-value"
                        className="text-sm font-medium tabular-nums text-foreground"
                      >
                        {value === null ? (
                          <NotMeaningful />
                        ) : (
                          formatMetric(locale, code, value)
                        )}
                      </dd>
                    </div>
                  )
                })}
              </dl>
            </div>

            <ValuationHistoryChart historical={data.historical} />

            <VsSectorStrip strip={data.strip} />

            <SectionMarkers
              asOf={data.asOf}
              stale={data.stale}
              adjusted={data.historical.adjusted}
            />
          </div>
        ) : null}
      </SectionBody>
    </SectionShell>
  )
}
