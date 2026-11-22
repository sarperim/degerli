import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router'

import { fetchStockSearch, stockQueryKeys } from '@/lib/api/stocks'
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue'
import { cn } from '@/lib/utils'
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
  const [activeIndex, setActiveIndex] = useState(-1)
  const containerRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

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
        setActiveIndex(-1)
      }
    }
    document.addEventListener('mousedown', handlePointerDown)
    return () => document.removeEventListener('mousedown', handlePointerDown)
  }, [])

  // Global `/` shortcut to focus search when not already typing in a form field.
  useEffect(() => {
    function handleGlobalKeyDown(event: globalThis.KeyboardEvent) {
      if (
        event.defaultPrevented ||
        event.isComposing ||
        event.key !== '/' ||
        event.metaKey ||
        event.ctrlKey ||
        event.altKey
      ) {
        return
      }
      if (document.querySelector('[role="dialog"][aria-modal="true"]')) {
        return
      }
      const target = event.target as HTMLElement | null
      if (
        target &&
        (target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.tagName === 'SELECT' ||
          target.isContentEditable)
      ) {
        return
      }
      event.preventDefault()
      inputRef.current?.focus()
    }
    document.addEventListener('keydown', handleGlobalKeyDown)
    return () => document.removeEventListener('keydown', handleGlobalKeyDown)
  }, [])

  const showPanel = open && term.length > 0
  const matches = searchQuery.data?.stocks ?? []
  const highlightedIndex =
    activeIndex >= 0 && activeIndex < matches.length ? activeIndex : -1
  const activeOptionId =
    showPanel && highlightedIndex >= 0
      ? `header-stock-search-option-${matches[highlightedIndex].symbol}`
      : undefined

  const openStock = (symbol: string) => {
    setOpen(false)
    setActiveIndex(-1)
    navigate(`/stocks/${symbol}`)
  }

  const viewAllMatches = () => {
    setOpen(false)
    setActiveIndex(-1)
    navigate(`/stocks?q=${encodeURIComponent(debouncedTerm)}`)
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') {
      if (open) {
        event.preventDefault()
        setOpen(false)
        setActiveIndex(-1)
      }
      return
    }

    if (event.key === 'ArrowDown') {
      event.preventDefault()
      if (!open && term.length > 0) {
        setOpen(true)
        return
      }
      if (matches.length > 0) {
        setActiveIndex(
          highlightedIndex < matches.length - 1 ? highlightedIndex + 1 : 0,
        )
      }
      return
    }

    if (event.key === 'ArrowUp') {
      if (!showPanel || matches.length === 0) return
      event.preventDefault()
      setActiveIndex(
        highlightedIndex <= 0 ? matches.length - 1 : highlightedIndex - 1,
      )
      return
    }

    if (
      event.key === 'Enter' &&
      showPanel &&
      term === debouncedTerm &&
      matches.length > 0
    ) {
      event.preventDefault()
      const target = matches[highlightedIndex >= 0 ? highlightedIndex : 0]
      if (target) openStock(target.symbol)
    }
  }

  return (
    <div
      ref={containerRef}
      className="relative w-full shrink-0 transition-[width] duration-150 motion-reduce:transition-none sm:w-44 2xl:w-48 sm:focus-within:w-56"
    >
      <label htmlFor="header-stock-search" className="sr-only">
        {t('header.searchLabel')}
      </label>
      <input
        ref={inputRef}
        id="header-stock-search"
        type="search"
        role="combobox"
        aria-expanded={showPanel}
        aria-controls="header-stock-search-results"
        aria-autocomplete="list"
        aria-activedescendant={activeOptionId}
        autoComplete="off"
        value={headerSearch}
        placeholder={t('header.searchPlaceholder')}
        onChange={(event) => {
          setHeaderSearch(event.target.value)
          setOpen(true)
          setActiveIndex(-1)
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={handleKeyDown}
        className="h-[30px] w-full rounded-[2px] border border-border bg-card pr-6 pl-2.5 font-mono text-xs text-foreground outline-none placeholder:text-muted-foreground focus:border-[var(--brand-accent)] focus-visible:ring-1 focus-visible:ring-[var(--brand-accent)]"
      />
      {term.length === 0 ? (
        <kbd
          aria-hidden="true"
          className="pointer-events-none absolute top-1/2 right-2 hidden -translate-y-1/2 font-mono text-[10px] text-muted-foreground sm:inline"
        >
          /
        </kbd>
      ) : null}

      {showPanel ? (
        <div
          id="header-stock-search-results"
          role="listbox"
          aria-label={t('headerSearch.resultsLabel')}
          className="absolute z-50 mt-1 flex w-full flex-col overflow-hidden rounded-[2px] border border-[var(--border-strong)] bg-popover text-popover-foreground shadow-md"
        >
          {searchQuery.isPending ? (
            <p className="px-2.5 py-2 font-mono text-[11px] text-muted-foreground">
              {t('common.loading')}
            </p>
          ) : null}

          {searchQuery.isError ? (
            <p role="alert" className="px-2.5 py-2 font-mono text-[11px] text-destructive">
              {t('common.error')}
            </p>
          ) : null}

          {searchQuery.isSuccess && matches.length === 0 ? (
            <p className="px-2.5 py-2 font-mono text-[11px] text-muted-foreground">
              {t('headerSearch.noMatches')}
            </p>
          ) : null}

          {matches.map((match, index) => (
            <button
              key={match.symbol}
              id={`header-stock-search-option-${match.symbol}`}
              type="button"
              role="option"
              aria-selected={index === highlightedIndex}
              aria-label={`${match.name} (${match.symbol})`}
              onClick={() => openStock(match.symbol)}
              className={cn(
                'flex w-full items-center justify-between gap-2 border-b border-border px-2.5 py-2 text-left text-xs last:border-b-0 hover:bg-accent focus-visible:bg-accent focus-visible:outline-none',
                index === highlightedIndex && 'bg-accent',
              )}
            >
              <span className="truncate">{match.name}</span>
              <span className="shrink-0 font-mono text-[11px] text-muted-foreground">
                {match.symbol}
              </span>
            </button>
          ))}

          {searchQuery.isSuccess && matches.length > 0 ? (
            <button
              type="button"
              onClick={viewAllMatches}
              className="border-t border-border bg-secondary/70 px-2.5 py-2 text-left font-mono text-[11px] font-medium text-stale-foreground hover:bg-accent focus-visible:bg-accent focus-visible:outline-none"
            >
              {t('headerSearch.viewAll')}
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  )
}
