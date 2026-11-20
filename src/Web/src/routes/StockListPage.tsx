import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { SearchX } from 'lucide-react'

import { AsOfDate, StaleBadge } from '@/components/honest-data'
import { Button, buttonVariants } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'
import { SkeletonTable } from '@/components/ui/skeleton'
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
import { fetchStockList, distinctSectors, stockQueryKeys } from '@/lib/api/stocks'
import { useDraftStore } from '@/store/draftStore'

/**
 * SCR-002 — Stock List / Universe Browser (UX `stock-research.md` §SCR-002;
 * FR-RES-001..003, UC-RES-001).
 *
 * Browses the covered universe with name, code and sector; a sector filter and a
 * name/code search apply together and are shown as active. Rows activate the
 * stock page. The active filter/search is UI state held in the M-3 draft store
 * (architecture §8.1) so it is preserved across in-navigation (UXR-G-021) and can
 * be pre-filled from URL params: a dashboard sector link (`?sector=`) or the
 * header search's "view all matches" (`?q=`) (UXR-RES-006/007). The global header
 * search widget itself is the shared-shell component; this route is its landing.
 */
export function StockListPage() {
  const { t, i18n } = useTranslation()
  const lang = i18n.resolvedLanguage ?? 'tr'
  const [searchParams, setSearchParams] = useSearchParams()

  // Initial state comes from the M-3 draft store so the active filter/search
  // survives in-session navigation (UXR-G-021).
  const storedFilters = useRef(useDraftStore.getState().stockListFilters).current
  const [sector, setSector] = useState<string | null>(storedFilters.sector)
  const [query, setQuery] = useState(storedFilters.query)
  const setStockListFilters = useDraftStore((state) => state.setStockListFilters)

  // An arriving URL param is an explicit new intent (dashboard sector link
  // `?sector=`, header search `?q=`): it wins and the *other* dimension starts
  // empty, so a stale stored search can never narrow a freshly-linked sector (and
  // vice versa). With no params, the stored state is left untouched (preserved).
  const paramsKey = searchParams.toString()
  useEffect(() => {
    const params = new URLSearchParams(paramsKey)
    if (!params.has('sector') && !params.has('q')) return
    setSector(params.get('sector'))
    setQuery(params.get('q') ?? '')
  }, [paramsKey])

  // Keep the M-3 draft store authoritative so the state survives navigation.
  useEffect(() => {
    setStockListFilters({ sector, query })
  }, [sector, query, setStockListFilters])

  const listQuery = useQuery({
    queryKey: stockQueryKeys.list({ sector, query }),
    queryFn: ({ signal }) => fetchStockList({ sector, query }, signal),
  })

  // A stable, unfiltered read of the universe supplies the sector-filter options
  // (they must not shrink as the active filter narrows the visible rows).
  const universeQuery = useQuery({
    queryKey: stockQueryKeys.universe(),
    queryFn: ({ signal }) => fetchStockList({}, signal),
  })

  const sectorOptions = distinctSectors(universeQuery.data?.stocks ?? [])
  const hasActiveFilter = sector !== null || query.trim() !== ''

  const clearFilters = () => {
    setSector(null)
    setQuery('')
    if (searchParams.has('sector') || searchParams.has('q')) {
      setSearchParams({}, { replace: true })
    }
  }

  return (
    <section
      data-screen="SCR-002"
      data-route-id="stockList"
      className="flex flex-col gap-6"
    >
      <header className="flex flex-wrap items-end justify-between gap-4 border-b border-border pb-5">
        <div className="flex flex-col gap-1.5">
          <span
            lang={lang}
            className="font-mono text-[11px] font-medium uppercase tracking-[0.09em] text-[var(--stale-foreground)]"
          >
            {t('stockList.kicker')}
          </span>
          <h1 className="text-h1 font-semibold tracking-tight text-foreground">
            {t('screens.stockList.title')}
          </h1>
          <p className="max-w-2xl text-sm text-muted-foreground">
            {t('screens.stockList.description')}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          {listQuery.isSuccess ? (
            <>
              <AsOfDate asOf={listQuery.data.asOf} />
              {listQuery.data.stale ? <StaleBadge /> : null}
            </>
          ) : null}
          <Link
            to="/"
            className={buttonVariants({ variant: 'outline', size: 'sm' })}
          >
            <span aria-hidden="true">←</span>
            {t('stockList.backToOverview')}
          </Link>
        </div>
      </header>

      <div className="flex flex-wrap items-end justify-between gap-4 border-b border-border pb-5">
        <div className="flex w-full flex-wrap items-end gap-4 sm:w-auto">
          <div className="flex w-full max-w-[280px] flex-col gap-1.5 sm:w-[280px]">
            <label
              htmlFor="stock-list-search"
              lang={lang}
              className="font-mono text-[10px] font-medium uppercase tracking-[0.06em] text-muted-foreground"
            >
              {t('stockList.searchLabel')}
            </label>
            <Input
              id="stock-list-search"
              type="search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder={t('stockList.searchPlaceholder')}
              className="max-w-[280px] font-mono text-xs"
            />
          </div>

          <div className="flex w-full flex-col gap-1.5 sm:w-auto">
            <label
              htmlFor="stock-list-sector"
              lang={lang}
              className="font-mono text-[10px] font-medium uppercase tracking-[0.06em] text-muted-foreground"
            >
              {t('stockList.sectorLabel')}
            </label>
            <select
              id="stock-list-sector"
              value={sector ?? ''}
              onChange={(event) => setSector(event.target.value || null)}
              className="h-9 w-full min-w-[180px] rounded-[2px] border border-[var(--border-strong)] bg-card px-3 font-mono text-xs text-foreground outline-none transition-[color,border-color,box-shadow] focus-visible:border-[var(--brand-accent)] focus-visible:ring-ring/40 focus-visible:ring-[2px] sm:w-auto"
            >
              <option value="">{t('stockList.sectorAll')}</option>
              {sectorOptions.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </div>
        </div>

        {hasActiveFilter ? (
          <div
            data-testid="active-filters"
            aria-label={t('stockList.activeFiltersLabel')}
            className="flex flex-wrap items-center gap-2"
          >
            {sector !== null ? (
              <Badge
                data-testid="active-sector"
                variant="secondary"
                className="font-mono text-xs"
              >
                {t('stockList.activeSector', { sector })}
              </Badge>
            ) : null}
            {query.trim() !== '' ? (
              <Badge
                data-testid="active-search"
                variant="secondary"
                className="font-mono text-xs"
              >
                {t('stockList.activeSearch', { query })}
              </Badge>
            ) : null}
            <Button
              variant="link"
              size="sm"
              onClick={clearFilters}
              className="h-auto px-1 font-mono text-xs"
            >
              {t('stockList.clearFilters')}
            </Button>
          </div>
        ) : null}

        {sectorOptions.length > 0 ? (
          <div
            data-slot="sector-chip-row"
            className="flex w-full flex-wrap items-center gap-1.5 pt-1"
          >
            <button
              type="button"
              data-magnetic=""
              aria-pressed={sector === null}
              onClick={() => setSector(null)}
              className={
                sector === null
                  ? 'rounded-[2px] border border-[var(--brand-accent)] bg-[var(--accent-soft)] px-2.5 py-1 font-mono text-[11px] font-medium text-foreground transition-colors'
                  : 'rounded-[2px] border border-[var(--border-strong)] bg-transparent px-2.5 py-1 font-mono text-[11px] text-muted-foreground transition-colors hover:border-foreground hover:text-foreground'
              }
            >
              {t('stockList.sectorAll')}
            </button>
            {sectorOptions.map((option) => {
              const active = sector === option
              return (
                <button
                  key={option}
                  type="button"
                  data-magnetic=""
                  aria-pressed={active}
                  onClick={() => setSector(active ? null : option)}
                  className={
                    active
                      ? 'rounded-[2px] border border-[var(--brand-accent)] bg-[var(--accent-soft)] px-2.5 py-1 font-mono text-[11px] font-medium text-foreground transition-colors'
                      : 'rounded-[2px] border border-[var(--border-strong)] bg-transparent px-2.5 py-1 font-mono text-[11px] text-muted-foreground transition-colors hover:border-foreground hover:text-foreground'
                  }
                >
                  {option}
                </button>
              )
            })}
          </div>
        ) : null}
      </div>

      {listQuery.isPending ? (
        <div role="status" aria-label={t('common.loading')}>
          <SkeletonTable rows={6} columns={3} />
        </div>
      ) : null}

      {listQuery.isError ? (
        <div
          role="alert"
          className="flex flex-col items-center gap-3 border-t border-border px-0 py-10 text-center"
        >
          <p className="text-h3 font-medium text-foreground">
            {t('stockList.errorTitle')}
          </p>
          <p className="max-w-prose text-sm text-muted-foreground">
            {t('stockList.errorBody')}
          </p>
          <Button variant="outline" onClick={() => void listQuery.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      ) : null}

      {listQuery.isSuccess && listQuery.data.stocks.length === 0 ? (
        <EmptyState
          data-testid="no-match-state"
          className="rounded-none border-0 border-t border-border px-0 py-10"
          icon={<SearchX />}
          title={
            hasActiveFilter
              ? t('stockList.emptyTitle')
              : t('stockList.emptyUniverseTitle')
          }
          description={
            hasActiveFilter
              ? t('stockList.emptyBody')
              : t('stockList.emptyUniverseBody')
          }
          action={
            hasActiveFilter ? (
              <Button variant="outline" onClick={clearFilters}>
                {t('stockList.clearFilters')}
              </Button>
            ) : undefined
          }
        />
      ) : null}

      {listQuery.isSuccess && listQuery.data.stocks.length > 0 ? (
        <TableContainer className="rounded-none border-0">
          <Table>
            <TableCaption className="px-0">
              {t('stockList.resultsCount', {
                count: listQuery.data.stocks.length,
              })}
            </TableCaption>
            <TableHeader className="bg-transparent">
              <TableRow className="hover:bg-transparent">
                <TableHead className="pl-0">{t('stockList.columns.symbol')}</TableHead>
                <TableHead>{t('stockList.columns.name')}</TableHead>
                <TableHead className="pr-0">{t('stockList.columns.sector')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {listQuery.data.stocks.map((stock) => (
                <TableRow key={stock.symbol} data-testid={`stock-row-${stock.symbol}`}>
                  <TableCell className="pl-0 font-mono tabular-nums">
                    <Link
                      to={`/stocks/${stock.symbol}`}
                      className="font-mono font-semibold text-foreground underline-offset-4 hover:text-[var(--stale-foreground)] hover:underline focus-visible:underline"
                    >
                      {stock.symbol}
                    </Link>
                  </TableCell>
                  <TableHead
                    scope="row"
                    className="font-medium text-foreground"
                  >
                    <Link
                      to={`/stocks/${stock.symbol}`}
                      aria-label={`${stock.name} (${stock.symbol})`}
                      className="underline-offset-4 hover:underline focus-visible:underline"
                    >
                      {stock.name}
                    </Link>
                  </TableHead>
                  <TableCell className="pr-0 text-muted-foreground">
                    {stock.sector ?? t('stockList.unclassified')}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      ) : null}
    </section>
  )
}
