import { describe, expect, it, beforeEach } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { formatAsOfDate } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import { API_BASE, problemHandler } from '@/test/msw/handlers'
import { resetAdminTest, renderAdmin } from '@/screens/admin/testSupport'

/**
 * TC-MDF-046 (L3 translation of the L4 case) — the ops summary renders with
 * staleness (UXR-MDF-003/004/005/006).
 *
 * Per-job last run shows status + completion time; a stale data type shows its
 * last-success date with a visible stale indication; content coverage shows the
 * published/draft/universe counts; the open-quarantine count links into the
 * quarantine queue.
 */

const tr = {
  jobsHeading: catalogValue('tr', 'admin.summary.jobsHeading'),
  job: catalogValue('tr', 'admin.summary.job'),
  status: catalogValue('tr', 'admin.summary.status'),
  finishedAt: catalogValue('tr', 'admin.summary.finishedAt'),
  freshnessHeading: catalogValue('tr', 'admin.summary.freshnessHeading'),
  lastSuccess: catalogValue('tr', 'admin.summary.lastSuccess'),
  contentHeading: catalogValue('tr', 'admin.summary.contentHeading'),
  openQuarantineLink: catalogValue('tr', 'admin.summary.openQuarantineLink'),
  succeeded: catalogValue('tr', 'admin.runStatus.succeeded'),
  failed: catalogValue('tr', 'admin.runStatus.failed'),
  stale: catalogValue('tr', 'honestData.stale'),
  quarantine: catalogValue('tr', 'admin.sections.quarantine'),
  summaryErrorTitle: catalogValue('tr', 'admin.summary.errorTitle'),
  retry: catalogValue('tr', 'common.retry'),
  pricesType: catalogValue('tr', 'admin.dataTypes.prices'),
}

describe('TC-MDF-046 — ops summary renders with staleness', () => {
  beforeEach(() => {
    resetAdminTest()
  })

  it('shows each job’s most recent run with its status and completion time (UXR-MDF-003)', async () => {
    renderAdmin()
    const heading = await screen.findByRole('heading', { name: tr.jobsHeading })
    const table = within(heading.parentElement as HTMLElement).getByRole('table')

    const pricesRow = within(table).getByRole('row', { name: /prices/ })
    expect(within(pricesRow).getByText(tr.succeeded)).toBeInTheDocument()
    expect(
      within(pricesRow).getByText(formatAsOfDate('tr', '2026-10-06')),
    ).toBeInTheDocument()

    const statementsRow = within(table).getByRole('row', { name: /statements/ })
    expect(within(statementsRow).getByText(tr.failed)).toBeInTheDocument()
  })

  it('shows a stale data type with its last-success date and a visible stale marker (UXR-MDF-004)', async () => {
    renderAdmin()
    const heading = await screen.findByRole('heading', { name: tr.freshnessHeading })
    const table = within(heading.parentElement as HTMLElement).getByRole('table')

    const goldRow = within(table).getByRole('row', { name: /gold/ })
    expect(
      within(goldRow).getByText(formatAsOfDate('tr', '2026-09-26')),
    ).toBeInTheDocument()
    expect(within(goldRow).getByText(tr.stale)).toBeInTheDocument()

    // A fresh type carries no stale marker.
    const pricesRow = within(table).getByRole('row', { name: new RegExp(tr.pricesType) })
    expect(within(pricesRow).queryByText(tr.stale)).toBeNull()
  })

  it('shows description coverage counts and the universe total (UXR-MDF-005)', async () => {
    renderAdmin()
    await screen.findByRole('heading', { name: tr.contentHeading })

    expect(screen.getByTestId('coverage-count-published')).toHaveTextContent('61')
    expect(screen.getByTestId('coverage-count-draft')).toHaveTextContent('12')
    expect(screen.getByTestId('coverage-count-universe')).toHaveTextContent('100')
  })

  it('links the open-quarantine count into the quarantine queue (UXR-MDF-006)', async () => {
    renderAdmin()
    const link = await screen.findByRole('button', { name: tr.openQuarantineLink })
    expect(screen.getByTestId('open-quarantine-count')).toHaveTextContent('4')

    const user = userEvent.setup()
    await user.click(link)

    // The active section switched to the quarantine queue.
    expect(
      await screen.findByRole('heading', { name: tr.quarantine }),
    ).toBeInTheDocument()
    expect(screen.getByTestId('quarantine-table')).toBeInTheDocument()
  })

  it('shows a per-section plain-language error with retry when the summary fails (UXR-G-003)', async () => {
    server.use(
      problemHandler(`${API_BASE}/admin/summary`, 'INTERNAL', { params: {} }),
    )
    renderAdmin()

    expect(
      await screen.findByText(tr.summaryErrorTitle),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: tr.retry })).toBeInTheDocument()
  })
})
