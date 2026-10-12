import { describe, expect, it, beforeEach } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import {
  API_BASE,
  INGEST_RUNS_FIXTURE,
  ingestTriggerHandler,
} from '@/test/msw/handlers'
import { ADMIN_LEDGER_POLL_MS } from '@/screens/admin/api'
import { resetAdminTest, renderAdmin } from '@/screens/admin/testSupport'

/**
 * TC-MDF-048 + TC-MDF-049 (L3 translations of the L4 cases) — the run ledger
 * auto-refresh (UXR-MDF-015/028), job/status filtering (UXR-MDF-014) and the
 * job/backfill trigger confirmations (UXR-MDF-016/017, UXR-G-022).
 */

const tr = {
  sectionLedger: catalogValue('tr', 'admin.sections.ledger'),
  filterJob: catalogValue('tr', 'admin.ledger.filterJob'),
  filterStatus: catalogValue('tr', 'admin.ledger.filterStatus'),
  allJobs: catalogValue('tr', 'admin.ledger.allJobs'),
  allStatuses: catalogValue('tr', 'admin.ledger.allStatuses'),
  job: catalogValue('tr', 'admin.ledger.columns.job'),
  status: catalogValue('tr', 'admin.ledger.columns.status'),
  autoRefreshNotice: catalogValue('tr', 'admin.ledger.autoRefreshNotice'),
  refresh: catalogValue('tr', 'admin.ledger.refresh'),
  triggerHeading: catalogValue('tr', 'admin.ledger.triggerHeading'),
  triggerJobLabel: catalogValue('tr', 'admin.ledger.triggerJobLabel'),
  backfillLabel: catalogValue('tr', 'admin.ledger.backfillLabel'),
  backfillFromLabel: catalogValue('tr', 'admin.ledger.backfillFromLabel'),
  backfillFromRequired: catalogValue('tr', 'admin.ledger.backfillFromRequired'),
  trigger: catalogValue('tr', 'admin.ledger.trigger'),
  triggerTitle: catalogValue('tr', 'admin.ledger.triggerTitle'),
  triggerDescription: catalogValue('tr', 'admin.ledger.triggerDescription'),
  triggerBackfillDescription: catalogValue(
    'tr',
    'admin.ledger.triggerBackfillDescription',
  ),
  triggerConfirm: catalogValue('tr', 'admin.ledger.triggerConfirm'),
  cancel: catalogValue('tr', 'admin.ledger.cancel'),
  running: catalogValue('tr', 'admin.runStatus.running'),
}

const fill = (template: string, vars: Record<string, string>) =>
  template.replace(/\{\{(\w+)\}\}/g, (_m, key: string) => vars[key] ?? '')

async function openLedger(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: tr.sectionLedger }))
  return screen.findByTestId('ledger-table')
}

describe('TC-MDF-048 — run-ledger auto-refresh & manual refresh', () => {
  beforeEach(() => {
    resetAdminTest()
  })

  it('lists runs with job, status and timing, and applies the job/status filters conjunctively (UXR-MDF-014)', async () => {
    const seen: Array<{ job: string | null; status: string | null }> = []
    server.use(
      http.get(`${API_BASE}/admin/ingest-runs`, ({ request }) => {
        const url = new URL(request.url)
        const job = url.searchParams.get('job')
        const status = url.searchParams.get('status')
        seen.push({ job, status })
        return HttpResponse.json({
          runs: INGEST_RUNS_FIXTURE.filter(
            (run) => (!job || run.job === job) && (!status || run.status === status),
          ),
        })
      }),
    )
    const user = userEvent.setup()
    renderAdmin()
    const table = await openLedger(user)

    expect(within(table).getByRole('columnheader', { name: tr.job })).toBeInTheDocument()
    expect(within(table).getByRole('columnheader', { name: tr.status })).toBeInTheDocument()
    expect(within(table).getByText(tr.running)).toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText(tr.filterJob), 'statements')
    await user.selectOptions(screen.getByLabelText(tr.filterStatus), 'succeeded')

    await waitFor(() =>
      expect(seen.some((s) => s.job === 'statements' && s.status === 'succeeded')).toBe(true),
    )
  })

  it('refreshes manually on demand (UXR-MDF-028)', async () => {
    let calls = 0
    server.use(
      http.get(`${API_BASE}/admin/ingest-runs`, () => {
        calls += 1
        return HttpResponse.json({ runs: INGEST_RUNS_FIXTURE })
      }),
    )
    const user = userEvent.setup()
    renderAdmin()
    await openLedger(user)
    await waitFor(() => expect(calls).toBeGreaterThan(0))

    const before = calls
    await user.click(screen.getByRole('button', { name: tr.refresh }))
    await waitFor(() => expect(calls).toBeGreaterThan(before))
  })

  it('shows a visible periodic-update indication and polls while a run is non-terminal (UXR-MDF-015)', async () => {
    let calls = 0
    server.use(
      http.get(`${API_BASE}/admin/ingest-runs`, () => {
        calls += 1
        return HttpResponse.json({ runs: INGEST_RUNS_FIXTURE })
      }),
    )
    const user = userEvent.setup()
    renderAdmin()
    await openLedger(user)

    expect(screen.getByText(tr.autoRefreshNotice)).toBeInTheDocument()
    await waitFor(() => expect(calls).toBeGreaterThan(0))

    // A poll fires without any user action while the run stays non-terminal.
    const before = calls
    await waitFor(() => expect(calls).toBeGreaterThan(before), {
      timeout: ADMIN_LEDGER_POLL_MS + 3_000,
    })
  }, 15_000)

  it('stops the periodic indication once every run is terminal (UXR-MDF-015)', async () => {
    server.use(
      http.get(`${API_BASE}/admin/ingest-runs`, () =>
        HttpResponse.json({
          runs: INGEST_RUNS_FIXTURE.filter((run) => run.status === 'succeeded'),
        }),
      ),
    )
    const user = userEvent.setup()
    renderAdmin()
    await openLedger(user)

    await waitFor(() => expect(screen.queryByText(tr.autoRefreshNotice)).toBeNull())
  })
})

