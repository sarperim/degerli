import { describe, expect, it, beforeEach } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import { API_BASE, problemHandler } from '@/test/msw/handlers'
import { resetAdminTest, renderAdmin } from '@/screens/admin/testSupport'

/**
 * TC-MDF-047 (L3 translation of the L4 browser case) — the quarantine review
 * flow (UXR-MDF-007/008/009/010).
 *
 * State transition: open → inspect payload → dismiss with an empty note (inline
 * validation blocks) → dismiss with a note (item moves to dismissed) → view the
 * dismissed filter (note retained) → navigate to the job's run-ledger view
 * filtered to that job.
 */

const tr = {
  sectionSummary: catalogValue('tr', 'admin.sections.summary'),
  sectionQuarantine: catalogValue('tr', 'admin.sections.quarantine'),
  sectionLedger: catalogValue('tr', 'admin.sections.ledger'),
  filterLabel: catalogValue('tr', 'admin.quarantine.filterLabel'),
  statusOpen: catalogValue('tr', 'admin.quarantine.statusOpen'),
  statusDismissed: catalogValue('tr', 'admin.quarantine.statusDismissed'),
  job: catalogValue('tr', 'admin.quarantine.columns.job'),
  reason: catalogValue('tr', 'admin.quarantine.columns.reason'),
  source: catalogValue('tr', 'admin.quarantine.columns.source'),
  inspect: catalogValue('tr', 'admin.quarantine.inspect'),
  hide: catalogValue('tr', 'admin.quarantine.hide'),
  dismiss: catalogValue('tr', 'admin.quarantine.dismiss'),
  noteLabel: catalogValue('tr', 'admin.quarantine.noteLabel'),
  noteRequired: catalogValue('tr', 'admin.quarantine.noteRequired'),
  dismissError: catalogValue('tr', 'admin.quarantine.dismissError'),
  resolutionNote: catalogValue('tr', 'admin.quarantine.resolutionNote'),
  viewLedger: catalogValue('tr', 'admin.quarantine.viewLedger'),
  negativePrice: catalogValue('tr', 'admin.reason.NEGATIVE_PRICE'),
  schemaMismatch: catalogValue('tr', 'admin.reason.SCHEMA_MISMATCH'),
}

async function openQuarantineSection() {
  const user = userEvent.setup()
  await user.click(await screen.findByRole('button', { name: tr.sectionQuarantine }))
  return user
}

