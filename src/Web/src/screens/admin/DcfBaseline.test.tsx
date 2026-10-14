import { describe, expect, it, beforeEach } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import {
  dcfBaselineRegenerateHandler,
  ingestRunsHandlers,
} from '@/test/msw/handlers'
import { resetAdminTest, renderAdmin } from '@/screens/admin/testSupport'

/**
 * TC-VAL-016 (L3 translation of the L4 browser case) — the DCF baseline
 * regeneration action and its confirmation (UXR-MDF-025, UC-VAL-003).
 *
 * The action is consequential: it must run behind a confirmation stating that
 * baselines are rebuilt from canonical facts and versioned. Cancel aborts with no
 * run; on confirm the run becomes visible in the run ledger.
 */

const tr = {
  sectionDcfBaselines: catalogValue('tr', 'admin.sections.dcfBaselines'),
  sectionLedger: catalogValue('tr', 'admin.sections.ledger'),
  explanation: catalogValue('tr', 'admin.dcfBaselines.explanation'),
  regenerate: catalogValue('tr', 'admin.dcfBaselines.regenerate'),
  confirmTitle: catalogValue('tr', 'admin.dcfBaselines.confirmTitle'),
  confirmDescription: catalogValue('tr', 'admin.dcfBaselines.confirmDescription'),
  confirm: catalogValue('tr', 'admin.dcfBaselines.confirm'),
  cancel: catalogValue('tr', 'admin.dcfBaselines.cancel'),
}

describe('TC-VAL-016 — DCF baseline regeneration confirmation', () => {
  let regenerateCalls = 0
  let ledger: ReturnType<typeof ingestRunsHandlers>

  beforeEach(() => {
    resetAdminTest()
    regenerateCalls = 0
    ledger = ingestRunsHandlers()
    server.use(
      ...ledger.handlers,
      dcfBaselineRegenerateHandler(() => {
        regenerateCalls += 1
        ledger.addRun({
          id: 500 + regenerateCalls,
          job: 'dcf-baselines',
          status: 'succeeded',
          startedAt: '2026-10-06T22:00:00Z',
          finishedAt: '2026-10-06T22:00:05Z',
        })
      }),
    )
  })

  it('requires a confirmation stating rebuild-from-canonical-facts + versioning, and cancel aborts (UXR-MDF-025)', async () => {
    const user = userEvent.setup()
    renderAdmin()

    await user.click(await screen.findByRole('button', { name: tr.sectionDcfBaselines }))
    expect(await screen.findByText(tr.explanation)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: tr.regenerate }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('heading', { name: tr.confirmTitle })).toBeInTheDocument()
    // The confirmation states rebuild-from-canonical-facts + versioning.
    expect(within(dialog).getByText(tr.confirmDescription)).toBeInTheDocument()

    // Cancel aborts: no run is triggered.
    await user.click(within(dialog).getByRole('button', { name: tr.cancel }))
    expect(regenerateCalls).toBe(0)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('on confirm the run is visible in the ledger (UXR-MDF-025)', async () => {
    const user = userEvent.setup()
    renderAdmin()

    await user.click(await screen.findByRole('button', { name: tr.sectionDcfBaselines }))
    await user.click(screen.getByRole('button', { name: tr.regenerate }))

    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: tr.confirm }))

    expect(regenerateCalls).toBe(1)
    expect(await screen.findByTestId('dcf-baselines-success')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).toBeNull()

    // The regeneration run is visible in the run ledger.
    await user.click(screen.getByRole('button', { name: tr.sectionLedger }))
    const table = await screen.findByTestId('ledger-table')
    expect(
      within(table).getByRole('row', { name: /dcf-baselines/ }),
    ).toBeInTheDocument()
  })
})