describe('TC-MDF-049 — job trigger and backfill confirmations', () => {
  beforeEach(() => {
    resetAdminTest()
  })

  it('requires an explicit confirmation naming the job before a run starts (UXR-MDF-016)', async () => {
    const calls: Array<{ job: string; body: { backfillFrom?: string } }> = []
    server.use(ingestTriggerHandler((job, body) => calls.push({ job, body })))
    const user = userEvent.setup()
    renderAdmin()
    await openLedger(user)

    await user.selectOptions(screen.getByLabelText(tr.triggerJobLabel), 'statements')
    await user.click(screen.getByRole('button', { name: tr.trigger }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(tr.triggerTitle)).toBeInTheDocument()
    expect(
      within(dialog).getByText(fill(tr.triggerDescription, { job: 'statements' })),
    ).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: tr.triggerConfirm }))
    await waitFor(() => expect(calls).toHaveLength(1))
    expect(calls[0].job).toBe('statements')
  })

  it('cancels without creating a run (UXR-G-022)', async () => {
    const calls: string[] = []
    server.use(ingestTriggerHandler((job) => calls.push(job)))
    const user = userEvent.setup()
    renderAdmin()
    await openLedger(user)

    await user.click(screen.getByRole('button', { name: tr.trigger }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: tr.cancel }))

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(calls).toHaveLength(0)
  })

  it('names the job and the backfill start date, and carries the date to the API (UXR-MDF-017)', async () => {
    const bodies: Array<{ backfillFrom?: string }> = []
    server.use(ingestTriggerHandler((_job, body) => bodies.push(body)))
    const user = userEvent.setup()
    renderAdmin()
    await openLedger(user)

    await user.click(screen.getByLabelText(tr.backfillLabel))
    await user.type(screen.getByLabelText(tr.backfillFromLabel), '2025-01-01')
    await user.click(screen.getByRole('button', { name: tr.trigger }))

    const dialog = await screen.findByRole('dialog')
    expect(
      within(dialog).getByText(
        fill(tr.triggerBackfillDescription, { job: 'prices', date: '2025-01-01' }),
      ),
    ).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: tr.triggerConfirm }))
    await waitFor(() => expect(bodies).toHaveLength(1))
    expect(bodies[0].backfillFrom).toBe('2025-01-01')
  })

  it('validates an empty backfill start date inline before confirming', async () => {
    const user = userEvent.setup()
    renderAdmin()
    await openLedger(user)

    await user.click(screen.getByLabelText(tr.backfillLabel))
    await user.click(screen.getByRole('button', { name: tr.trigger }))

    expect(await screen.findByText(tr.backfillFromRequired)).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('locks the trigger while the run request is pending (UXR-G-002/020)', async () => {
    let release!: () => void
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    server.use(
      http.post(`${API_BASE}/admin/ingest/:job/run`, async () => {
        await gate
        return HttpResponse.json({
          runId: 13,
          job: 'prices',
          status: 'running',
          written: 0,
          unchanged: 0,
          quarantined: 0,
        })
      }),
    )
    const user = userEvent.setup()
    renderAdmin()
    await openLedger(user)

    await user.click(screen.getByRole('button', { name: tr.trigger }))
    const dialog = await screen.findByRole('dialog')
    const confirm = within(dialog).getByRole('button', { name: tr.triggerConfirm })
    await user.click(confirm)

    await waitFor(() => expect(confirm).toBeDisabled())
    release()
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })
})
