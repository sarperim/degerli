import { useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery, type UseQueryResult } from '@tanstack/react-query'
import { Link } from 'react-router'
import { ArrowDown, ArrowUp } from 'lucide-react'

import { AsOfDate, NoDataState, StaleBadge } from '@/components/honest-data'
import { Button, buttonVariants } from '@/components/ui/button'
import { SkeletonText } from '@/components/ui/skeleton'
import { MacroStrip } from '@/routes/MacroStrip'
import {
  HeroInteractivePlate,
  useHeroBackdropCanvas,
} from '@/screens/market-overview/HeroInteractivePlate'
import { PriceStateHeader } from '@/screens/price-state/PriceStateHeader'
import {
  marketQueryKeys,
  fetchMarketOverview,
  type MarketBlockId,
  type SectorPeriod,
} from '@/lib/api/market'
import type { IndexLevel, MarketOverviewResponse, MoverEntry } from '@/lib/api/contracts'
import { formatNumber, formatRatioPercent, formatSignedPercent } from '@/lib/format'
import { cn } from '@/lib/utils'

/**
 * SCR-001 — Market Overview (Home) equity blocks, navigation and disclaimer
 * (UX `market-overview.md`; FR-MOV-001..006, 011..013, 020, 021; UC-MOV-001/004).
 *
 * The daily "discover" entry point: editorial hero + 4-column KPI ribbon
 * (indices, breadth, volume), Band 01 pairing market valuation with the macro
 * strip (TKT-mov-006, separate failure domain), Band 02 diverging sector
 * return bars (entries link to the Stock List pre-filtered), and Band 03 open
 * borderless movers ledgers (entries link to the stock page). The
 * informational-only disclaimer is rendered by the shell footer for research
 * routes (UXR-G-016).
 *
 * Each block is its own query instance against the snapshot endpoint (M-5,
 * architecture §8.1), so a block loads, fails and retries independently — a single
 * block failure never blanks the dashboard (SCR-001 §5, TC-MOV-025). Over the
 * honest-data envelope every block shows its data-as-of date and stale marker
 * (UXR-MOV-010). Change direction is conveyed by an explicit sign plus an arrow,
 * never by colour alone (SCR-001 §9).
 */

type BlockQuery = UseQueryResult<MarketOverviewResponse>

/** One independent per-block query against the shared snapshot endpoint (M-5). */
function useMarketBlock(block: MarketBlockId, period: SectorPeriod = 'daily'): BlockQuery {
  return useQuery({
    queryKey: marketQueryKeys.overview(block, period),
    queryFn: ({ signal }) => fetchMarketOverview(block, period, signal),
  })
}

function BlockSkeleton() {
  const { t } = useTranslation()
  return (
    <div role="status" aria-label={t('common.loading')}>
      <SkeletonText lines={4} />
    </div>
  )
}

function BlockError({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation()
  return (
    <div
      role="alert"
      className="flex flex-col items-start gap-2 rounded-md border border-dashed px-4 py-5"
    >
      <p className="text-body font-medium text-foreground">
        {t('marketOverview.errorTitle')}
      </p>
      <p className="text-sm text-muted-foreground">{t('marketOverview.errorBody')}</p>
      <Button variant="outline" size="sm" onClick={onRetry}>
        {t('common.retry')}
      </Button>
    </div>
  )
}

function Unavailable() {
  return <NoDataState state="unavailable" />
}

/** Query state switcher shared across borderless bands and KPI cells. */
function BlockContent({
  query,
  children,
}: {
  query: BlockQuery
  children: (data: MarketOverviewResponse) => ReactNode
}) {
  if (query.isPending) return <BlockSkeleton />
  if (query.isError) return <BlockError onRetry={() => void query.refetch()} />
  if (query.isSuccess) return <>{children(query.data)}</>
  return null
}

/** A daily % change with its sign and an arrow — never colour-only (SCR-001 §9). */
function ChangeValue({ value, className }: { value: number; className?: string }) {
  const { i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const Icon = value < 0 ? ArrowDown : ArrowUp
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 tabular-nums',
        value > 0 && 'text-[var(--pos)]',
        value < 0 && 'text-[var(--neg)]',
        className,
      )}
    >
      <Icon aria-hidden="true" className="size-3.5 shrink-0" />
      {formatSignedPercent(locale, value)}
    </span>
  )
}

