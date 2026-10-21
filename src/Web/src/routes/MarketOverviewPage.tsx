import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery, type UseQueryResult } from '@tanstack/react-query'
import { Link } from 'react-router'
import { ArrowDown, ArrowUp } from 'lucide-react'

import { AsOfDate, NoDataState, StaleBadge } from '@/components/honest-data'
import { Button } from '@/components/ui/button'
import { SkeletonText } from '@/components/ui/skeleton'
import { MacroStrip } from '@/routes/MacroStrip'
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
 * The daily "discover" entry point: index levels, sector performance (entries link
 * to the Stock List pre-filtered), market breadth, top movers with volume (entries
 * link to the stock page), total market volume, and the market-valuation block with
 * its loss-maker exclusion disclosure. The macro strip (TKT-mov-006) is rendered
 * below the equity grid from its own endpoint (separate failure domain); the
 * informational-only disclaimer is rendered by the shell footer for research routes
 * (UXR-G-016).
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

/** A dashboard block: its own heading + independent loading/error/success states. */
function Block({
  title,
  testId,
  query,
  children,
}: {
  title: string
  testId: string
  query: BlockQuery
  children: (data: MarketOverviewResponse) => ReactNode
}) {
  return (
    <section
      data-testid={testId}
      aria-label={title}
      className="flex flex-col gap-3 rounded-lg border bg-background p-4"
    >
      <h2 className="text-h3 font-semibold tracking-tight">{title}</h2>
      {query.isPending ? <BlockSkeleton /> : null}
      {query.isError ? <BlockError onRetry={() => void query.refetch()} /> : null}
      {query.isSuccess ? children(query.data) : null}
    </section>
  )
}

