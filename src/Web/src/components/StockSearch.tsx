import { useTranslation } from 'react-i18next'

import { useDraftStore } from '@/store/draftStore'

/**
 * Global header stock search placeholder (FR-RES-004; UX `00-screen-inventory.md` §3).
 *
 * This scaffold ships the input and its draft-store wiring (search term survives
 * navigation, UXR-G-021). The results/landing behavior is wired by the
 * stock-research tickets (TKT-res-007) against the route contracts here.
 */
export function StockSearch() {
  const { t } = useTranslation()
  const headerSearch = useDraftStore((state) => state.headerSearch)
  const setHeaderSearch = useDraftStore((state) => state.setHeaderSearch)

  return (
    <div className="relative w-full sm:max-w-xs">
      <label htmlFor="header-stock-search" className="sr-only">
        {t('header.searchLabel')}
      </label>
      <input
        id="header-stock-search"
        type="search"
        value={headerSearch}
        placeholder={t('header.searchPlaceholder')}
        onChange={(event) => setHeaderSearch(event.target.value)}
        className="h-9 w-full rounded-md border bg-background px-3 text-sm outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px]"
      />
    </div>
  )
}
