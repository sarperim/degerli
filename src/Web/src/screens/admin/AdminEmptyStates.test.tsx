import { describe, expect, it, beforeEach } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import { quarantineHandlers } from '@/test/msw/handlers'
import { resetAdminTest, renderAdmin } from '@/screens/admin/testSupport'

/**
 * TC-MDF-050 (L3 translation of the L4 case) — admin empty states (UXR-MDF-027).
 *
 * Each list surface shows an explicit empty state naming its condition when it
 * legitimately has no items: the healthy no-open-quarantine state names that
 * condition; a run-ledger filter matching nothing names its condition.
 */

const tr = {
  sectionQuarantine: catalogValue('tr', 'admin.sections.quarantine'),
  sectionLedger: catalogValue('tr', 'admin.sections.ledger'),
  emptyOpenTitle: catalogValue('tr', 'admin.quarantine.emptyOpenTitle'),
  emptyOpenBody: catalogValue('tr', 'admin.quarantine.emptyOpenBody'),
  ledgerEmptyTitle: catalogValue('tr', 'admin.ledger.emptyTitle'),
  ledgerEmptyBody: catalogValue('tr', 'admin.ledger.emptyBody'),
  filterJob: catalogValue('tr', 'admin.ledger.filterJob'),
}

describe('TC-MDF-050 — admin empty states', () => {
  beforeEach(() => {
    resetAdminTest()
  })

  it('shows the healthy no-open-quarantine empty state naming the condition (UXR-MDF-027)', async () => {
    server.use(...quarantineHandlers([]).handlers)
    const user = userEvent.setup()
    renderAdmin()

    await user.click(await screen.findByRole('button', { name: tr.sectionQuarantine }))

    expect(await screen.findByText(tr.emptyOpenTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.emptyOpenBody)).toBeInTheDocument()
  })

  it('shows a named empty state when the run-ledger filter matches nothing (UXR-MDF-027)', async () => {
    const user = userEvent.setup()
    renderAdmin()

    await user.click(await screen.findByRole('button', { name: tr.sectionLedger }))
    await screen.findByTestId('ledger-table')

    await user.selectOptions(screen.getByLabelText(tr.filterJob), 'universe-sync')

    expect(await screen.findByText(tr.ledgerEmptyTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.ledgerEmptyBody)).toBeInTheDocument()
  })
})
