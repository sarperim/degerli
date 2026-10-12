import { create } from 'zustand'
import { createJSONStorage, persist } from 'zustand/middleware'

import type { CoverageScope } from '@/lib/api/contracts'
import type { AdminSectionId } from '@/screens/admin/copy'

/**
 * SCR-012 in-session UI state (UXR-G-021): the active section, quarantine status
 * filter, coverage scope and run-ledger filters are preserved across in-session
 * navigation. Persisted to sessionStorage so it survives route unmounts within
 * the tab session — the same M-3 mechanism as the shared draft store, but scoped
 * to the admin surface so sibling admin sections (RES/SCR/VAL) never touch it.
 */

export type QuarantineStatus = 'open' | 'dismissed'

export interface AdminUiState {
  activeSection: AdminSectionId
  quarantineStatus: QuarantineStatus
  coverageScope: CoverageScope
  /** Run-ledger job filter (null = all jobs). */
  ledgerJob: string | null
  /** Run-ledger status filter (null = all statuses). */
  ledgerStatus: string | null

  setActiveSection: (section: AdminSectionId) => void
  setQuarantineStatus: (status: QuarantineStatus) => void
  setCoverageScope: (scope: CoverageScope) => void
  setLedgerFilters: (filters: { job?: string | null; status?: string | null }) => void
  resetAdminUi: () => void
}

const initialAdminUiState = {
  activeSection: 'summary' as AdminSectionId,
  quarantineStatus: 'open' as QuarantineStatus,
  coverageScope: 'instrument' as CoverageScope,
  ledgerJob: null,
  ledgerStatus: null,
}

export const useAdminUiStore = create<AdminUiState>()(
  persist(
    (set) => ({
      ...initialAdminUiState,
      setActiveSection: (activeSection) => set({ activeSection }),
      setQuarantineStatus: (quarantineStatus) => set({ quarantineStatus }),
      setCoverageScope: (coverageScope) => set({ coverageScope }),
      setLedgerFilters: ({ job, status }) =>
        set((state) => ({
          ledgerJob: job === undefined ? state.ledgerJob : job,
          ledgerStatus: status === undefined ? state.ledgerStatus : status,
        })),
      resetAdminUi: () => set({ ...initialAdminUiState }),
    }),
    {
      name: 'degerli-admin-ui',
      storage: createJSONStorage(() => sessionStorage),
    },
  ),
)

/** Reset the admin UI state (tests, and a fresh session). */
export function resetAdminUiState(): void {
  useAdminUiStore.getState().resetAdminUi()
}
