import { useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { ListChecks, Pencil, RefreshCw, Trash2 } from 'lucide-react'

import { Button, buttonVariants } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'
import { SkeletonTable } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { ApiError, isApiErrorCode } from '@/lib/api/client'
import { useSession } from '@/lib/auth/session'
import type { CatalogMetric, SavedScreen } from '@/lib/api/contracts'
import { useDeleteScreen, useRenameScreen, useSavedScreens } from '@/screens/savedScreens/api'
import { screenCriteriaToDrafts } from '@/screens/savedScreens/criteria'
import { useRunScreen, useScreenerMetrics } from '@/screens/screener/api'
import { useDraftStore } from '@/store/draftStore'

/**
 * SCR-004 — My Saved Screens (UX `stock-screening.md` §SCR-004; UC-SCR-003).
 *
 * Lists the signed-in user's server-persisted screens by name (FR-SCR-008,
 * NFR-SCR-005) and offers per-screen actions:
 *
 * - **Re-run** (`POST /screener/run` with the stored criteria) — fresh results
 *   computed on the latest EOD data, presented in the screener context with the
 *   current as-of date; never a stored result set (FR-SCR-009, UXR-SCR-014). A
 *   since-hidden metric is dropped by the server and named via `droppedCriteria`
 *   (UXR-SCR-015). The screener context is seeded through the M-3 draft store —
 *   the agreed active-screen display contract.
 * - **Rename** in place (`PATCH /me/screens/{id}`); a duplicate name is an inline
 *   error and the original is retained (FR-SCR-010, UXR-G-029).
 * - **Delete** behind a focus-trapping confirmation stating the consequences
 *   (FR-SCR-011, UXR-G-022); the active-screen reference is cleared.
 *
 * Anonymous direct access presents the sign-in path scoped to this feature
 * (UXR-SCR-012); an empty account gets a guidance state (UXR-SCR-018).
 */
export function SavedScreensScreen() {
  const { t } = useTranslation()
  const navigate = useNavigate()

  const session = useSession()
  const authenticated = session.data?.authenticated === true
  const screensQuery = useSavedScreens(authenticated)

  // The visible catalog is needed to convert canonical criteria → builder drafts
  // when seeding the screener on re-run.
  const metricsQuery = useScreenerMetrics()
  const metricsByCode = useMemo(
    () =>
      new Map<string, CatalogMetric>(
        (metricsQuery.data?.families ?? [])
          .flatMap((group) => group.metrics)
          .map((metric) => [metric.metricCode, metric] as const),
      ),
    [metricsQuery.data],
  )

  const [editingId, setEditingId] = useState<number | null>(null)
  const [editName, setEditName] = useState('')
  const [pendingDelete, setPendingDelete] = useState<SavedScreen | null>(null)
  const [lastRerun, setLastRerun] = useState<SavedScreen | null>(null)
  const [lastDelete, setLastDelete] = useState<SavedScreen | null>(null)

  const rename = useRenameScreen()
  const remove = useDeleteScreen()
  const run = useRunScreen()

  const setScreenerDraft = useDraftStore((state) => state.setScreenerDraft)
  const setScreenerResult = useDraftStore((state) => state.setScreenerResult)
  const setActiveScreen = useDraftStore((state) => state.setActiveScreen)
  const clearActiveScreen = useDraftStore((state) => state.clearActiveScreen)
  const setScreenerDroppedCriteria = useDraftStore(
    (state) => state.setScreenerDroppedCriteria,
  )

  const screens = screensQuery.data?.screens ?? []

  function startEdit(screen: SavedScreen) {
    rename.reset()
    setEditingId(screen.id)
    setEditName(screen.name)
  }

  function cancelEdit() {
    rename.reset()
    setEditingId(null)
    setEditName('')
  }

  function submitRename(event: FormEvent, screen: SavedScreen) {
    event.preventDefault()
    const name = editName.trim()
    if (name === '' || name === screen.name) {
      setEditingId(null)
      return
    }
    rename.mutate(
      { id: screen.id, name },
      {
        onSuccess: () => {
          // Keep the SCR-003 active-screen display in sync, in place (mutation
          // matrix §2 rename row).
          if (useDraftStore.getState().activeScreenId === screen.id) {
            setActiveScreen(screen.id, name)
          }
          setEditingId(null)
          setEditName('')
        },
      },
    )
  }

  function handleRerun(screen: SavedScreen) {
    setLastRerun(screen)
    // Naming the screen marks this as a re-run: the server drops any criterion whose
    // metric has since been hidden and reports it in `droppedCriteria` (UC-SCR-003a,
    // UXR-SCR-015) instead of the ad-hoc METRIC_NOT_AVAILABLE rejection.
    run.mutate(
      { savedScreenId: screen.id, criteria: screen.criteria },
      {
        onSuccess: (data) => {
          setScreenerDraft(screenCriteriaToDrafts(screen.criteria, metricsByCode))
          setScreenerResult(data)
          setActiveScreen(screen.id, screen.name)
          setScreenerDroppedCriteria(data.droppedCriteria)
          navigate('/screener')
        },
      },
    )
  }

  function performDelete(screen: SavedScreen) {
    setLastDelete(screen)
    remove.mutate(screen.id, {
      onSuccess: () => {
        if (useDraftStore.getState().activeScreenId === screen.id) clearActiveScreen()
        setPendingDelete(null)
      },
      // Close the confirmation so the plain-language error + retry is visible;
      // the row is retained on failure (mutation matrix §2).
      onError: () => setPendingDelete(null),
    })
  }

  function confirmDelete() {
    if (pendingDelete) performDelete(pendingDelete)
  }

  return (
    <section
      data-screen="SCR-004"
      data-route-id="savedScreens"
      className="flex flex-col gap-6"
    >
      <header className="flex flex-col gap-2">
        <h1 className="text-h1 font-semibold tracking-tight">
          {t('screens.savedScreens.title')}
        </h1>
        <p className="max-w-2xl text-muted-foreground">
          {t('screens.savedScreens.description')}
        </p>
      </header>

      {session.isPending ? (
        <div role="status" aria-label={t('common.loading')}>
          <SkeletonTable rows={3} columns={2} />
        </div>
      ) : !authenticated ? (
        <AnonymousState />
      ) : (
        <>
          {run.isError ? (
            <div
              role="alert"
              data-testid="rerun-error"
              className="flex flex-col items-start gap-2 rounded-lg border border-dashed p-4"
            >
              <p className="font-medium text-destructive">
                {t('savedScreens.reRunErrorTitle')}
              </p>
              <p className="text-sm text-muted-foreground">
                {t('savedScreens.reRunErrorBody')}
              </p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => lastRerun && handleRerun(lastRerun)}
              >
                {t('common.retry')}
              </Button>
            </div>
          ) : null}

          {screensQuery.isPending ? (
            <div role="status" aria-label={t('savedScreens.listLabel')}>
              <SkeletonTable rows={3} columns={2} />
            </div>
          ) : screensQuery.isError ? (
            <div
              role="alert"
              data-testid="saved-screens-error"
              className="flex flex-col items-start gap-2 rounded-lg border border-dashed p-4"
            >
              <p className="font-medium">{t('savedScreens.listErrorTitle')}</p>
              <p className="text-sm text-muted-foreground">
                {t('savedScreens.listErrorBody')}
              </p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => void screensQuery.refetch()}
              >
                {t('common.retry')}
              </Button>
            </div>
          ) : screens.length === 0 ? (
            <EmptyState
              data-testid="saved-screens-empty"
              icon={<ListChecks />}
              title={t('savedScreens.emptyTitle')}
              description={t('savedScreens.emptyBody')}
              action={
                <Link className={buttonVariants({ size: 'sm' })} to="/screener">
                  {t('savedScreens.emptyAction')}
                </Link>
              }
            />
          ) : (
            <TableContainer>
              <Table>
                <TableCaption>{t('savedScreens.listLabel')}</TableCaption>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('savedScreens.columns.name')}</TableHead>
                    <TableHead>{t('savedScreens.columns.actions')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody data-testid="saved-screens-list">
                  {screens.map((screen) => {
                    const isEditing = editingId === screen.id
                    const failedHere =
                      rename.isError && rename.variables?.id === screen.id
                    const duplicate =
                      failedHere && isApiErrorCode(rename.error, 'DUPLICATE_NAME')
                    const serverName =
                      rename.error instanceof ApiError
                        ? (rename.error.problem?.params?.name as string | undefined)
                        : undefined
                    return (
                      <TableRow
                        key={screen.id}
                        data-testid={`saved-screen-row-${screen.id}`}
                      >
                        <TableHead scope="row" className="align-top font-medium text-foreground">
                          {isEditing ? (
                            <form
                              className="flex flex-wrap items-center gap-2"
                              onSubmit={(event) => submitRename(event, screen)}
                            >
                              <Input
                                data-testid={`rename-input-${screen.id}`}
                                aria-label={t('savedScreens.renameInputLabel', {
                                  name: screen.name,
                                })}
                                value={editName}
                                disabled={rename.isPending}
                                onChange={(event) => setEditName(event.target.value)}
                                className="h-8 w-48"
                              />
                              <Button
                                type="submit"
                                size="sm"
                                data-testid={`rename-save-${screen.id}`}
                                disabled={rename.isPending || editName.trim() === ''}
                                aria-busy={rename.isPending || undefined}
                              >
                                {rename.isPending
                                  ? t('savedScreens.renaming')
                                  : t('savedScreens.renameSave')}
                              </Button>
                              <Button
                                type="button"
                                size="sm"
                                variant="ghost"
                                data-testid={`rename-cancel-${screen.id}`}
                                onClick={cancelEdit}
                              >
                                {t('savedScreens.renameCancel')}
                              </Button>
                            </form>
                          ) : (
                            <span data-testid={`saved-screen-name-${screen.id}`}>
                              {screen.name}
                            </span>
                          )}
                          {failedHere ? (
                            <p
                              data-testid={`rename-error-${screen.id}`}
                              role="alert"
                              className="mt-1 text-sm font-normal text-destructive"
                            >
                              {duplicate
                                ? t('savedScreens.renameDuplicate', {
                                    name: serverName ?? editName.trim(),
                                  })
                                : t('savedScreens.renameErrorBody')}
                            </p>
                          ) : null}
                        </TableHead>
                        <TableCell className="align-top">
                          <div className="flex flex-wrap items-center gap-2">
                            <Button
                              type="button"
                              size="sm"
                              variant="outline"
                              data-testid={`rerun-${screen.id}`}
                              disabled={run.isPending}
                              aria-busy={run.isPending || undefined}
                              onClick={() => handleRerun(screen)}
                            >
                              <RefreshCw aria-hidden="true" />
                              {run.isPending
                                ? t('savedScreens.reRunning')
                                : t('savedScreens.reRun')}
                            </Button>
                            <Button
                              type="button"
                              size="sm"
                              variant="ghost"
                              data-testid={`rename-${screen.id}`}
                              onClick={() => startEdit(screen)}
                            >
                              <Pencil aria-hidden="true" />
                              {t('savedScreens.rename')}
                            </Button>
                            <Button
                              type="button"
                              size="sm"
                              variant="ghost"
                              data-testid={`delete-${screen.id}`}
                              onClick={() => setPendingDelete(screen)}
                            >
                              <Trash2 aria-hidden="true" />
                              {t('savedScreens.delete')}
                            </Button>
                          </div>
                        </TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
            </TableContainer>
          )}

          {remove.isError ? (
            <div
              role="alert"
              data-testid="delete-error"
              className="flex flex-col items-start gap-2 rounded-lg border border-dashed p-4"
            >
              <p className="font-medium text-destructive">
                {t('savedScreens.deleteErrorTitle')}
              </p>
              <p className="text-sm text-muted-foreground">
                {t('savedScreens.deleteErrorBody')}
              </p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => lastDelete && performDelete(lastDelete)}
              >
                {t('common.retry')}
              </Button>
            </div>
          ) : null}
        </>
      )}

      <ConfirmDialog
        open={pendingDelete !== null}
        onOpenChange={(open) => {
          if (!open) setPendingDelete(null)
        }}
        title={t('savedScreens.deleteTitle')}
        description={
          pendingDelete
            ? t('savedScreens.deleteDescription', { name: pendingDelete.name })
            : ''
        }
        confirmLabel={t('savedScreens.deleteConfirm')}
        cancelLabel={t('common.cancel')}
        closeLabel={t('common.close')}
        destructive
        pending={remove.isPending}
        onConfirm={confirmDelete}
      />
    </section>
  )
}

/** Anonymous direct access → sign-in path scoped to this feature (UXR-SCR-012). */
function AnonymousState() {
  const { t } = useTranslation()
  return (
    <div
      data-testid="saved-screens-anonymous"
      className="flex flex-col gap-2 rounded-lg border p-4"
    >
      <p className="font-medium">{t('savedScreens.anonymousTitle')}</p>
      <p className="text-sm text-muted-foreground">
        {t('savedScreens.anonymousBody')}
      </p>
      <div className="flex flex-wrap gap-3">
        <Link
          className={buttonVariants({ size: 'sm' })}
          to="/sign-in?returnUrl=%2Fscreens"
        >
          {t('savedScreens.signIn')}
        </Link>
        <Link
          className={buttonVariants({ size: 'sm', variant: 'outline' })}
          to="/register?returnUrl=%2Fscreens"
        >
          {t('savedScreens.register')}
        </Link>
      </div>
    </div>
  )
}
