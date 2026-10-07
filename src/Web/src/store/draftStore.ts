import { create } from 'zustand'
import { createJSONStorage, persist } from 'zustand/middleware'

/**
 * M-3 draft store skeleton (architecture §8.1; AD-09).
 *
 * Holds user-entered/selected state that must survive in-session navigation and
 * the auth hop (UXR-G-021/024): screener criteria, DCF assumptions, list
 * filters/search, the active stock-page section, and in-progress save names.
 *
 * The slices are intentionally empty scaffolds — domain tickets (SCR/VAL/RES)
 * own their concrete shapes and extend the corresponding slice. The store is
 * persisted to sessionStorage so drafts are scoped to the tab session.
 */

export type StockPageSection =
  | 'overview'
  | 'valuation'
  | 'financials'
  | 'profitability'
  | 'growth'
  | 'balanceSheet'
  | 'dividends'

export interface DraftState {
  /** SCR-003 criteria builder draft (owned by the stock-screening tickets). */
  screenerDraft: unknown | null
  /** SCR-003 unsaved screen name. */
  screenerDraftName: string
  /** SCR-006 DCF assumptions draft (owned by the valuation tickets). */
  dcfDraft: unknown | null
  /** SCR-006 unsaved scenario name. */
  dcfDraftName: string
  /** SCR-002 list filters/search. */
  stockListFilters: {
    sector: string | null
    query: string
  }
  /** SCR-005 active content section. */
  activeStockSection: StockPageSection
  /** Global header search term, preserved across navigation. */
  headerSearch: string
  /** Route to return to after an auth hop (?returnUrl), UXR-G-024. */
  returnUrl: string | null

  setScreenerDraft: (draft: unknown | null) => void
  setScreenerDraftName: (name: string) => void
  setDcfDraft: (draft: unknown | null) => void
  setDcfDraftName: (name: string) => void
  setStockListFilters: (filters: Partial<DraftState['stockListFilters']>) => void
  setActiveStockSection: (section: StockPageSection) => void
  setHeaderSearch: (term: string) => void
  setReturnUrl: (url: string | null) => void
  resetDrafts: () => void
}

const initialDraftState = {
  screenerDraft: null,
  screenerDraftName: '',
  dcfDraft: null,
  dcfDraftName: '',
  stockListFilters: { sector: null, query: '' },
  activeStockSection: 'overview' as StockPageSection,
  headerSearch: '',
  returnUrl: null,
}

export const useDraftStore = create<DraftState>()(
  persist(
    (set) => ({
      ...initialDraftState,
      setScreenerDraft: (draft) => set({ screenerDraft: draft }),
      setScreenerDraftName: (screenerDraftName) => set({ screenerDraftName }),
      setDcfDraft: (draft) => set({ dcfDraft: draft }),
      setDcfDraftName: (dcfDraftName) => set({ dcfDraftName }),
      setStockListFilters: (filters) =>
        set((state) => ({
          stockListFilters: { ...state.stockListFilters, ...filters },
        })),
      setActiveStockSection: (activeStockSection) => set({ activeStockSection }),
      setHeaderSearch: (headerSearch) => set({ headerSearch }),
      setReturnUrl: (returnUrl) => set({ returnUrl }),
      resetDrafts: () => set({ ...initialDraftState }),
    }),
    {
      name: 'degerli.drafts',
      storage: createJSONStorage(() => sessionStorage),
    },
  ),
)
