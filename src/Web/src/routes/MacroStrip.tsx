import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'

import { AsOfDate, StaleBadge } from '@/components/honest-data'
import { Button } from '@/components/ui/button'
import { SkeletonText } from '@/components/ui/skeleton'
import { fetchMarketMacro, marketQueryKeys } from '@/lib/api/market'
import type { MacroSeries } from '@/lib/api/contracts'
import { formatNumber } from '@/lib/format'
import { unitLabel } from '@/lib/units'

/**
 * SCR-001 macro strip (UX `market-overview.md` §3.7; UXR-MOV-007..010;
 * FR-MOV-007..010, 016; TC-MOV-016/017/018/024).
 *
 * The six V1 macro indicators (TÜİK CPI, the independent inflation measure, CBRT
 * 1-week repo, USD/TRY, EUR/TRY, gold), each carrying its value, unit,
 * plain-language label, source attribution and as-of date so daily and monthly
 * series are never misread as equally current (FR-MOV-009, NFR-MOV-004). The
 * official and independent inflation measures are always presented as a pair;
 * when the independent measure is unavailable the pair degrades to the official
 * value with an explicit note and the unavailable label retained — never hidden,
 * never blank-as-zero (BR-MOV-002, UXR-MOV-008/009). A stale series carries the
 * stale marker together with its as-of date (UXR-MOV-010). Data is end-of-day;
 * the strip never polls or implies real-time (UXR-G-012).
 *
 * It reads its own endpoint (`GET /market/macro`), separate from the equity
 * snapshot (M-1/M-5, SCR-001 §5), so a macro outage never blanks the equity
 * blocks and vice versa (TC-MOV-014).
 */

/** Plain-language series labels, keyed by stable series code (UXR-MOV-007). */
const SERIES_LABEL_KEYS: Record<string, string> = {
  TUIK_CPI: 'marketOverview.macro.series.TUIK_CPI',
  INDEP_CPI: 'marketOverview.macro.series.INDEP_CPI',
  CBRT_REPO: 'marketOverview.macro.series.CBRT_REPO',
  USD_TRY: 'marketOverview.macro.series.USD_TRY',
  EUR_TRY: 'marketOverview.macro.series.EUR_TRY',
  GOLD: 'marketOverview.macro.series.GOLD',
}

/** The inflation pair (BR-MOV-002) — official first, independent second. */
const OFFICIAL_CODE = 'TUIK_CPI'
const INDEPENDENT_CODE = 'INDEP_CPI'

/** The remaining four indicators, in serving order (FR-MOV-007). */
const REMAINING_ORDER = ['CBRT_REPO', 'USD_TRY', 'EUR_TRY', 'GOLD'] as const

/** Availability is `state`, not freshness — staleness is the `stale` boolean. */
function isUnavailable(series: MacroSeries): boolean {
  return series.state === 'unavailable' || series.value === null
}

/**
 * Publication cadence per series (TKT-foundation-039, finding B3). Mirrors the
 * seeded `macro_series.cadence` column (`DegerliSeed.MacroSeriesSql`): the CPI
 * pair is monthly, the policy rate is published per decision, FX and gold daily.
 * A series is never labelled more current than its source publishes, so an older
 * monthly figure reads as "latest published value", not as a stale feed
 * (TKT-foundation-038's publication-aware freshness, made self-explanatory).
 */
type Cadence = 'monthly' | 'perRelease' | 'daily'

/**
 * Partial on purpose: an unmapped code renders no cadence note at all. A
 * `?? 'daily'` default would label a future weekly/quarterly series as *more*
 * current than it publishes — no claim beats a false one (TKT-foundation-039
 * review round 1, DR-2).
 */
const SERIES_CADENCE: Partial<Record<string, Cadence>> = {
  TUIK_CPI: 'monthly',
  INDEP_CPI: 'monthly',
  CBRT_REPO: 'perRelease',
  USD_TRY: 'daily',
  EUR_TRY: 'daily',
  GOLD: 'daily',
}

const CADENCE_LABEL_KEYS: Record<Cadence, string> = {
  monthly: 'marketOverview.macro.cadence.monthly',
  perRelease: 'marketOverview.macro.cadence.perRelease',
  daily: 'marketOverview.macro.cadence.daily',
}

