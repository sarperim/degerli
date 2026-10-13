import { useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { FolderOpen, Pencil, Trash2 } from 'lucide-react'

import { Disclaimer } from '@/components/Disclaimer'
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
import type { SavedScenario } from '@/lib/api/contracts'
import { useSession } from '@/lib/auth/session'
import {
  useAllScenarios,
  useDeleteScenario,
  useRenameScenario,
} from '@/screens/dcf/scenariosApi'

/**
 * SCR-011 — My DCF Scenarios (UX `valuation-dcf.md` §SCR-011; UC-VAL-002).
 *
 * Lists the signed-in user's saved DCF scenarios grouped by stock (UXR-VAL-016),
 * each with per-row actions:
 *
 * - **Open** loads that stock's calculator with the scenario's assumptions and
 *   recomputes from them (UXR-VAL-016, FR-VAL-010) — the hand-off is the
 *   `?scenario=<id>` query param the calculator consumes.
 * - **Rename** in place (`PATCH /me/scenarios/{id}`); a duplicate name within the
 *   same stock is an inline error and the original is retained (UXR-VAL-017).
 * - **Delete** behind a focus-trapping confirmation stating the consequences
 *   (UXR-VAL-018); on success the row leaves the list and the calculator's picker
 *   without a manual reload (M-2, §8.4).
 *
 * Anonymous direct access presents the sign-in path scoped to this feature
 * (UXR-VAL-019); an account with no scenarios gets a guidance state (UXR-VAL-020).
 * As a research surface the screen carries the informational-only disclaimer
 * (UXR-G-016).
 */

interface ScenarioGroup {
  symbol: string
  scenarios: SavedScenario[]
}

/** Group scenarios by stock, ordered by symbol for a stable display. */
function groupBySymbol(scenarios: SavedScenario[]): ScenarioGroup[] {
  const groups = new Map<string, SavedScenario[]>()
  for (const scenario of scenarios) {
    const existing = groups.get(scenario.symbol)
    if (existing) existing.push(scenario)
    else groups.set(scenario.symbol, [scenario])
  }
  return [...groups.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([symbol, group]) => ({ symbol, scenarios: group }))
}

export function DcfScenariosScreen() {
  const { t } = useTranslation()

  const session = useSession()
  const authenticated = session.data?.authenticated === true
  const scenariosQuery = useAllScenarios(authenticated)

  const rename = useRenameScenario()
  const remove = useDeleteScenario()

  const [editingId, setEditingId] = useState<number | null>(null)
  const [editName, setEditName] = useState('')
  const [pendingDelete, setPendingDelete] = useState<SavedScenario | null>(null)
  const [lastDelete, setLastDelete] = useState<SavedScenario | null>(null)

  const scenarios = useMemo(
    () => scenariosQuery.data?.scenarios ?? [],
    [scenariosQuery.data],
  )
  const groups = useMemo(() => groupBySymbol(scenarios), [scenarios])

  function startEdit(scenario: SavedScenario) {
    rename.reset()
    setEditingId(scenario.id)
    setEditName(scenario.name)
  }

  function cancelEdit() {
    rename.reset()
    setEditingId(null)
    setEditName('')
  }

  function submitRename(event: FormEvent, scenario: SavedScenario) {
    event.preventDefault()
    const name = editName.trim()
    if (name === '' || name === scenario.name) {
      setEditingId(null)
      return
    }
    rename.mutate(
      { id: scenario.id, name },
      {
        onSuccess: () => {
          setEditingId(null)
          setEditName('')
        },
      },
    )
  }

  function performDelete(scenario: SavedScenario) {
    setLastDelete(scenario)
    remove.mutate(scenario, {
      onSuccess: () => setPendingDelete(null),
      // Close the confirmation so the plain-language error + retry is visible;
      // the row is retained on failure (UXR-G-003/004).
      onError: () => setPendingDelete(null),
    })
  }

  function confirmDelete() {
    if (pendingDelete) performDelete(pendingDelete)
  }

  return (
    <section
      data-screen="SCR-011"
      data-route-id="dcfScenarios"
      className="flex flex-col gap-6"
    >
      <header className="flex flex-col gap-2">
        <h1 className="text-h1 font-semibold tracking-tight">
          {t('screens.dcfScenarios.title')}
        </h1>
        <p className="max-w-2xl text-muted-foreground">
          {t('screens.dcfScenarios.description')}
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
          {scenariosQuery.isPending ? (
            <div role="status" aria-label={t('dcfScenarios.listLabel')}>
              <SkeletonTable rows={3} columns={2} />
            </div>
          ) : scenariosQuery.isError ? (
            <div
              role="alert"
              data-testid="dcf-scenarios-error"
              className="flex flex-col items-start gap-2 rounded-lg border border-dashed p-4"
            >
              <p className="font-medium">{t('dcfScenarios.listErrorTitle')}</p>
              <p className="text-sm text-muted-foreground">
                {t('dcfScenarios.listErrorBody')}
              </p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => void scenariosQuery.refetch()}
              >
                {t('common.retry')}
              </Button>
            </div>
          ) : scenarios.length === 0 ? (
            <EmptyState
              data-testid="dcf-scenarios-empty"
              icon={<FolderOpen />}
              title={t('dcfScenarios.emptyTitle')}
              description={t('dcfScenarios.emptyBody')}
              action={
                <Link className={buttonVariants({ size: 'sm' })} to="/stocks">
                  {t('dcfScenarios.emptyAction')}
                </Link>
              }
            />
          ) : (
            <div data-testid="dcf-scenarios-list" className="flex flex-col gap-8">
              {groups.map((group) => (
                <section
                  key={group.symbol}
                  data-testid={`dcf-scenarios-group-${group.symbol}`}
                  aria-labelledby={`dcf-scenarios-group-${group.symbol}-heading`}
                  className="flex flex-col gap-3"
                >
                  <h2
                    id={`dcf-scenarios-group-${group.symbol}-heading`}
                    data-testid={`dcf-scenarios-group-${group.symbol}-heading`}
                    className="text-h3 font-medium text-foreground"
                  >
                    {group.symbol}
                  </h2>
                  <TableContainer>
                    <Table>
                      <TableCaption>
                        {t('dcfScenarios.groupLabel', { symbol: group.symbol })}
                      </TableCaption>
                      <TableHeader>
                        <TableRow>
                          <TableHead>{t('dcfScenarios.columns.name')}</TableHead>
                          <TableHead>{t('dcfScenarios.columns.actions')}</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {group.scenarios.map((scenario) => {
                          const isEditing = editingId === scenario.id
                          const failedHere =
                            rename.isError && rename.variables?.id === scenario.id
                          const duplicate =
                            failedHere && isApiErrorCode(rename.error, 'DUPLICATE_NAME')
                          const serverName =
                            rename.error instanceof ApiError
                              ? (rename.error.problem?.params?.name as string | undefined)
                              : undefined
                          return (
                            <TableRow
                              key={scenario.id}
                              data-testid={`dcf-scenario-row-${scenario.id}`}
                            >
                              <TableHead
                                scope="row"
                                className="align-top font-medium text-foreground"
                              >
                                {isEditing ? (
                                  <form
                                    className="flex flex-wrap items-center gap-2"
                                    onSubmit={(event) => submitRename(event, scenario)}
                                  >
                                    <Input
                                      data-testid={`rename-input-${scenario.id}`}
                                      aria-label={t('dcfScenarios.renameInputLabel', {
                                        name: scenario.name,
                                      })}
                                      value={editName}
                                      disabled={rename.isPending}
                                      onChange={(event) => setEditName(event.target.value)}
                                      className="h-8 w-48"
                                    />
                                    <Button
                                      type="submit"
                                      size="sm"
                                      data-testid={`rename-save-${scenario.id}`}
                                      disabled={rename.isPending || editName.trim() === ''}
                                      aria-busy={rename.isPending || undefined}
                                    >
                                      {rename.isPending
                                        ? t('dcfScenarios.renaming')
                                        : t('dcfScenarios.renameSave')}
                                    </Button>
                                    <Button
                                      type="button"
                                      size="sm"
                                      variant="ghost"
                                      data-testid={`rename-cancel-${scenario.id}`}
                                      onClick={cancelEdit}
                                    >
                                      {t('dcfScenarios.renameCancel')}
                                    </Button>
                                  </form>
                                ) : (
                                  <span data-testid={`dcf-scenario-name-${scenario.id}`}>
                                    {scenario.name}
                                  </span>
                                )}
                                {failedHere ? (
                                  <p
                                    data-testid={`rename-error-${scenario.id}`}
                                    role="alert"
                                    className="mt-1 text-sm font-normal text-destructive"
                                  >
                                    {duplicate
                                      ? t('dcfScenarios.renameDuplicate', {
                                          name: serverName ?? editName.trim(),
                                        })
                                      : t('dcfScenarios.renameErrorBody')}
                                  </p>
                                ) : null}
                              </TableHead>
                              <TableCell className="align-top">
                                <div className="flex flex-wrap items-center gap-2">
                                  <Link
                                    className={buttonVariants({
                                      size: 'sm',
                                      variant: 'outline',
                                    })}
                                    data-testid={`dcf-scenario-open-${scenario.id}`}
                                    to={`/stocks/${scenario.symbol}/dcf?scenario=${scenario.id}`}
                                    aria-label={t('dcfScenarios.openLabel', {
                                      name: scenario.name,
                                    })}
                                  >
                                    <FolderOpen aria-hidden="true" />
                                    {t('dcfScenarios.open')}
                                  </Link>
                                  <Button
                                    type="button"
                                    size="sm"
                                    variant="ghost"
                                    data-testid={`rename-${scenario.id}`}
                                    aria-label={t('dcfScenarios.renameLabel', {
                                      name: scenario.name,
                                    })}
                                    onClick={() => startEdit(scenario)}
                                  >
                                    <Pencil aria-hidden="true" />
                                    {t('dcfScenarios.rename')}
                                  </Button>
                                  <Button
                                    type="button"
                                    size="sm"
                                    variant="ghost"
                                    data-testid={`delete-${scenario.id}`}
                                    aria-label={t('dcfScenarios.deleteLabel', {
                                      name: scenario.name,
                                    })}
                                    onClick={() => setPendingDelete(scenario)}
                                  >
                                    <Trash2 aria-hidden="true" />
                                    {t('dcfScenarios.delete')}
                                  </Button>
                                </div>
                              </TableCell>
                            </TableRow>
                          )
                        })}
                      </TableBody>
                    </Table>
                  </TableContainer>
                </section>
              ))}
            </div>
          )}

          {remove.isError ? (
            <div
              role="alert"
              data-testid="dcf-scenarios-delete-error"
              className="flex flex-col items-start gap-2 rounded-lg border border-dashed p-4"
            >
              <p className="font-medium text-destructive">
                {t('dcfScenarios.deleteErrorTitle')}
              </p>
              <p className="text-sm text-muted-foreground">
                {t('dcfScenarios.deleteErrorBody')}
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
        title={t('dcfScenarios.deleteTitle')}
        description={
          pendingDelete
            ? t('dcfScenarios.deleteDescription', { name: pendingDelete.name })
            : ''
        }
        confirmLabel={t('dcfScenarios.deleteConfirm')}
        cancelLabel={t('common.cancel')}
        closeLabel={t('common.close')}
        destructive
        pending={remove.isPending}
        onConfirm={confirmDelete}
      />

      <Disclaimer />
    </section>
  )
}

/** Anonymous direct access → sign-in path scoped to this feature (UXR-VAL-019). */
function AnonymousState() {
  const { t } = useTranslation()
  return (
    <div
      data-testid="dcf-scenarios-anonymous"
      className="flex flex-col gap-2 rounded-lg border p-4"
    >
      <p className="font-medium">{t('dcfScenarios.anonymousTitle')}</p>
      <p className="text-sm text-muted-foreground">
        {t('dcfScenarios.anonymousBody')}
      </p>
      <div className="flex flex-wrap gap-3">
        <Link
          className={buttonVariants({ size: 'sm' })}
          to="/sign-in?returnUrl=%2Fscenarios"
        >
          {t('dcfScenarios.signIn')}
        </Link>
        <Link
          className={buttonVariants({ size: 'sm', variant: 'outline' })}
          to="/register?returnUrl=%2Fscenarios"
        >
          {t('dcfScenarios.register')}
        </Link>
      </div>
    </div>
  )
}