describe('TC-MDF-047 — quarantine review flow', () => {
  beforeEach(() => {
    resetAdminTest()
  })

  it('lists open quarantined facts with job, reason explanation, source and date, and inspects the payload (UXR-MDF-007)', async () => {
    renderAdmin()
    const user = await openQuarantineSection()

    const table = await screen.findByTestId('quarantine-table')
    // Semantic columns (UXR-G-028).
    expect(within(table).getByRole('columnheader', { name: tr.job })).toBeInTheDocument()
    expect(within(table).getByRole('columnheader', { name: tr.reason })).toBeInTheDocument()
    expect(within(table).getByRole('columnheader', { name: tr.source })).toBeInTheDocument()

    const row = within(table).getByRole('row', { name: /prices/ })
    expect(within(row).getByText(tr.negativePrice)).toBeInTheDocument()
    expect(within(row).getByText('prices-ok.json#ALFA')).toBeInTheDocument()

    await user.click(within(row).getByRole('button', { name: tr.inspect }))
    const payload = await screen.findByTestId('quarantine-payload-1')
    expect(payload).toHaveTextContent('ALFA')
    expect(within(row).getByRole('button', { name: tr.hide })).toBeInTheDocument()
  })

  it('blocks an empty-note dismissal inline and never drops the item silently (UXR-MDF-008)', async () => {
    renderAdmin()
    const user = await openQuarantineSection()
    const table = await screen.findByTestId('quarantine-table')
    const row = within(table).getByRole('row', { name: /prices/ })

    await user.click(within(row).getByRole('button', { name: tr.dismiss }))

    expect(await within(row).findByText(tr.noteRequired)).toBeInTheDocument()
    // The item is still open (no silent drop).
    expect(within(table).getByRole('row', { name: /prices/ })).toBeInTheDocument()
  })

  it('dismisses with a note, moves the item to dismissed, and keeps it reviewable (UXR-MDF-008/009)', async () => {
    renderAdmin()
    const user = await openQuarantineSection()
    const table = await screen.findByTestId('quarantine-table')
    const row = within(table).getByRole('row', { name: /prices/ })

    await user.type(within(row).getByLabelText(tr.noteLabel), 'accepted as source error')
    await user.click(within(row).getByRole('button', { name: tr.dismiss }))

    // The open queue no longer contains the item.
    await waitFor(() =>
      expect(screen.queryByRole('row', { name: /prices/ })).toBeNull(),
    )

    // Switch to the dismissed filter: the item is reviewable with its note.
    await user.selectOptions(screen.getByLabelText(tr.filterLabel), tr.statusDismissed)
    const dismissed = await screen.findByTestId('quarantine-table')
    expect(
      within(dismissed).getByRole('columnheader', { name: tr.resolutionNote }),
    ).toBeInTheDocument()
    const dismissedRow = within(dismissed).getByRole('row', { name: /prices/ })
    expect(within(dismissedRow).getByText('accepted as source error')).toBeInTheDocument()

    // Pre-existing dismissed rows remain reviewable with their plain-language reason.
    const betaRow = within(dismissed).getByRole('row', { name: /statements/ })
    expect(within(betaRow).getByText(tr.schemaMismatch)).toBeInTheDocument()
    expect(within(betaRow).getByText('source error accepted')).toBeInTheDocument()
  })

  it('dismissal updates the summary’s open-quarantine count without a manual reload (UXR-MDF-006/009)', async () => {
    renderAdmin()
    const user = await openQuarantineSection()
    const table = await screen.findByTestId('quarantine-table')
    const row = within(table).getByRole('row', { name: /prices/ })

    await user.type(within(row).getByLabelText(tr.noteLabel), 'accepted as source error')
    await user.click(within(row).getByRole('button', { name: tr.dismiss }))
    await waitFor(() =>
      expect(screen.queryByRole('row', { name: /prices/ })).toBeNull(),
    )

    // The shell shows one section at a time: returning to the summary shows the
    // decremented open count (4 → 3) — the dismissal invalidated the summary read,
    // so no manual reload is needed.
    await user.click(screen.getByRole('button', { name: tr.sectionSummary }))
    const count = await screen.findByTestId('open-quarantine-count')
    await waitFor(() => expect(count).toHaveTextContent('3'))
  })

  it('surfaces a dismiss failure inline and preserves the entered note (UXR-G-003)', async () => {
    server.use(
      problemHandler(`${API_BASE}/admin/quarantine/1/dismiss`, 'INTERNAL', {
        method: 'post',
      }),
    )
    renderAdmin()
    const user = await openQuarantineSection()
    const table = await screen.findByTestId('quarantine-table')
    const row = within(table).getByRole('row', { name: /prices/ })

    const note = within(row).getByLabelText(tr.noteLabel)
    await user.type(note, 'accepted as source error')
    await user.click(within(row).getByRole('button', { name: tr.dismiss }))

    // The failure is surfaced inline (never a silent drop) and the note the
    // operator entered is preserved for a retry.
    expect(await within(row).findByRole('alert')).toHaveTextContent(tr.dismissError)
    expect(note).toHaveValue('accepted as source error')
    expect(within(table).getByRole('row', { name: /prices/ })).toBeInTheDocument()
  })

  it('navigates from an item to the run ledger filtered to its job (UXR-MDF-010)', async () => {
    renderAdmin()
    const user = await openQuarantineSection()
    const table = await screen.findByTestId('quarantine-table')
    const row = within(table).getByRole('row', { name: /prices/ })

    await user.click(within(row).getByRole('button', { name: tr.viewLedger }))

    expect(
      await screen.findByRole('heading', { name: tr.sectionLedger }),
    ).toBeInTheDocument()
    expect(screen.getByLabelText(catalogValue('tr', 'admin.ledger.filterJob'))).toHaveValue(
      'prices',
    )
  })
})