function MacroIndicator({ series }: { series: MacroSeries }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const unavailable = isUnavailable(series)
  const labelKey = SERIES_LABEL_KEYS[series.code]
  const label = labelKey ? t(labelKey) : series.code
  const cadence = SERIES_CADENCE[series.code]

  return (
    <div data-testid={`macro-${series.code}`} className="flex flex-col gap-1">
      <dt className="text-sm font-medium text-foreground">{label}</dt>
      <dd className="flex flex-col gap-1">
        {unavailable ? (
          <span
            data-testid={`macro-unavailable-${series.code}`}
            className="text-sm text-muted-foreground"
          >
            {t('honestData.unavailableTitle')}
          </span>
        ) : (
          <span className="flex items-baseline gap-1">
            <span
              data-testid={`macro-value-${series.code}`}
              className="text-h3 tabular-nums"
            >
              {formatNumber(locale, series.value as number, {
                maximumFractionDigits: 2,
              })}
            </span>
            <span
              data-testid={`macro-unit-${series.code}`}
              className="text-sm text-muted-foreground"
            >
              {/* The served `unit` is a code, not prose (`03` §1): localized here,
                  with an honest "unit not specified" fallback (TKT-foundation-072). */}
              {unitLabel(series.unit, t)}
            </span>
          </span>
        )}
        {series.source ? (
          <span
            data-testid={`macro-source-${series.code}`}
            className="text-xs text-muted-foreground"
          >
            {t('marketOverview.macro.source', { source: series.source })}
          </span>
        ) : null}
        {series.asOf ? (
          <span className="flex flex-col gap-0.5">
            <AsOfDate asOf={series.asOf} />
            {/* The as-of date says what it qualifies: this cadence's latest
                published value (TKT-foundation-039, UXR-MOV-010). A stale series
                suppresses the recency claim, because the sibling stale badge
                states that a newer completed session/publication has not
                arrived — both cannot be true (review round 1, DR-1). */}
            {!series.stale && cadence ? (
              <span
                data-slot="macro-cadence"
                className="text-xs text-muted-foreground"
              >
                {t(CADENCE_LABEL_KEYS[cadence])}
              </span>
            ) : null}
          </span>
        ) : null}
        {series.stale ? <StaleBadge /> : null}
      </dd>
    </div>
  )
}

function InflationPair({
  official,
  independent,
}: {
  official?: MacroSeries
  independent?: MacroSeries
}) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-2" data-testid="macro-inflation">
      <h3 className="text-body font-medium text-foreground">
        {t('marketOverview.macro.inflationTitle')}
      </h3>
      <dl
        data-testid="macro-inflation-pair"
        className="flex flex-col gap-3 sm:flex-row sm:gap-8"
      >
        {official ? <MacroIndicator series={official} /> : null}
        {independent ? <MacroIndicator series={independent} /> : null}
      </dl>
      {!independent || isUnavailable(independent) ? (
        <p
          data-testid="macro-independent-unavailable"
          className="text-xs text-muted-foreground"
        >
          {t('marketOverview.macro.independentUnavailable')}
        </p>
      ) : null}
    </div>
  )
}

function MacroSkeleton() {
  const { t } = useTranslation()
  return (
    <div role="status" aria-label={t('common.loading')}>
      <SkeletonText lines={4} />
    </div>
  )
}

function MacroError({ onRetry }: { onRetry: () => void }) {
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

export function MacroStrip() {
  const { t } = useTranslation()
  const query = useQuery({
    queryKey: marketQueryKeys.macro(),
    queryFn: ({ signal }) => fetchMarketMacro(signal),
  })

  const byCode = new Map((query.data?.series ?? []).map((entry) => [entry.code, entry]))
  const official = byCode.get(OFFICIAL_CODE)
  const independent = byCode.get(INDEPENDENT_CODE)
  const remaining = REMAINING_ORDER.map((code) => byCode.get(code)).filter(
    (entry): entry is MacroSeries => entry !== undefined,
  )

  return (
    <section
      data-testid="market-block-macro"
      aria-label={t('marketOverview.macro.title')}
      className="flex flex-col gap-3 rounded-lg border bg-background p-4"
    >
      <h2 className="text-h3 font-semibold tracking-tight">
        {t('marketOverview.macro.title')}
      </h2>
      {query.isPending ? <MacroSkeleton /> : null}
      {query.isError ? <MacroError onRetry={() => void query.refetch()} /> : null}
      {query.isSuccess ? (
        <div className="flex flex-col gap-4">
          <InflationPair official={official} independent={independent} />
          <dl className="grid gap-3 sm:grid-cols-2">
            {remaining.map((series) => (
              <MacroIndicator key={series.code} series={series} />
            ))}
          </dl>
          <p
            data-testid="macro-eod-note"
            className="text-xs text-muted-foreground"
          >
            {t('marketOverview.macro.eodNote')}
          </p>
        </div>
      ) : null}
    </section>
  )
}