/** A daily % change with its sign and an arrow — never colour-only (SCR-001 §9). */
function ChangeValue({ value, className }: { value: number; className?: string }) {
  const { i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const Icon = value < 0 ? ArrowDown : ArrowUp
  return (
    <span className={cn('inline-flex items-center gap-1 tabular-nums', className)}>
      <Icon aria-hidden="true" className="size-3.5 shrink-0" />
      {formatSignedPercent(locale, value)}
    </span>
  )
}

function IndexRow({
  id,
  label,
  index,
}: {
  id: string
  label: string
  index: IndexLevel
}) {
  const { i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  return (
    <div
      data-testid={`index-${id}`}
      className="flex items-baseline justify-between gap-3"
    >
      <dt className="font-medium text-foreground">{label}</dt>
      <dd className="flex items-baseline gap-3">
        <span className="tabular-nums">
          {formatNumber(locale, index.level, {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
          })}
        </span>
        <ChangeValue value={index.changePct} />
      </dd>
    </div>
  )
}

function IndexLevelsBlock({ query }: { query: BlockQuery }) {
  const { t } = useTranslation()
  return (
    <Block
      title={t('marketOverview.blocks.indices.title')}
      testId="market-block-indices"
      query={query}
    >
      {(data) =>
        data.indices ? (
          <dl className="flex flex-col gap-2">
            <IndexRow
              id="xu100"
              label={t('marketOverview.blocks.indices.xu100')}
              index={data.indices.xu100}
            />
            <IndexRow
              id="xu30"
              label={t('marketOverview.blocks.indices.xu30')}
              index={data.indices.xu30}
            />
          </dl>
        ) : (
          <Unavailable />
        )
      }
    </Block>
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
      className="inline-flex items-center rounded-md border p-0.5"
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
  const { t } = useTranslation()
  return (
    <Block
      title={t('marketOverview.blocks.sectors.title')}
      testId="market-block-sectors"
      query={query}
    >
      {(data) => {
        const entries = Object.entries(data.sectorPerformance)
        return (
          <div className="flex flex-col gap-3">
            <SectorPeriodSelector period={period} onPeriodChange={onPeriodChange} />
            {entries.length === 0 ? (
              <Unavailable />
            ) : (
              <ul className="flex flex-col divide-y">
                {entries.map(([code, change]) => (
                  <li key={code}>
                    <Link
                      data-testid={`sector-entry-${code}`}
                      to={`/stocks?sector=${encodeURIComponent(code)}`}
                      aria-label={t('marketOverview.blocks.sectors.linkLabel', {
                        sector: code,
                      })}
                      className="flex items-baseline justify-between gap-3 py-2 underline-offset-4 hover:underline focus-visible:underline"
                    >
                      <span className="font-medium text-foreground">{code}</span>
                      {/*
                        The daily change is supplementary to the link's purpose (open
                        the filtered list). Presenting it as a parenthetical aside keeps
                        the link's accessible name (sector + intent) stable while the
                        visible non-parenthetical text stays contained in that name
                        (WCAG 2.5.3 Label in Name; UXR-G-027).
                      */}
                      <span className="text-muted-foreground">
                        {'('}
                        <ChangeValue value={change} className="text-foreground" />
                        {')'}
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )
      }}
    </Block>
  )
}

function BreadthStat({
  testId,
  label,
  value,
}: {
  testId: string
  label: string
  value: number
}) {
  return (
    <div data-testid={testId} className="flex flex-col">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="text-h2 tabular-nums">{value}</dd>
    </div>
  )
}

function BreadthBlock({ query }: { query: BlockQuery }) {
  const { t } = useTranslation()
  return (
    <Block
      title={t('marketOverview.blocks.breadth.title')}
      testId="market-block-breadth"
      query={query}
    >
      {(data) =>
        data.breadth ? (
          <dl className="flex flex-wrap gap-x-8 gap-y-3">
            <BreadthStat
              testId="breadth-advancing"
              label={t('marketOverview.blocks.breadth.advancing')}
              value={data.breadth.advancing}
            />
            <BreadthStat
              testId="breadth-declining"
              label={t('marketOverview.blocks.breadth.declining')}
              value={data.breadth.declining}
            />
            <BreadthStat
              testId="breadth-unchanged"
              label={t('marketOverview.blocks.breadth.unchanged')}
              value={data.breadth.unchanged}
            />
          </dl>
        ) : (
          <Unavailable />
        )
      }
    </Block>
  )
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
  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-body font-medium text-foreground">{title}</h3>
      {entries.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          {t('marketOverview.blocks.movers.empty')}
        </p>
      ) : (
        <ul className="flex flex-col divide-y">
          {entries.map((entry) => (
            <li key={entry.symbol}>
              <Link
                data-testid={`mover-${kind}-${entry.symbol}`}
                to={`/stocks/${entry.symbol}`}
                aria-label={t('marketOverview.moverLinkLabel', {
                  symbol: entry.symbol,
                  change: formatSignedPercent(locale, entry.changePct),
                  volume: formatNumber(locale, entry.volume),
                })}
                className="flex items-baseline justify-between gap-3 py-2 underline-offset-4 hover:underline focus-visible:underline"
              >
                <span className="font-mono text-sm font-medium text-foreground">
                  {entry.symbol}
                </span>
                <span className="flex items-baseline gap-3">
                  <ChangeValue value={entry.changePct} />
                  <span
                    data-testid={`mover-volume-${entry.symbol}`}
                    className="text-sm text-muted-foreground tabular-nums"
                  >
                    {formatNumber(locale, entry.volume)}
                  </span>
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function TopMoversBlock({ query }: { query: BlockQuery }) {
  const { t } = useTranslation()
  return (
    <Block
      title={t('marketOverview.blocks.movers.title')}
      testId="market-block-movers"
      query={query}
    >
      {(data) => (
        <div className="grid gap-5 sm:grid-cols-2">
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
    </Block>
  )
}

function MarketVolumeBlock({ query }: { query: BlockQuery }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  return (
    <Block
      title={t('marketOverview.blocks.volume.title')}
      testId="market-block-volume"
      query={query}
    >
      {(data) =>
        data.volumeTotal !== null ? (
          <div className="flex flex-col gap-1">
            <span className="text-sm text-muted-foreground">
              {t('marketOverview.blocks.volume.label')}
            </span>
            <span data-testid="market-volume-total" className="text-h2 tabular-nums">
              {formatNumber(locale, data.volumeTotal)}
            </span>
          </div>
        ) : (
          <Unavailable />
        )
      }
    </Block>
  )
}

function MarketValuationBlock({ query }: { query: BlockQuery }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  return (
    <Block
      title={t('marketOverview.blocks.valuation.title')}
      testId="market-block-valuation"
      query={query}
    >
      {(data) => (
        <div className="flex flex-col gap-3">
          <dl className="flex flex-wrap gap-x-8 gap-y-3">
            <div className="flex flex-col">
              <dt className="text-sm text-muted-foreground">
                {t('marketOverview.blocks.valuation.marketPe')}
              </dt>
              <dd data-testid="market-pe" className="text-h3 tabular-nums">
                {data.marketPe !== null
                  ? formatNumber(locale, data.marketPe, {
                      minimumFractionDigits: 2,
                      maximumFractionDigits: 2,
                    })
                  : t('honestData.unavailableTitle')}
              </dd>
            </div>
            <div className="flex flex-col">
              <dt className="text-sm text-muted-foreground">
                {t('marketOverview.blocks.valuation.marketDivYield')}
              </dt>
              <dd data-testid="market-div-yield" className="text-h3 tabular-nums">
                {data.marketDivYield !== null
                  ? formatRatioPercent(locale, data.marketDivYield)
                  : t('honestData.unavailableTitle')}
              </dd>
            </div>
          </dl>
          {data.marketPeExcludedCount !== null ? (
            <p
              data-testid="market-pe-disclosure"
              className="text-xs text-muted-foreground"
            >
              {t('marketOverview.blocks.valuation.exclusion', {
                count: data.marketPeExcludedCount,
              })}
            </p>
          ) : null}
        </div>
      )}
    </Block>
  )
}

export function MarketOverviewPage() {
  const { t } = useTranslation()
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

  return (
    <section
      data-screen="SCR-001"
      data-route-id="marketOverview"
      className="flex flex-col gap-6"
    >
      <header className="flex flex-col gap-2">
        <h1 className="text-h1 font-semibold tracking-tight">
          {t('screens.marketOverview.title')}
        </h1>
        <p className="max-w-2xl text-muted-foreground">
          {t('screens.marketOverview.description')}
        </p>
        {envelope ? (
          <div className="flex flex-col gap-1">
            <div className="flex flex-wrap items-center gap-3">
              <AsOfDate asOf={envelope.asOf} />
              {envelope.stale ? <StaleBadge /> : null}
            </div>
            {/* TKT-foundation-039 (finding B3): the as-of date means "the latest
                completed session" — stated, so a fresh-but-not-today row is never
                read as a data error (FR-MOV-010/020, UXR-MOV-010). A stale row
                suppresses the recency claim: 037's stale means a newer stored
                session exists that this row lacks, so the badge beside it is the
                honest statement (review round 1, DR-1). */}
            {envelope.stale ? null : (
              <p
                data-testid="market-asof-explanation"
                className="text-xs text-muted-foreground"
              >
                {t('screens.marketOverview.asOfExplanation')}
              </p>
            )}
          </div>
        ) : null}
      </header>

      <div className="grid gap-4 lg:grid-cols-2">
        <IndexLevelsBlock query={indices} />
        <SectorPerformanceBlock
          query={sectors}
          period={sectorPeriod}
          onPeriodChange={setSectorPeriod}
        />
        <BreadthBlock query={breadth} />
        <TopMoversBlock query={movers} />
        <MarketVolumeBlock query={volume} />
        <MarketValuationBlock query={valuation} />
      </div>

      <MacroStrip />
    </section>
  )
}
