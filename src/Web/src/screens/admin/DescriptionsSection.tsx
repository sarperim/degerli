import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Button } from '@/components/ui/button'
import { EmptyState } from '@/components/ui/empty-state'
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
import {
  DESCRIPTION_STATUSES,
  type AdminDescription,
  type DescriptionStatus,
} from '@/lib/api/contracts'
import {
  useAdminDescriptions,
  usePublishDescription,
  useSaveDescription,
} from '@/screens/admin/api'
import { datePart } from '@/screens/admin/copy'
import { SectionError } from '@/screens/admin/SectionError'
import { useAdminUiStore } from '@/screens/admin/adminUiStore'

/**
 * Description review & publication section (SCR-012 §3.5; UXR-MDF-018..022;
 * FR-RES-020/021, UC-RES-004 steps 2–4).
 *
 * The status-filtered queue **is** the editor payload (`03` §8 v1.2): both
 * language texts and the KAP `sourceRefs` ride the list. Selecting an entry opens
 * the bilingual editor with its source refs alongside (UXR-MDF-020); saving keeps
 * both texts on failure (UXR-MDF-019, UXR-G-004); publish is disabled while a
 * language is empty, with the explanation (UXR-MDF-021), and publishing requires
 * a confirmation stating public visibility (UXR-MDF-022). A publish refreshes the
 * queue and the ops-summary coverage counts without a manual refresh (M-2).
 */

/** The description status vocabulary, falling back to the raw code (SCR-012 §5). */
const DESCRIPTION_STATUS_KEYS: Record<string, string> = {
  draft: 'admin.descriptions.statusDraft',
  reviewed: 'admin.descriptions.statusReviewed',
  published: 'admin.descriptions.statusPublished',
}

function descriptionStatusKey(status: string): string {
  return DESCRIPTION_STATUS_KEYS[status] ?? status
}