function IndexCell({
  id,
  label,
  index,
  lead = false,
}: {
  id: string
  label: string
  index: IndexLevel
  lead?: boolean
}) {
  const { i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const lang = i18n.resolvedLanguage ?? 'tr'
  return (
    <div
      data-testid={`index-${id}`}
      className="flex flex-col justify-between gap-2 p-4 sm:p-5"
    >
      <dt
        lang={lang}
        className="font-mono text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground"
      >
        {label}
      </dt>
      <dd className="flex flex-col gap-1">
        <span
          className={cn(
            'font-mono font-medium leading-none tracking-tight tabular-nums text-foreground',
            lead ? 'text-[26px] sm:text-[28px]' : 'text-[22px]',
          )}
        >
          {formatNumber(locale, index.level, {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
          })}
        </span>
        <ChangeValue value={index.changePct} className="font-mono text-xs font-medium" />
      </dd>
    </div>
  )
}

function IndexLevelsBlock({ query }: { query: BlockQuery }) {
  const { t } = useTranslation()
  return (
    <section
      data-testid="market-block-indices"
      aria-label={t('marketOverview.blocks.indices.title')}
      className="sm:col-span-2 lg:col-span-2 flex flex-col justify-center border-b lg:border-b-0 lg:border-r border-border"
    >
      <h2 className="sr-only">{t('marketOverview.blocks.indices.title')}</h2>
      {query.isPending ? (
        <div className="p-4 sm:p-5">
          <BlockSkeleton />
        </div>
      ) : null}
      {query.isError ? (
        <div className="p-4 sm:p-5">
          <BlockError onRetry={() => void query.refetch()} />
        </div>
      ) : null}
      {query.isSuccess ? (
        query.data.indices ? (
          <dl className="grid h-full grid-cols-1 sm:grid-cols-2 divide-y sm:divide-y-0 sm:divide-x divide-border">
            <IndexCell
              id="xu100"
              label={t('marketOverview.blocks.indices.xu100')}
              index={query.data.indices.xu100}
              lead
            />
            <IndexCell
              id="xu30"
              label={t('marketOverview.blocks.indices.xu30')}
              index={query.data.indices.xu30}
            />
          </dl>
        ) : (
          <div className="p-4 sm:p-5">
            <Unavailable />
          </div>
        )
      ) : null}
    </section>
  )
}

/**
 * The sector-performance period selector (FR-MOV-017 Could, UXR-MOV-012):
 * a session-scoped choice of daily / 1W / 1M / YTD, never persisted
 * server-side. Each non-daily selection re-queries the shared snapshot endpoint
 * with its `period` token (the daily view omits it). The control is a labelled
 * button group whose active option carries `aria-pressed` (never colour-only).
 */
function SectorPeriodSelector({
  period,
  onPeriodChange,
}: {
  period: SectorPeriod
  onPeriodChange: (next: SectorPeriod) => void
}) {
  const { t } = useTranslation()
  const options: { value: SectorPeriod; labelKey: string }[] = [
    { value: 'daily', labelKey: 'marketOverview.blocks.sectors.period.daily' },
    { value: '1w', labelKey: 'marketOverview.blocks.sectors.period.1w' },
    { value: '1m', labelKey: 'marketOverview.blocks.sectors.period.1m' },
    { value: 'ytd', labelKey: 'marketOverview.blocks.sectors.period.ytd' },
  ]

  return (
    <div
      role="group"
      aria-label={t('marketOverview.blocks.sectors.periodLabel')}
      className="inline-flex w-fit items-center rounded-[2px] border border-border bg-card p-0.5"
    >
      {options.map(({ value, labelKey }) => {
        const active = value === period
        return (
          <Button
            key={value}
            type="button"
            size="sm"
            variant={active ? 'default' : 'ghost'}
            aria-pressed={active}
            data-testid={`sector-period-${value}`}
            className="font-mono text-xs"
            onClick={() => {
              if (!active) onPeriodChange(value)
            }}
          >
            {t(labelKey)}
          </Button>
        )
      })}
    </div>
  )
}

function SectorPerformanceBlock({
  query,
  period,
  onPeriodChange,
}: {
  query: BlockQuery
  period: SectorPeriod
  onPeriodChange: (next: SectorPeriod) => void
}) {
  const { t, i18n } = useTranslation()
  const lang = i18n.resolvedLanguage ?? 'tr'

  return (
    <section
      data-testid="market-block-sectors"
      aria-label={t('marketOverview.blocks.sectors.title')}
      className="border-b border-border py-10 grid grid-cols-1 lg:grid-cols-[0.29fr_0.71fr] gap-8 lg:gap-10 items-start"
    >
      <div className="flex flex-col gap-3">
        <span
          lang={lang}
          className="font-mono text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground"
        >
          {t('marketOverview.blocks.sectors.kicker')}
        </span>
        <h2 className="text-h2 font-semibold tracking-tight text-foreground">
          {t('marketOverview.blocks.sectors.title')}
        </h2>
        <p className="text-sm text-muted-foreground">
          {t('marketOverview.blocks.sectors.subtitle')}
        </p>
        <div className="pt-1">
          <SectorPeriodSelector period={period} onPeriodChange={onPeriodChange} />
        </div>
      </div>

      <div>
        <BlockContent query={query}>
          {(data) => {
            const entries = Object.entries(data.sectorPerformance)
            if (entries.length === 0) return <Unavailable />
            const maxAbs = Math.max(
              ...entries.map(([, val]) => Math.abs(val)),
              0.0001,
            )

            return (
              <ul className="flex flex-col border-y border-border divide-y divide-border">
                {entries.map(([code, change]) => {
                  const pctWidth =
                    change === 0
                      ? 0
                      : Math.max((Math.abs(change) / maxAbs) * 50, 1.5)
                  return (
                    <li key={code}>
                      <Link
                        data-testid={`sector-entry-${code}`}
                        data-sector-bar-row=""
                        to={`/stocks?sector=${encodeURIComponent(code)}`}
                        className="grid grid-cols-[minmax(88px,140px)_1fr_auto] sm:grid-cols-[minmax(120px,180px)_1fr_auto] items-center gap-3 sm:gap-4 px-2 py-3 transition-colors hover:bg-accent/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                      >
                        <span className="truncate font-medium text-foreground">
                          {code}
                        </span>
                        <div
                          className="relative flex h-4 items-center"
                          aria-hidden="true"
                        >
                          <span className="absolute left-1/2 top-0 bottom-0 w-px bg-[var(--border-strong)]" />
                          {change >= 0 ? (
                            <span
                              data-testid={`sector-bar-${code}`}
                              className="absolute left-1/2 h-2 bg-[var(--pos)]"
                              style={{ width: `${pctWidth}%` }}
                            />
                          ) : (
                            <span
                              data-testid={`sector-bar-${code}`}
                              className="absolute right-1/2 h-2 bg-[var(--neg)]"
                              style={{ width: `${pctWidth}%` }}
                            />
                          )}
                        </div>
                        <span className="font-mono text-sm text-right">
                          <ChangeValue value={change} />
                        </span>
                      </Link>
                    </li>
                  )
                })}
              </ul>
            )
          }}
        </BlockContent>
      </div>
    </section>
  )
}

function BreadthStat({
  testId,
  label,
  value,
  valueClassName,
}: {
  testId: string
  label: string
  value: number
  valueClassName?: string
}) {
  return (
    <div data-testid={testId} className="flex flex-col gap-0.5">
      <dt className="text-[11px] text-muted-foreground">{label}</dt>
      <dd className={cn('font-mono text-lg font-medium tabular-nums', valueClassName)}>
        {value}
      </dd>
    </div>
  )
}

function BreadthBlock({ query }: { query: BlockQuery }) {
  const { t, i18n } = useTranslation()
  const lang = i18n.resolvedLanguage ?? 'tr'

  return (
    <section
      data-testid="market-block-breadth"
      aria-label={t('marketOverview.blocks.breadth.title')}
      className="flex flex-col justify-between gap-3 p-4 sm:p-5 border-b sm:border-b-0 sm:border-r border-border"
    >
      <h2
        lang={lang}
        className="font-mono text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground"
      >
        {t('marketOverview.blocks.breadth.title')}
      </h2>
      <BlockContent query={query}>
        {(data) => {
          if (!data.breadth) return <Unavailable />
          const { advancing, declining, unchanged } = data.breadth
          const total = advancing + declining + unchanged
          return (
            <div className="flex flex-col gap-2.5">
              <dl className="grid grid-cols-3 gap-2">
                <BreadthStat
                  testId="breadth-advancing"
                  label={t('marketOverview.blocks.breadth.advancing')}
                  value={advancing}
                  valueClassName="text-[var(--pos)]"
                />
                <BreadthStat
                  testId="breadth-declining"
                  label={t('marketOverview.blocks.breadth.declining')}
                  value={declining}
                  valueClassName="text-[var(--neg)]"
                />
                <BreadthStat
                  testId="breadth-unchanged"
                  label={t('marketOverview.blocks.breadth.unchanged')}
                  value={unchanged}
                  valueClassName="text-muted-foreground"
                />
              </dl>
              {total > 0 ? (
                <div
                  data-testid="breadth-bar"
                  className="flex h-1 w-full overflow-hidden bg-border"
                  aria-hidden="true"
                >
                  <span
                    className="bg-[var(--pos)]"
                    style={{ width: `${(advancing / total) * 100}%` }}
                  />
                  <span
                    className="bg-muted-foreground"
                    style={{ width: `${(unchanged / total) * 100}%` }}
                  />
                  <span
                    className="bg-[var(--neg)]"
                    style={{ width: `${(declining / total) * 100}%` }}
                  />
                </div>
              ) : null}
            </div>
          )
        }}
      </BlockContent>
    </section>
  )
}

function moverSparkPoints(symbol: string, changePct: number): string {
  const seed = Array.from(symbol).reduce((acc, ch) => acc + ch.charCodeAt(0), 0)
  const vals = Array.from({ length: 10 }, (_, i) => {
    const trend = changePct * 65 * (i / 9)
    const wave = Math.sin(i * 1.3 + seed) * 1.4
    return 100 + trend + wave
  })
  const min = Math.min(...vals)
  const max = Math.max(...vals)
  const span = max - min || 1
  return vals
    .map(
      (v, i) =>
        `${((i / (vals.length - 1)) * 46).toFixed(1)},${(15 - ((v - min) / span) * 12).toFixed(1)}`,
    )
    .join(' ')
}

function MoverList({
  kind,
  title,
  entries,
}: {
  kind: 'gainer' | 'loser'
  title: string
  entries: MoverEntry[]
}) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const maxVol = Math.max(...entries.map((entry) => entry.volume), 1)

  return (
    <div className="flex flex-col">
      <div
        className={cn(
          'flex items-baseline justify-between border-b-2 pb-2.5',
          kind === 'gainer' ? 'border-[var(--pos)]' : 'border-[var(--neg)]',
        )}
      >
        <h3
          lang={locale}
          className="font-mono text-xs font-medium uppercase tracking-[0.08em] text-foreground"
        >
          {title}
        </h3>
        <span
          aria-hidden="true"
          className={cn(
            'font-mono text-[11px]',
            kind === 'gainer' ? 'text-[var(--pos)]' : 'text-[var(--neg)]',
          )}
        >
          {kind === 'gainer' ? '▲' : '▼'} {t('marketOverview.blocks.movers.changeCol')} · 10D
        </span>
      </div>

      {entries.length === 0 ? (
        <p className="py-4 text-sm text-muted-foreground">
          {t('marketOverview.blocks.movers.empty')}
        </p>
      ) : (
        <>
          <div
            aria-hidden="true"
            lang={locale}
            className="grid grid-cols-[1fr_48px_auto_auto] gap-4 border-b border-[var(--border-strong)] px-1 py-2 font-mono text-[10px] font-medium uppercase tracking-[0.05em] text-muted-foreground"
          >
            <span>{t('marketOverview.blocks.movers.symbolCol')}</span>
            <span>10D</span>
            <span className="text-right">
              {t('marketOverview.blocks.movers.changeCol')}
            </span>
            <span className="min-w-[88px] text-right">
              {t('marketOverview.blocks.movers.volumeCol')}
            </span>
          </div>
          <ul className="flex flex-col divide-y divide-border">
            {entries.map((entry) => {
              const volDepthPct = Math.min(
                76,
                Math.max(4, Math.round((entry.volume / maxVol) * 76)),
              )
              return (
                <li key={entry.symbol}>
                  <Link
                    data-testid={`mover-${kind}-${entry.symbol}`}
                    data-row-link=""
                    to={`/stocks/${entry.symbol}`}
                    aria-label={t('marketOverview.moverLinkLabel', {
                      symbol: entry.symbol,
                      change: formatSignedPercent(locale, entry.changePct),
                      volume: formatNumber(locale, entry.volume),
                    })}
                    className="grid grid-cols-[1fr_48px_auto_auto] items-center gap-4 px-1 py-2.5 transition-colors hover:bg-accent/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    <span className="font-mono text-sm font-semibold text-foreground">
                      {entry.symbol}
                    </span>
                    <svg
                      width="48"
                      height="16"
                      viewBox="0 0 48 16"
                      aria-hidden="true"
                      className="shrink-0"
                    >
                      <polyline
                        fill="none"
                        stroke={entry.changePct >= 0 ? 'var(--pos)' : 'var(--neg)'}
                        strokeWidth="1.3"
                        points={moverSparkPoints(entry.symbol, entry.changePct)}
                      />
                    </svg>
                    <ChangeValue
                      value={entry.changePct}
                      className="justify-end font-mono text-sm"
                    />
                    <span
                      data-testid={`mover-volume-${entry.symbol}`}
                      className="relative min-w-[88px] pb-1 text-right font-mono text-sm text-muted-foreground tabular-nums"
                    >
                      {formatNumber(locale, entry.volume)}
                      <span
                        data-slot="vol-depth"
                        aria-hidden="true"
                        className="pointer-events-none absolute right-0 bottom-0 h-[2px] bg-[var(--border-strong)]"
                        style={{ width: `${volDepthPct}%` }}
                      />
                    </span>
                  </Link>
                </li>
              )
            })}
          </ul>
        </>
      )}
    </div>
  )
}

