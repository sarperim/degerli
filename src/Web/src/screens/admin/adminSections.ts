import type { ComponentType } from 'react'

import type { AdminSectionId } from '@/screens/admin/copy'
import { CoverageSection } from '@/screens/admin/CoverageSection'
import { OpsSummarySection } from '@/screens/admin/OpsSummarySection'
import { QuarantineSection } from '@/screens/admin/QuarantineSection'
import { RunLedgerSection } from '@/screens/admin/RunLedgerSection'
import { StatsSection } from '@/screens/admin/StatsSection'

/**
 * SCR-012 section registry (TKT-mdf-012).
 *
 * The admin shell renders exactly one section at a time from this map. Each
 * section lives in its own file; sibling admin tickets (RES descriptions / SCR
 * metric visibility / VAL baselines) add their section by contributing a
 * component file plus one entry here — no other shared code changes.
 */
export const ADMIN_SECTION_COMPONENTS: Record<AdminSectionId, ComponentType> = {
  summary: OpsSummarySection,
  quarantine: QuarantineSection,
  coverage: CoverageSection,
  ledger: RunLedgerSection,
  stats: StatsSection,
}