export function DescriptionsSection() {
  const { t } = useTranslation()
  const status = useAdminUiStore((state) => state.descriptionStatus)
  const setStatus = useAdminUiStore((state) => state.setDescriptionStatus)
  const queue = useAdminDescriptions(status)

  const [selected, setSelected] = useState<AdminDescription | null>(null)

  return (
    <section data-testid="descriptions" aria-label={t('admin.sections.descriptions')}>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-h3 font-semibold">{t('admin.sections.descriptions')}</h2>
        <div className="flex items-center gap-2">
          <label htmlFor="descriptions-status" className="text-sm text-muted-foreground">
            {t('admin.descriptions.filterLabel')}
          </label>
          <select
            id="descriptions-status"
            className="h-9 rounded-md border bg-background px-2 text-sm"
            value={status}
            onChange={(event) => setStatus(event.target.value as DescriptionStatus)}
          >
            {DESCRIPTION_STATUSES.map((option) => (
              <option key={option} value={option}>
                {t(descriptionStatusKey(option))}
              </option>
            ))}
          </select>
        </div>
      </div>

      {queue.isPending ? (
        <p className="text-sm text-muted-foreground">{t('common.loading')}</p>
      ) : queue.isError ? (
        <SectionError
          title={t('admin.descriptions.errorTitle')}
          body={t('admin.descriptions.errorBody')}
          onRetry={() => void queue.refetch()}
        />
      ) : queue.data.descriptions.length === 0 ? (
        <EmptyState
          title={t('admin.descriptions.emptyTitle')}
          description={t('admin.descriptions.emptyBody')}
        />
      ) : (
        <TableContainer>
          <Table data-testid="descriptions-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('admin.descriptions.columns.stock')}</TableHead>
                <TableHead>{t('admin.descriptions.columns.status')}</TableHead>
                {/* Visually-hidden header keeps the cell/column relationship for
                    assistive tech (UXR-G-028). */}
                <TableHead className="sr-only">
                  {t('admin.descriptions.columns.actions')}
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {queue.data.descriptions.map((row) => (
                <TableRow key={row.id}>
                  <TableCell className="font-medium">
                    {row.symbol}
                    <span className="ml-2 text-muted-foreground">{row.name}</span>
                  </TableCell>
                  <TableCell>{t(descriptionStatusKey(row.status))}</TableCell>
                  <TableCell>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => setSelected(row)}
                    >
                      {t('admin.descriptions.edit')}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      {selected ? (
        <DescriptionEditor
          key={selected.id}
          description={selected}
          onUpdated={(updated) => setSelected(updated)}
        />
      ) : null}
    </section>
  )
}

function DescriptionEditor({
  description,
  onUpdated,
}: {
  description: AdminDescription
  onUpdated: (description: AdminDescription) => void
}) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'

  const [textTr, setTextTr] = useState(description.textTr ?? '')
  const [textEn, setTextEn] = useState(description.textEn ?? '')
  const [confirmOpen, setConfirmOpen] = useState(false)

  // When a different description is loaded, seed the editor with its texts.
  useEffect(() => {
    setTextTr(description.textTr ?? '')
    setTextEn(description.textEn ?? '')
  }, [description.id, description.textTr, description.textEn])

  const save = useSaveDescription()
  const publish = usePublishDescription()

  const bothPresent = textTr.trim() !== '' && textEn.trim() !== ''

  const submitSave = () => {
    save.mutate(
      { id: description.id, textTr, textEn },
      {
        onSuccess: (updated) => {
          setTextTr(updated.textTr ?? '')
          setTextEn(updated.textEn ?? '')
          onUpdated(updated)
        },
      },
    )
  }

  return (
    <div
      data-testid="description-editor"
      className="mt-8 rounded-md border p-4"
      aria-label={t('admin.descriptions.editorLabel', { symbol: description.symbol })}
    >
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <h3 className="text-base font-medium">
          {t('admin.descriptions.editorHeading', { symbol: description.symbol })}
        </h3>
        <span className="text-sm text-muted-foreground">
          {t(descriptionStatusKey(description.status))}
        </span>
        {description.status === 'published' ? (
          <span className="text-sm font-medium text-primary">
            {t('admin.descriptions.publishedNotice')}
          </span>
        ) : null}
      </div>

      <div className="flex flex-col gap-4">
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted-foreground">
            {t('admin.descriptions.textTrLabel')}
          </span>
          <textarea
            className="min-h-24 rounded-md border bg-background p-2 text-sm"
            value={textTr}
            onChange={(event) => setTextTr(event.target.value)}
          />
        </label>

        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted-foreground">
            {t('admin.descriptions.textEnLabel')}
          </span>
          <textarea
            className="min-h-24 rounded-md border bg-background p-2 text-sm"
            value={textEn}
            onChange={(event) => setTextEn(event.target.value)}
          />
        </label>

        <div>
          <h4 className="mb-2 text-sm font-medium">
            {t('admin.descriptions.sourceRefsHeading')}
          </h4>
          <ul data-testid="description-source-refs" className="flex flex-col gap-2 text-sm">
            {description.sourceRefs.map((ref) => (
              <li key={ref.disclosureId} className="flex flex-wrap items-center gap-2">
                {ref.title ? (
                  <>
                    <span className="font-medium">{ref.title}</span>
                    {ref.disclosureType ? (
                      <span className="text-muted-foreground">{ref.disclosureType}</span>
                    ) : null}
                    {ref.publishDate ? (
                      <span className="text-muted-foreground">
                        {formatAsOfDate(locale, datePart(ref.publishDate))}
                      </span>
                    ) : null}
                    {ref.sourceUrl ? (
                      <a
                        className="text-primary underline-offset-4 hover:underline"
                        href={ref.sourceUrl}
                        target="_blank"
                        rel="noreferrer"
                      >
                        {ref.sourceUrl}
                      </a>
                    ) : null}
                  </>
                ) : (
                  // An id that no longer resolves is a stub, never dropped (`01` §10.7).
                  <span className="text-muted-foreground">
                    {t('admin.descriptions.sourceRefStub', { id: ref.disclosureId })}
                  </span>
                )}
              </li>
            ))}
          </ul>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <Button
            type="button"
            onClick={submitSave}
            disabled={save.isPending}
            aria-busy={save.isPending || undefined}
          >
            {t('admin.descriptions.save')}
          </Button>
          {save.isSuccess ? (
            <span className="text-sm text-muted-foreground">
              {t('admin.descriptions.saved')}
            </span>
          ) : null}
          {save.isError ? (
            <span
              role="alert"
              data-testid="description-save-error"
              className="text-sm font-medium text-destructive"
            >
              {t('admin.descriptions.saveError')}
            </span>
          ) : null}
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <Button
            type="button"
            variant="outline"
            onClick={() => setConfirmOpen(true)}
            disabled={!bothPresent || publish.isPending}
          >
            {t('admin.descriptions.publish')}
          </Button>
          {!bothPresent ? (
            <span className="text-sm text-muted-foreground">
              {t('admin.descriptions.publishDisabledReason')}
            </span>
          ) : null}
        </div>
      </div>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={(open) => {
          setConfirmOpen(open)
          if (!open) publish.reset()
        }}
        title={t('admin.descriptions.publishTitle')}
        description={t('admin.descriptions.publishConfirm', { symbol: description.symbol })}
        confirmLabel={t('admin.descriptions.publishConfirmAction')}
        cancelLabel={t('common.cancel')}
        closeLabel={t('common.close')}
        pending={publish.isPending}
        error={publish.isError ? t('admin.descriptions.publishError') : undefined}
        onConfirm={() =>
          publish.mutate(description.id, {
            onSuccess: (updated) => {
              setTextTr(updated.textTr ?? '')
              setTextEn(updated.textEn ?? '')
              onUpdated(updated)
              setConfirmOpen(false)
            },
          })
        }
      />
    </div>
  )
}
