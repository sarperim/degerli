import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router'

import { fetchStockSearch, stockQueryKeys } from '@/lib/api/stocks'
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue'
import { useDraftStore } from '@/store/draftStore'

/**
 * Global header stock search (FR-RES-004; UX `00-screen-inventory.md` §3).
 *
 * The widget lives in the shared shell (available on every screen). Its term is
 * held in the M-3 draft store so it survives navigation (UXR-G-021). Behaviour
 * wired by this ticket (UXR-RES-007): a lightweight symbol+name typeahead over
 * `GET /api/v1/stocks/search?q=`; selecting a match opens that stock's page
 * directly, "view all matches" opens the Stock List with the query applied, and a
 * query matching nothing shows an explicit no-match state.
 */
export function StockSearch() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const headerSearch = useDraftStore((state) => state.headerSearch)
  const setHeaderSearch = useDraftStore((state) => state.setHeaderSearch)

  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  const term = headerSearch.trim()
  const debouncedTerm = useDebouncedValue(term, 250)

  const searchQuery = useQuery({
    queryKey: stockQueryKeys.search(debouncedTerm),
    queryFn: ({ signal }) => fetchStockSearch(debouncedTerm, signal),
    enabled: debouncedTerm.length > 0,
  })

  // Dismiss the results panel on an outside click (mousedown pre-empts focus loss).
  useEffect(() => {
    function handlePointerDown(event: MouseEvent) {
      if (
        containerRef.current &&
        !containerRef.current.contains(event.target as Node)
      ) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', handlePointerDown)
    return () => document.removeEventListener('mousedown', handlePointerDown)
  }, [])

  const showPanel = open && term.length > 0
  const matches = searchQuery.data?.stocks ?? []

  const openStock = (symbol: string) => {
    setOpen(false)
    navigate(`/stocks/${symbol}`)
  }

  const viewAllMatches = () => {
    setOpen(false)
    navigate(`/stocks?q=${encodeURIComponent(debouncedTerm)}`)
  }

  return (
    <div ref={containerRef} className="relative w-full sm:max-w-xs">
      <label htmlFor="header-stock-search" className="sr-only">
        {t('header.searchLabel')}
      </label>
      <input
        id="header-stock-search"
        type="search"
        role="combobox"
        aria-expanded={showPanel}
        aria-controls="header-stock-search-results"
        aria-autocomplete="list"
        autoComplete="off"
        value={headerSearch}
        placeholder={t('header.searchPlaceholder')}
        onChange={(event) => {
          setHeaderSearch(event.target.value)
          setOpen(true)
        }}
        onFocus={() => setOpen(true)}
        className="h-9 w-full rounded-md border bg-background px-3 text-sm outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px]"
      />

      {showPanel ? (
        <div
          id="header-stock-search-results"
          role="listbox"
          aria-label={t('headerSearch.resultsLabel')}
          className="absolute z-20 mt-1 flex w-full flex-col overflow-hidden rounded-md border bg-background shadow-md"
        >
          {searchQuery.isPending ? (
            <p className="px-3 py-2 text-sm text-muted-foreground">
              {t('common.loading')}
            </p>
          ) : null}

          {searchQuery.isError ? (
            <p role="alert" className="px-3 py-2 text-sm text-destructive">
              {t('common.error')}
            </p>
          ) : null}

          {searchQuery.isSuccess && matches.length === 0 ? (
            <p className="px-3 py-2 text-sm text-muted-foreground">
              {t('headerSearch.noMatches')}
            </p>
          ) : null}

          {matches.map((match) => (
            <button
              key={match.symbol}
              type="button"
              role="option"
              aria-selected={false}
              aria-label={`${match.name} (${match.symbol})`}
              onClick={() => openStock(match.symbol)}
              className="flex w-full items-center justify-between gap-3 px-3 py-2 text-left text-sm hover:bg-accent focus-visible:bg-accent focus-visible:outline-none"
            >
              <span>{match.name}</span>
              <span className="font-mono text-xs text-muted-foreground">
                {match.symbol}
              </span>
            </button>
          ))}

          {searchQuery.isSuccess && matches.length > 0 ? (
            <button
              type="button"
              onClick={viewAllMatches}
              className="border-t px-3 py-2 text-left text-sm font-medium text-primary hover:bg-accent focus-visible:bg-accent focus-visible:outline-none"
            >
              {t('headerSearch.viewAll')}
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  )
}
