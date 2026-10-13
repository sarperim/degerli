import { useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'
import { EmptyState } from '@/components/ui/empty-state'
import { FormField } from '@/components/ui/field'
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
import type { QuarantineItem } from '@/lib/api/contracts'
import { formatAsOfDate } from '@/lib/format'
import { useDismissQuarantine, useQuarantine } from '@/screens/admin/api'
import { datePart, reasonKey } from '@/screens/admin/copy'
import { SectionError } from '@/screens/admin/SectionError'
import { useAdminUiStore, type QuarantineStatus } from '@/screens/admin/adminUiStore'

/**
 * Quarantine review queue (SCR-012 §3.2; UXR-MDF-007/008/009/010/027).
 *
 * Open/dismissed views; each item shows its job, source, plain-language reason
 * and date with the rejected payload inspectable; dismissing requires a
 * non-empty note (inline validation) and moves the item to dismissed while
 * recording the accepted gap; each item links into the run ledger filtered to its
 * job.
 */
export function QuarantineSection() {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const status = useAdminUiStore((state) => state.quarantineStatus)
  const setStatus = useAdminUiStore((state) => state.setQuarantineStatus)
  const setActiveSection = useAdminUiStore((state) => state.setActiveSection)
  const setLedgerFilters = useAdminUiStore((state) => state.setLedgerFilters)
  const queue = useQuarantine(status)

  return (
    <section data-testid="quarantine" aria-label={t('admin.sections.quarantine')}>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-h3 font-semibold">{t('admin.sections.quarantine')}</h2>
        <div className="flex items-center gap-2">
          <label htmlFor="quarantine-status" className="text-sm text-muted-foreground">
            {t('admin.quarantine.filterLabel')}
          </label>
          <select
            id="quarantine-status"
            className="h-9 rounded-md border bg-background px-2 text-sm"
            value={status}
            onChange={(event) => setStatus(event.target.value as QuarantineStatus)}
          >
            <option value="open">{t('admin.quarantine.statusOpen')}</option>
            <option value="dismissed">{t('admin.quarantine.statusDismissed')}</option>
          </select>
        </div>
      </div>

      {queue.isPending ? (
        <p className="text-sm text-muted-foreground">{t('common.loading')}</p>
      ) : queue.isError ? (
        <SectionError
          title={t('admin.quarantine.errorTitle')}
          body={t('admin.quarantine.errorBody')}
          onRetry={() => void queue.refetch()}
        />
      ) : queue.data.items.length === 0 ? (
        <EmptyState
          title={
            status === 'open'
              ? t('admin.quarantine.emptyOpenTitle')
              : t('admin.quarantine.emptyDismissedTitle')
          }
          description={
            status === 'open'
              ? t('admin.quarantine.emptyOpenBody')
              : t('admin.quarantine.emptyDismissedBody')
          }
        />
      ) : (
        <TableContainer>
          <Table data-testid="quarantine-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('admin.quarantine.columns.job')}</TableHead>
                <TableHead>{t('admin.quarantine.columns.reason')}</TableHead>
                <TableHead>{t('admin.quarantine.columns.source')}</TableHead>
                <TableHead>{t('admin.quarantine.columns.quarantinedAt')}</TableHead>
                {status === 'dismissed' ? (
                  <TableHead>{t('admin.quarantine.resolutionNote')}</TableHead>
                ) : null}
                {/* Actions column: visually-hidden header keeps the cell/column
                    relationship for assistive tech (UXR-G-028). */}
                <TableHead className="sr-only">
                  {t('admin.quarantine.columns.actions')}
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {queue.data.items.map((item) => (
                <QuarantineRow
                  key={item.id}
                  item={item}
                  locale={locale}
                  onViewLedger={() => {
                    setLedgerFilters({ job: item.jobCode })
                    setActiveSection('ledger')
                  }}
                />
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
    </section>
  )
}

function QuarantineRow({
  item,
  locale,
  onViewLedger,
}: {
  item: QuarantineItem
  locale: string
  onViewLedger: () => void
}) {
  const { t } = useTranslation()
  const [inspected, setInspected] = useState(false)
  const [note, setNote] = useState('')
  const [noteError, setNoteError] = useState<string | null>(null)
  const dismiss = useDismissQuarantine()

  const open = item.status === 'open'

  const submitDismiss = (event: FormEvent) => {
    event.preventDefault()
    if (note.trim() === '') {
      setNoteError(t('admin.quarantine.noteRequired'))
      return
    }
    setNoteError(null)
    dismiss.mutate({ id: item.id, note: note.trim() })
  }

  return (
    <TableRow>
      <TableCell className="font-medium">{item.jobCode}</TableCell>
      <TableCell>{t(reasonKey(item.reasonCode))}</TableCell>
      <TableCell>{item.sourceRef ?? '—'}</TableCell>
      <TableCell>{formatAsOfDate(locale, datePart(item.quarantinedAt))}</TableCell>
      {!open ? <TableCell>{item.resolutionNote ?? '—'}</TableCell> : null}
      <TableCell>
        <div className="flex flex-col gap-2">
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setInspected((value) => !value)}
            >
              {inspected ? t('admin.quarantine.hide') : t('admin.quarantine.inspect')}
            </Button>
            <Button type="button" variant="ghost" size="sm" onClick={onViewLedger}>
              {t('admin.quarantine.viewLedger')}
            </Button>
          </div>

          {open ? (
            <form onSubmit={submitDismiss} className="flex flex-col gap-2">
              <FormField
                id={`quarantine-note-${item.id}`}
                label={t('admin.quarantine.noteLabel')}
                error={noteError ?? undefined}
              >
                {(controlProps) => (
                  <Input
                    {...controlProps}
                    value={note}
                    onChange={(event) => setNote(event.target.value)}
                  />
                )}
              </FormField>
              <Button
                type="submit"
                size="sm"
                disabled={dismiss.isPending}
                aria-busy={dismiss.isPending || undefined}
              >
                {t('admin.quarantine.dismiss')}
              </Button>
              {dismiss.isError ? (
                <p
                  role="alert"
                  data-testid={`quarantine-dismiss-error-${item.id}`}
                  className="text-xs font-medium text-destructive"
                >
                  {t('admin.quarantine.dismissError')}
                </p>
              ) : null}
            </form>
          ) : null}

          {inspected ? (
            <pre
              data-testid={`quarantine-payload-${item.id}`}
              className="max-w-md overflow-x-auto rounded bg-muted p-2 text-xs"
            >
              {JSON.stringify(item.payloadJson, null, 2)}
            </pre>
          ) : null}
        </div>
      </TableCell>
    </TableRow>
  )
}
