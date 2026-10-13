import { useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { formatAsOfDate } from '@/lib/format'
import { hasActiveRun, useIngestRuns, useTriggerIngest } from '@/screens/admin/api'
import { RUN_STATUS_KEYS, datePart, runStatusKey } from '@/screens/admin/copy'
import { SectionError } from '@/screens/admin/SectionError'
import { useAdminUiStore } from '@/screens/admin/adminUiStore'

/** The `03` §8 job-code catalog (the registered ingest jobs). */
const JOB_CODES = [
  'prices',
  'universe-sync',
  'statements',
  'dividends',
  'corporate-actions',
  'disclosures',
  'metrics-recompute',
  'medians',
  'dcf-baselines',
  'market-snapshot',
  'fund-nav',
  'fund-performance',
  'fund-holdings',
  'macro-daily',
  'macro-cpi',
]

/** The filterable run statuses, derived from the single status-key table. */
const STATUS_CODES = Object.keys(RUN_STATUS_KEYS)

/**
 * Ingest run ledger + job triggers (SCR-012 §3.4; UXR-MDF-014/015/016/017/028).
 *
 * Runs are filterable by job and status conjunctively; the view polls with a
 * visible periodic-update indication while any run is non-terminal and always
 * offers a manual refresh. Triggering a job (or a backfill with a start date)
 * requires an explicit confirmation naming the job and — for a backfill — the
 * start date, with a cancel path and a trigger lock while pending.
 */
export function RunLedgerSection() {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const ledgerJob = useAdminUiStore((state) => state.ledgerJob)
  const ledgerStatus = useAdminUiStore((state) => state.ledgerStatus)
  const setLedgerFilters = useAdminUiStore((state) => state.setLedgerFilters)
  const runs = useIngestRuns({ job: ledgerJob, status: ledgerStatus })

  return (
    <section data-testid="run-ledger" aria-label={t('admin.sections.ledger')}>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-h3 font-semibold">{t('admin.sections.ledger')}</h2>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => void runs.refetch()}
        >
          {t('admin.ledger.refresh')}
        </Button>
      </div>

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted-foreground">{t('admin.ledger.filterJob')}</span>
          <select
            className="h-9 rounded-md border bg-background px-2 text-sm"
            value={ledgerJob ?? ''}
            onChange={(event) => setLedgerFilters({ job: event.target.value || null })}
          >
            <option value="">{t('admin.ledger.allJobs')}</option>
            {JOB_CODES.map((job) => (
              <option key={job} value={job}>
                {job}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted-foreground">{t('admin.ledger.filterStatus')}</span>
          <select
            className="h-9 rounded-md border bg-background px-2 text-sm"
            value={ledgerStatus ?? ''}
            onChange={(event) => setLedgerFilters({ status: event.target.value || null })}
          >
            <option value="">{t('admin.ledger.allStatuses')}</option>
            {STATUS_CODES.map((status) => (
              <option key={status} value={status}>
                {t(runStatusKey(status))}
              </option>
            ))}
          </select>
        </label>
      </div>

      {hasActiveRun(runs.data?.runs) ? (
        <p role="status" className="mb-3 text-xs text-muted-foreground">
          {t('admin.ledger.autoRefreshNotice')}
        </p>
      ) : null}

      {runs.isPending ? (
        <p className="text-sm text-muted-foreground">{t('common.loading')}</p>
      ) : runs.isError ? (
        <SectionError
          title={t('admin.ledger.errorTitle')}
          body={t('admin.ledger.errorBody')}
          onRetry={() => void runs.refetch()}
        />
      ) : runs.data.runs.length === 0 ? (
        <EmptyState
          title={t('admin.ledger.emptyTitle')}
          description={t('admin.ledger.emptyBody')}
        />
      ) : (
        <TableContainer>
          <Table data-testid="ledger-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('admin.ledger.columns.job')}</TableHead>
                <TableHead>{t('admin.ledger.columns.status')}</TableHead>
                <TableHead>{t('admin.ledger.columns.startedAt')}</TableHead>
                <TableHead>{t('admin.ledger.columns.finishedAt')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {runs.data.runs.map((run) => (
                <TableRow key={run.id}>
                  <TableCell className="font-medium">{run.job}</TableCell>
                  <TableCell>{t(runStatusKey(run.status))}</TableCell>
                  <TableCell>
                    {run.startedAt ? formatAsOfDate(locale, datePart(run.startedAt)) : '—'}
                  </TableCell>
                  <TableCell>
                    {run.finishedAt ? formatAsOfDate(locale, datePart(run.finishedAt)) : '—'}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      <TriggerPanel />
    </section>
  )
}

function TriggerPanel() {
  const { t } = useTranslation()
  const trigger = useTriggerIngest()
  const [job, setJob] = useState('prices')
  const [backfill, setBackfill] = useState(false)
  const [from, setFrom] = useState('')
  const [fromError, setFromError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (backfill && from.trim() === '') {
      setFromError(t('admin.ledger.backfillFromRequired'))
      return
    }
    setFromError(null)
    setConfirmOpen(true)
  }

  const description =
    backfill && from
      ? t('admin.ledger.triggerBackfillDescription', { job, date: from })
      : t('admin.ledger.triggerDescription', { job })

  return (
    <div className="mt-8">
      <h3 className="mb-2 text-base font-medium">{t('admin.ledger.triggerHeading')}</h3>
      <form onSubmit={submit} className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted-foreground">{t('admin.ledger.triggerJobLabel')}</span>
          <select
            className="h-9 rounded-md border bg-background px-2 text-sm"
            value={job}
            onChange={(event) => setJob(event.target.value)}
          >
            {JOB_CODES.map((code) => (
              <option key={code} value={code}>
                {code}
              </option>
            ))}
          </select>
        </label>

        <label className="flex items-center gap-2 pb-2 text-sm">
          <input
            type="checkbox"
            checked={backfill}
            onChange={(event) => {
              setBackfill(event.target.checked)
              setFromError(null)
            }}
          />
          {t('admin.ledger.backfillLabel')}
        </label>

        {backfill ? (
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted-foreground">
              {t('admin.ledger.backfillFromLabel')}
            </span>
            <Input
              type="date"
              aria-label={t('admin.ledger.backfillFromLabel')}
              aria-invalid={fromError ? true : undefined}
              value={from}
              onChange={(event) => setFrom(event.target.value)}
            />
            {fromError ? (
              <span role="alert" className="text-xs font-medium text-destructive">
                {fromError}
              </span>
            ) : null}
          </label>
        ) : null}

        <Button type="submit">{t('admin.ledger.trigger')}</Button>
      </form>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title={t('admin.ledger.triggerTitle')}
        description={description}
        confirmLabel={t('admin.ledger.triggerConfirm')}
        cancelLabel={t('admin.ledger.cancel')}
        closeLabel={t('common.close')}
        pending={trigger.isPending}
        error={trigger.isError ? t('admin.ledger.triggerError') : undefined}
        onConfirm={() =>
          trigger.mutate(
            { job, backfillFrom: backfill && from ? from : undefined },
            { onSuccess: () => setConfirmOpen(false) },
          )
        }
      />
    </div>
  )
}