function TopMoversBlock({ query }: { query: BlockQuery }) {
  const { t, i18n } = useTranslation()
  const lang = i18n.resolvedLanguage ?? 'tr'

  return (
    <section
      data-testid="market-block-movers"
      aria-label={t('marketOverview.blocks.movers.title')}
      className="py-10 flex flex-col gap-6"
    >
      <div className="flex flex-col gap-1.5">
        <span
          lang={lang}
          className="font-mono text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground"
        >
          {t('marketOverview.blocks.movers.kicker')}
        </span>
        <h2 className="text-h2 font-semibold tracking-tight text-foreground">
          {t('marketOverview.blocks.movers.title')}
        </h2>
      </div>
      <BlockContent query={query}>
        {(data) => (
          <div className="grid grid-cols-1 gap-10 lg:grid-cols-2">
            <MoverList
              kind="gainer"
              title={t('marketOverview.blocks.movers.gainers')}
              entries={data.gainers}
            />
            <MoverList
              kind="loser"
              title={t('marketOverview.blocks.movers.losers')}
              entries={data.losers}
            />
          </div>
        )}
      </BlockContent>
    </section>
  )
}

function MarketVolumeBlock({ query }: { query: BlockQuery }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'

  return (
    <section
      data-testid="market-block-volume"
      aria-label={t('marketOverview.blocks.volume.title')}
      className="flex flex-col justify-between gap-2 p-4 sm:p-5"
    >
      <h2
        lang={locale}
        className="font-mono text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground"
      >
        {t('marketOverview.blocks.volume.title')}
      </h2>
      <BlockContent query={query}>
        {(data) =>
          data.volumeTotal !== null ? (
            <div className="flex flex-col gap-1">
              <span
                data-testid="market-volume-total"
                className="font-mono text-2xl font-medium tracking-tight tabular-nums text-foreground"
              >
                {formatNumber(locale, data.volumeTotal)}
              </span>
              <span className="font-mono text-xs text-muted-foreground">
                {t('marketOverview.blocks.volume.label')}
              </span>
            </div>
          ) : (
            <Unavailable />
          )
        }
      </BlockContent>
    </section>
  )
}

