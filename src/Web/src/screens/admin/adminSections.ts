import type { ComponentType } from 'react'

import { CoverageSection } from '@/screens/admin/CoverageSection'
import { MetricVisibilitySection } from '@/screens/admin/MetricVisibilitySection'
import { OpsSummarySection } from '@/screens/admin/OpsSummarySection'
import { QuarantineSection } from '@/screens/admin/QuarantineSection'
import { RunLedgerSection } from '@/screens/admin/RunLedgerSection'
import { StatsSection } from '@/screens/admin/StatsSection'

/**
 * SCR-012 section registry (TKT-mdf-012).
 *
 * The admin shell renders exactly one section at a time from this map. This file
 * is the single registration point: a sibling admin ticket (RES descriptions /
 * SCR metric visibility / VAL baselines) contributes a component file plus an
 * entry in each of the two maps below (`ADMIN_SECTION_KEYS` for the nav label,
 * `ADMIN_SECTION_COMPONENTS` for the section body, keyed by the shared
 * `AdminSectionId` union) — no other shared code (e.g. `copy.ts`) changes. The
 * `Record<AdminSectionId, …>` annotations keep both maps exhaustive.
 */

export type AdminSectionId =
  | 'summary'
  | 'quarantine'
  | 'coverage'
  | 'ledger'
  | 'metrics'
  | 'stats'

export const ADMIN_SECTION_KEYS: Record<AdminSectionId, string> = {
  summary: 'admin.sections.summary',
  quarantine: 'admin.sections.quarantine',
  coverage: 'admin.sections.coverage',
  ledger: 'admin.sections.ledger',
  metrics: 'admin.sections.metrics',
  stats: 'admin.sections.stats',
}

export const ADMIN_SECTION_COMPONENTS: Record<AdminSectionId, ComponentType> = {
  summary: OpsSummarySection,
  quarantine: QuarantineSection,
  coverage: CoverageSection,
  ledger: RunLedgerSection,
  metrics: MetricVisibilitySection,
  stats: StatsSection,
}
