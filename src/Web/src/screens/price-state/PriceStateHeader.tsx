import { useTranslation } from 'react-i18next'
import { ArrowDown, ArrowUp } from 'lucide-react'

import { AsOfDate, StaleBadge } from '@/components/honest-data'
import type { PriceState } from '@/lib/api/contracts'
import { formatNumber, formatSignedPercent } from '@/lib/format'
import { cn } from '@/lib/utils'
import { formatSessionDate, formatUpdateTime } from '@/screens/price-state/priceState'

export interface PriceStateHeaderProps {
  /** The server's price state; absent when the deployment has no intraday lane. */
  priceState?: PriceState | null
  /** The envelope's data-as-of date (the EOD fallback's only honest display). */
  asOf: string
  /** The envelope's freshness flag (`stale` = a newer completed session exists). */
  stale?: boolean
  /** Instrument symbol, shown when the surface is a stock page (SCR-005). */
  symbol?: string
  /**
   * Render the EOD envelope (as-of date + stale flag, no price) when `priceState`
   * is absent. Used where the header is the surface's only price display; the
   * SCR-001 page header already renders that envelope itself and passes `false`.
   */
  showEodFallback?: boolean
  /** Suppress the stale badge where a sibling already states the same envelope. */
  showStale?: boolean
  className?: string
}

/**
 * The shared intraday/close price header for SCR-001 and SCR-005
 * (TKT-foundation-049; the frozen contract of TKT-foundation-047 item 4).
 *
 * The component is a **renderer only**: `state`, `lastPrice`, `changePct`, `asOf`
 * and `sessionDate` come from the server verbatim. It never computes the change,
 * never freezes a value at 18:00 and never relabels an intraday refresh as a
 * close — the close in formation stays "last update HH:MM — not final", which is
 * the honest statement (architecture §10.9.x, planner-pinned). A payload without
 * `priceState` degrades to the existing 037/039 EOD display (as-of date + stale
 * flag) instead of showing an inferred or fabricated price.
 *
 * The numerics are nullable (`01` §10.9.18): on the market overview the server
 * serves `state`/`asOf`/`sessionDate` with `lastPrice`/`prevClose`/`changePct`
 * all `null`, and on an instrument's cold start `prevClose`/`changePct` are
 * `null`. A `null` renders as **absence** — no value, no change, no direction
 * arrow — never as `0,00` / `+0,00%`.
 *
 * All copy is localized; the state and the direction are carried by text, never
 * by colour alone (UXR-G-009).
 */
export function PriceStateHeader({
  priceState,
  asOf,
  stale = false,
  symbol,
  showEodFallback = false,
  showStale = true,
  className,
}: PriceStateHeaderProps) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'

  if (!priceState) {
    if (!showEodFallback) return null
    return (
      <div
        data-testid="price-state"
        data-price-state="eod"
        role="group"
        aria-label={t('priceState.label')}
        className={cn('flex flex-wrap items-center gap-3', className)}
      >
        <AsOfDate asOf={asOf} />
        {stale && showStale ? <StaleBadge /> : null}
      </div>
    )
  }

  const intraday = priceState.state === 'intraday'
  const lastPrice = priceState.lastPrice
  const changePct = priceState.changePct
  const ChangeIcon = changePct !== null && changePct < 0 ? ArrowDown : ArrowUp
  const vsPreviousClose = t('priceState.changeVsPrevClose')
  const hasValueBlock = Boolean(symbol) || lastPrice !== null

  return (
    <div
      data-testid="price-state"
      data-price-state={priceState.state}
      role="group"
      aria-label={t('priceState.label')}
      className={cn('flex flex-col gap-1', className)}
    >
      {hasValueBlock ? (
        <div className="flex flex-wrap items-baseline gap-3">
          {symbol ? (
            <span
              data-testid="price-state-symbol"
              className="font-mono text-sm font-medium text-foreground"
            >
              {symbol}
            </span>
          ) : null}
          {lastPrice !== null ? (
            <>
              <span className="text-xs text-muted-foreground">
                {t('priceState.valueLabel')}
              </span>
              <span
                data-testid="price-state-value"
                className="text-h2 font-semibold tabular-nums"
              >
                {formatNumber(locale, lastPrice, {
                  minimumFractionDigits: 2,
                  maximumFractionDigits: 2,
                })}
              </span>
            </>
          ) : null}
          {/* The served change is a fraction vs the previous session's close; the
              direction is an arrow + an explicit sign, never colour alone. A null
              change (cold start) is omitted entirely — never rendered as +0,00%. */}
          {lastPrice !== null && changePct !== null ? (
            <span
              data-testid="price-state-change"
              title={vsPreviousClose}
              className="inline-flex items-center gap-1 tabular-nums"
            >
              <ChangeIcon aria-hidden="true" className="size-3.5 shrink-0" />
              {formatSignedPercent(locale, changePct)}
              <span className="sr-only"> ({vsPreviousClose})</span>
            </span>
          ) : null}
        </div>
      ) : null}
      <div className="flex flex-wrap items-center gap-3">
        {intraday ? (
          <p data-testid="price-state-asof" className="text-xs text-muted-foreground">
            {t('priceState.intraday.label', {
              time: formatUpdateTime(locale, priceState.asOf),
            })}
          </p>
        ) : (
          <p
            data-testid="price-state-session-date"
            className="text-xs text-muted-foreground"
          >
            {t('priceState.close.label', {
              date: formatSessionDate(locale, priceState.sessionDate),
            })}
          </p>
        )}
        {stale && showStale ? <StaleBadge /> : null}
      </div>
    </div>
  )
}