function MarketValuationBlock({ query }: { query: BlockQuery }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'

  return (
    <section
      data-testid="market-block-valuation"
      aria-label={t('marketOverview.blocks.valuation.title')}
      className="flex flex-col gap-5"
    >
      <h2
        lang={locale}
        className="font-mono text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground"
      >
        {t('marketOverview.blocks.valuation.title')}
      </h2>
      <BlockContent query={query}>
        {(data) => (
          <div className="flex flex-col gap-5">
            <div className="flex flex-col gap-2">
              <dl className="flex flex-col gap-2">
                <dt className="font-mono text-xs text-muted-foreground">
                  {t('marketOverview.blocks.valuation.marketPe')}
                </dt>
                <dd
                  data-testid="market-pe"
                  className="font-mono text-4xl sm:text-[44px] font-medium leading-none tracking-tight tabular-nums text-foreground"
                >
                  {data.marketPe !== null
                    ? formatNumber(locale, data.marketPe, {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })
                    : t('honestData.unavailableTitle')}
                </dd>
              </dl>
              {data.marketPeExcludedCount !== null ? (
                <p
                  data-testid="market-pe-disclosure"
                  className="font-mono text-xs leading-relaxed text-muted-foreground"
                >
                  {t('marketOverview.blocks.valuation.exclusion', {
                    count: data.marketPeExcludedCount,
                  })}
                </p>
              ) : null}
            </div>

            <dl className="flex flex-col gap-2 border-t border-border pt-5">
              <dt className="font-mono text-xs text-muted-foreground">
                {t('marketOverview.blocks.valuation.marketDivYield')}
              </dt>
              <dd
                data-testid="market-div-yield"
                className="font-mono text-3xl font-medium leading-none tracking-tight tabular-nums text-foreground"
              >
                {data.marketDivYield !== null
                  ? formatRatioPercent(locale, data.marketDivYield)
                  : t('honestData.unavailableTitle')}
              </dd>
            </dl>
          </div>
        )}
      </BlockContent>
    </section>
  )
}

