import { useState } from 'react'

import type { StatementBasis } from '@/screens/stock-page/statementsView'

/**
 * The SCR-005 statements period toggle's session state (TKT-foundation-095).
 *
 * The spec's Yıllık | Çeyreklik toggle is **UI state that survives navigation**
 * (UXR-G-021, the same rule the active section follows): it lives in
 * `sessionStorage`, so leaving the stock page for the DCF hand-off and coming back
 * keeps the basis the user chose, and a new tab starts at the default (Yıllık).
 * It is deliberately not server state — the payload serves both period types.
 */
const STORAGE_KEY = 'degerli.statements.basis'

/** The default basis the spec lists first. */
export const DEFAULT_STATEMENT_BASIS: StatementBasis = 'annual'

function isBasis(value: unknown): value is StatementBasis {
  return value === 'annual' || value === 'quarterly'
}

/** The persisted basis, or the default when nothing (valid) is stored. */
export function readStatementBasis(): StatementBasis {
  if (typeof window === 'undefined') return DEFAULT_STATEMENT_BASIS
  try {
    const stored = window.sessionStorage.getItem(STORAGE_KEY)
    return isBasis(stored) ? stored : DEFAULT_STATEMENT_BASIS
  } catch {
    return DEFAULT_STATEMENT_BASIS
  }
}

/** The basis plus its setter, persisted for the tab session. */
export function useStatementBasis(): [StatementBasis, (next: StatementBasis) => void] {
  const [basis, setBasis] = useState<StatementBasis>(readStatementBasis)

  const update = (next: StatementBasis) => {
    setBasis(next)
    try {
      window.sessionStorage.setItem(STORAGE_KEY, next)
    } catch {
      // A storage-restricted tab keeps the toggle in memory; never a crash.
    }
  }

  return [basis, update]
}
