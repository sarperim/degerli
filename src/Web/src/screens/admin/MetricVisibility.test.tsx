import { describe, expect, it, beforeEach } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import { API_BASE, adminMetricVisibilityHandlers } from '@/test/msw/handlers'
import { resetAdminTest, renderAdmin } from '@/screens/admin/testSupport'

/**
 * TC-SCR-021 (L4 in the plan; translated to the component level here) — the SCR-012
 * metric-visibility panel.
 *
 * The panel lists all 18 screenable metrics with their current state; toggling
 * requires a confirmation that states both consequences (the criteria builder stops
 * offering the metric; saved screens report the dropped criterion on re-run); cancel
 * aborts with no change; a failed toggle keeps the previous visibility; and a
 * successful toggle flips the served state (TC-SCR-020 mechanism, UXR-MDF-023/024).
 */

const tr = {
  section: catalogValue('tr', 'admin.sections.metrics'),
  columnMetric: catalogValue('tr', 'admin.metrics.columnMetric'),
  columnState: catalogValue('tr', 'admin.metrics.columnState'),
  screenable: catalogValue('tr', 'admin.metrics.screenable'),
  hidden: catalogValue('tr', 'admin.metrics.hidden'),
  hide: catalogValue('tr', 'admin.metrics.hide'),
  show: catalogValue('tr', 'admin.metrics.show'),
  confirmTitleRoe: catalogValue('tr', 'admin.metrics.confirmTitle').replace(
    '{{metric}}',
    'Özkaynak Kârlılığı',
  ),
  confirmDescription: catalogValue('tr', 'admin.metrics.confirmDescription'),
  toggleError: catalogValue('tr', 'admin.metrics.toggleError'),
  cancel: catalogValue('tr', 'common.cancel'),
}

async function openMetrics(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: tr.section }))
}

function roeRow() {
  const table = screen.getByTestId('metric-visibility-table')
  return within(table).getByRole('row', { name: /Özkaynak Kârlılığı/ })
}

describe('TC-SCR-021 — metric visibility panel', () => {
  beforeEach(() => {
    resetAdminTest()
  })

  it('lists all 18 metrics with their current visibility state (UXR-MDF-023)', async () => {
    const user = userEvent.setup()
    server.use(...adminMetricVisibilityHandlers().handlers)
    renderAdmin()
    await openMetrics(user)

    const table = await screen.findByTestId('metric-visibility-table')
    expect(within(table).getAllByRole('row')).toHaveLength(19) // header + 18 metrics
    expect(within(table).getByRole('columnheader', { name: tr.columnMetric })).toBeInTheDocument()
    expect(within(table).getByRole('columnheader', { name: tr.columnState })).toBeInTheDocument()

    expect(within(roeRow()).getByText(tr.screenable)).toBeInTheDocument()
  })

  it('requires a consequence-stating confirmation; cancel aborts with no change (UXR-MDF-024)', async () => {
    const user = userEvent.setup()
    const visibility = adminMetricVisibilityHandlers()
    server.use(...visibility.handlers)
    renderAdmin()
    await openMetrics(user)

    await user.click(within(roeRow()).getByRole('button', { name: tr.hide }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(tr.confirmTitleRoe)).toBeInTheDocument()
    // The confirmation states BOTH consequences.
    expect(within(dialog).getByText(tr.confirmDescription)).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: tr.cancel }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    // Nothing changed: the row is still visible and the server still reports it so.
    expect(within(roeRow()).getByText(tr.screenable)).toBeInTheDocument()
    expect(visibility.metrics.find((metric) => metric.metricCode === 'roe')?.isScreenable).toBe(true)
  })

  it('toggles a metric on confirmation and reflects the new state (TC-SCR-020 mechanism)', async () => {
    const user = userEvent.setup()
    const visibility = adminMetricVisibilityHandlers()
    server.use(...visibility.handlers)
    renderAdmin()
    await openMetrics(user)

    await user.click(within(roeRow()).getByRole('button', { name: tr.hide }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: tr.hide }))

    // The panel refetches and now shows the hidden state; the server flipped too.
    expect(await within(roeRow()).findByText(tr.hidden)).toBeInTheDocument()
    expect(visibility.metrics.find((metric) => metric.metricCode === 'roe')?.isScreenable).toBe(false)
  })

  it('keeps the previous visibility and states a plain-language error when the toggle fails', async () => {
    const user = userEvent.setup()
    const visibility = adminMetricVisibilityHandlers()
    server.use(
      http.patch(`${API_BASE}/admin/screener-metrics/:code`, () =>
        new HttpResponse(null, { status: 500 }),
      ),
      ...visibility.handlers,
    )
    renderAdmin()
    await openMetrics(user)

    await user.click(within(roeRow()).getByRole('button', { name: tr.hide }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: tr.hide }))

    // The dialog stays open and states the plain-language error (UXR-G-003).
    expect(await within(dialog).findByText(tr.toggleError)).toBeInTheDocument()

    // Cancel out; the previous visibility is retained (row and server both unchanged).
    await user.click(within(dialog).getByRole('button', { name: tr.cancel }))
    expect(within(roeRow()).getByText(tr.screenable)).toBeInTheDocument()
    expect(visibility.metrics.find((metric) => metric.metricCode === 'roe')?.isScreenable).toBe(true)
  })
})