export function MarketOverviewPage() {
  const { t, i18n } = useTranslation()
  const lang = i18n.resolvedLanguage ?? 'tr'
  // The sector-performance period is session-scoped UI state (UXR-MOV-012 §6):
  // never persisted server-side, reset on reload.
  const [sectorPeriod, setSectorPeriod] = useState<SectorPeriod>('daily')
  const indices = useMarketBlock('indices')
  const sectors = useMarketBlock('sectors', sectorPeriod)
  const breadth = useMarketBlock('breadth')
  const movers = useMarketBlock('movers')
  const volume = useMarketBlock('volume')
  const valuation = useMarketBlock('valuation')

  // Every block serves the same snapshot, so any successful block carries the
  // dashboard-level as-of date and stale flag (UXR-MOV-010).
  const envelope = [indices, sectors, breadth, movers, volume, valuation].find(
    (query) => query.data,
  )?.data

  const heroRef = useRef<HTMLElement | null>(null)
  const heroCanvasRef = useRef<HTMLCanvasElement | null>(null)
  const scrubReadout = useHeroBackdropCanvas({
    heroRef,
    canvasRef: heroCanvasRef,
    xu100Level: indices.data?.indices?.xu100.level ?? null,
    asOf: envelope?.asOf,
    locale: lang,
  })

  return (
    <section
      data-screen="SCR-001"
      data-route-id="marketOverview"
      className="flex flex-col"
    >
      <header
        ref={heroRef}
        id="hero-section"
        className="relative overflow-hidden pb-10 pt-4"
      >
        <canvas
          ref={heroCanvasRef}
          id="hero-bg-canvas"
          data-testid="hero-bg-canvas"
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 z-0 h-full w-full"
        />

        <div className="relative z-10 grid grid-cols-1 items-center gap-10 lg:grid-cols-[1.20fr_0.80fr] lg:gap-12">
          <div className="flex flex-col gap-4">
            <div className="flex flex-wrap items-center gap-3">
              <span
                data-testid="market-hero-eyebrow"
                lang={lang}
                className="font-mono text-[11px] font-medium uppercase tracking-[0.09em] text-[var(--stale-foreground)]"
              >
                {envelope?.priceState?.state === 'intraday'
                  ? t('marketOverview.hero.eyebrowIntraday')
                  : t('marketOverview.hero.eyebrow')}
              </span>
              {envelope ? (
                <>
                  {/* TKT-foundation-073: the as-of marker carries its own explanation,
                      and the recency half only while the row really is the latest
                      publication — suppressed on a stale envelope and while an intraday
                      price stands in for the completed close (the same two conditions
                      the paragraph below uses). */}
                  <AsOfDate
                    asOf={envelope.asOf}
                    fresh={
                      !envelope.stale &&
                      envelope.priceState?.state !== 'intraday'
                    }
                  />
                  {envelope.stale ? <StaleBadge /> : null}
                </>
              ) : null}
            </div>

            <h1 className="max-w-2xl text-display font-semibold tracking-tight text-foreground sm:text-[2.5rem] sm:leading-[1.1]">
              {t('screens.marketOverview.title')}
            </h1>

            <p className="max-w-2xl text-base leading-relaxed text-muted-foreground">
              {t('screens.marketOverview.description')}
            </p>

            <div className="flex flex-wrap items-center gap-3 pt-1">
              <Link
                to="/screener"
                data-magnetic=""
                className={buttonVariants({ variant: 'default', size: 'default' })}
              >
                {t('marketOverview.hero.primaryCta')}
              </Link>
              <Link
                to="/stocks"
                data-magnetic=""
                className={buttonVariants({ variant: 'outline', size: 'default' })}
              >
                {t('marketOverview.hero.secondaryCta')}
              </Link>
            </div>

            <div
              data-testid="hero-scrub-hint"
              className="inline-flex w-fit items-center gap-2 border border-border bg-card/90 px-2.5 py-1.5 font-mono text-[11px] text-muted-foreground"
            >
              <span aria-hidden="true" className="text-[var(--stale-foreground)]">
                +
              </span>
              <span id="hero-live-readout">
                {scrubReadout ?? t('marketOverview.hero.scrubHint')}
              </span>
            </div>

            {envelope ? (
              <div className="flex flex-col gap-1 pt-1">
                {/* TKT-foundation-049: the price header renders the served
                    intraday/close state. Its stale flag is left to the envelope row
                    above (one envelope, one badge). */}
                <PriceStateHeader
                  priceState={envelope.priceState}
                  asOf={envelope.asOf}
                  stale={envelope.stale}
                  showStale={false}
                />
                {/* TKT-foundation-039 (finding B3): the as-of date means "the latest
                    completed session" — stated, so a fresh-but-not-today row is never
                    read as a data error (FR-MOV-010/020, UXR-MOV-010). A stale row
                    suppresses the recency claim: 037's stale means a newer stored
                    session exists that this row lacks, so the badge beside it is the
                    honest statement (review round 1, DR-1). TKT-foundation-049: an
                    intraday price on the screen also falsifies the "closing values /
                    no intraday prices" claim, so it is suppressed there too. */}
                {envelope.stale || envelope.priceState?.state === 'intraday' ? null : (
                  <p
                    data-testid="market-asof-explanation"
                    className="text-xs text-muted-foreground"
                  >
                    {t('screens.marketOverview.asOfExplanation')}
                  </p>
                )}
              </div>
            ) : null}
          </div>

          <HeroInteractivePlate />
        </div>
      </header>

      {/* 4-Column KPI Ribbon */}
      <div className="border-y border-border bg-card grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4">
        <IndexLevelsBlock query={indices} />
        <BreadthBlock query={breadth} />
        <MarketVolumeBlock query={volume} />
      </div>

      {/* Band 01: Market Valuation + Paired Inflation & Macro */}
      <div className="border-b border-border py-10 grid grid-cols-1 lg:grid-cols-[0.36fr_0.64fr] gap-10 items-start">
        <MarketValuationBlock query={valuation} />
        <MacroStrip />
      </div>

      {/* Band 02: Diverging Sector Return Bars */}
      <SectorPerformanceBlock
        query={sectors}
        period={sectorPeriod}
        onPeriodChange={setSectorPeriod}
      />

      {/* Band 03: Open Borderless Movers Ledgers */}
      <TopMoversBlock query={movers} />
    </section>
  )
}
