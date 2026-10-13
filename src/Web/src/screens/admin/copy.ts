import type { CoverageScope } from '@/lib/api/contracts'

/**
 * SCR-012 admin copy maps (TKT-mdf-012).
 *
 * The dynamic vocabularies (run statuses, quarantine reason codes, data types)
 * are kept as explicit key tables — rather than interpolated key strings — so the
 * i18n parity script sees every catalog key referenced in source
 * (`scripts/i18n-parity.mjs`), and so an unknown server value degrades to the raw
 * code instead of a missing-key placeholder.
 */

export const RUN_STATUS_KEYS: Record<string, string> = {
  succeeded: 'admin.runStatus.succeeded',
  failed: 'admin.runStatus.failed',
  partial: 'admin.runStatus.partial',
  running: 'admin.runStatus.running',
  queued: 'admin.runStatus.queued',
}

export const REASON_KEYS: Record<string, string> = {
  MISSING_PROVENANCE: 'admin.reason.MISSING_PROVENANCE',
  NEGATIVE_PRICE: 'admin.reason.NEGATIVE_PRICE',
  UNPARSEABLE_PAYLOAD: 'admin.reason.UNPARSEABLE_PAYLOAD',
  SCHEMA_MISMATCH: 'admin.reason.SCHEMA_MISMATCH',
  CONFLICTING_VALUE: 'admin.reason.CONFLICTING_VALUE',
  MISSING_CLASSIFICATION: 'admin.reason.MISSING_CLASSIFICATION',
}

export const DATA_TYPE_KEYS: Record<string, string> = {
  prices: 'admin.dataTypes.prices',
  statements: 'admin.dataTypes.statements',
  dividends: 'admin.dataTypes.dividends',
  corporate_actions: 'admin.dataTypes.corporate_actions',
  disclosures: 'admin.dataTypes.disclosures',
  nav: 'admin.dataTypes.nav',
  performance: 'admin.dataTypes.performance',
  holdings: 'admin.dataTypes.holdings',
}

/** Key for a run status, falling back to the raw code for unknown values. */
export function runStatusKey(status: string): string {
  return RUN_STATUS_KEYS[status] ?? status
}

/** Key for a quarantine reason, falling back to the raw code for unknown values. */
export function reasonKey(code: string): string {
  return REASON_KEYS[code] ?? code
}

/** Key for a data type, falling back to the raw code for unknown values. */
export function dataTypeKey(dataType: string): string {
  return DATA_TYPE_KEYS[dataType] ?? dataType
}

export const COVERAGE_SCOPE_KEYS: Record<CoverageScope, string> = {
  instrument: 'admin.coverage.scopeInstrument',
  fund: 'admin.coverage.scopeFund',
}

/**
 * The canonical calendar date of an ISO timestamp, for locale-aware date-only
 * display.
 *
 * Deliberate choice: admin ops timestamps (quarantine/ledger/summary) are shown
 * on their canonical UTC calendar day — the same day the ingestion and ledger
 * source of truth records — rather than re-projecting the instant into the
 * viewer's local timezone, which could roll a late-UTC run onto the next day.
 * The `YYYY-MM-DD` slice is then rendered locale-aware by `formatAsOfDate`.
 */
export function datePart(iso: string): string {
  return iso.slice(0, 10)
}
