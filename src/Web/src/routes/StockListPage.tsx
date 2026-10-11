import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { SearchX } from 'lucide-react'

import { AsOfDate, StaleBadge } from '@/components/honest-data'
import { Button } from '@/components/ui/button'
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
  const { t } = useTranslation()
  const [searchParams] = useSearchParams()

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
  }

  return (
    <section
      data-screen="SCR-002"
      data-route-id="stockList"
      className="flex flex-col gap-6"
    >
      <header className="flex flex-col gap-2">
        <h1 className="text-h1 font-semibold tracking-tight">
          {t('screens.stockList.title')}
        </h1>
        <p className="max-w-2xl text-muted-foreground">
          {t('screens.stockList.description')}
        </p>
      </header>

      <div className="flex flex-col gap-4 rounded-lg border bg-background p-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <label
              htmlFor="stock-list-search"
              className="text-sm font-medium text-foreground"
            >
              {t('stockList.searchLabel')}
            </label>
            <Input
              id="stock-list-search"
              type="search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder={t('stockList.searchPlaceholder')}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <label
              htmlFor="stock-list-sector"
              className="text-sm font-medium text-foreground"
            >
              {t('stockList.sectorLabel')}
            </label>
            <select
              id="stock-list-sector"
              value={sector ?? ''}
              onChange={(event) => setSector(event.target.value || null)}
              className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px]"
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
              <Badge data-testid="active-sector" variant="secondary">
                {t('stockList.activeSector', { sector })}
              </Badge>
            ) : null}
            {query.trim() !== '' ? (
              <Badge data-testid="active-search" variant="secondary">
                {t('stockList.activeSearch', { query })}
              </Badge>
            ) : null}
            <Button variant="link" size="sm" onClick={clearFilters}>
              {t('stockList.clearFilters')}
            </Button>
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
          className="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-10 text-center"
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
        <div className="flex flex-col gap-3">
          <TableContainer>
            <Table>
              <TableCaption>
                {t('stockList.resultsCount', {
                  count: listQuery.data.stocks.length,
                })}
              </TableCaption>
              <TableHeader>
                <TableRow>
                  <TableHead>{t('stockList.columns.name')}</TableHead>
                  <TableHead>{t('stockList.columns.symbol')}</TableHead>
                  <TableHead>{t('stockList.columns.sector')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {listQuery.data.stocks.map((stock) => (
                  <TableRow key={stock.symbol} data-testid={`stock-row-${stock.symbol}`}>
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
                    <TableCell className="font-mono tabular-nums">
                      {stock.symbol}
                    </TableCell>
                    <TableCell>
                      {stock.sector ?? t('stockList.unclassified')}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
          <div className="flex flex-wrap items-center gap-3">
            <AsOfDate asOf={listQuery.data.asOf} />
            {listQuery.data.stale ? <StaleBadge /> : null}
          </div>
        </div>
      ) : null}
    </section>
  )
}
