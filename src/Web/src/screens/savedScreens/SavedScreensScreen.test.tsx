import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import { formatAsOfDate } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import {
  ANONYMOUS_SESSION_FIXTURE,
  API_BASE,
  SCREENER_RUN_FIXTURE,
  SAVED_SCREENS_FIXTURE,
  problemHandler,
  savedScreensListHandler,
  screenerRunHandler,
  sessionHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import {
  fill,
  renderSavedScreens,
  resetSavedScreensTest,
} from '@/screens/savedScreens/testSupport'
import { useDraftStore } from '@/store/draftStore'

/**
 * TC-SCR-030 — My Saved Screens management (critical flows 1–4 of SCR-004, L4).
 *
 * Traces FR-SCR-008..011, UXR-SCR-012..018, UC-SCR-003. Decision table over the
 * list surface: signed-in list by name; re-run presents *fresh* results in the
 * screener context (current as-of, never a stored result set); rename updates in
 * place with an inline duplicate error that retains the original; delete sits
 * behind a focus-trapping confirmation stating the consequences; anonymous direct
 * access presents the sign-in path; an empty account gets a guidance state.
 */

const TR = (key: string) => catalogValue('tr', key)

beforeEach(() => resetSavedScreensTest())

describe('TC-SCR-030 — My Saved Screens list', () => {
  it('lists the signed-in user’s saved screens by name', async () => {
    renderSavedScreens()

    const list = await screen.findByTestId('saved-screens-list')
    expect(within(list).getByTestId('saved-screen-name-1')).toHaveTextContent('Ucuz kaliteli')
    expect(within(list).getByTestId('saved-screen-name-2')).toHaveTextContent('Büyüme')
  })

  it('presents the sign-in path on anonymous direct access', async () => {
    server.use(sessionHandler(ANONYMOUS_SESSION_FIXTURE))

    renderSavedScreens()

    const anonymous = await screen.findByTestId('saved-screens-anonymous')
    expect(
      within(anonymous).getByRole('link', { name: TR('savedScreens.signIn') }),
    ).toHaveAttribute('href', '/sign-in?returnUrl=%2Fscreens')
    // No list request is made while anonymous (the sign-in path is scoped to this feature).
    expect(screen.queryByTestId('saved-screens-list')).not.toBeInTheDocument()
  })

  it('shows a guidance empty state when the account has no saved screens', async () => {
    server.use(savedScreensListHandler([]))

    renderSavedScreens()

    const empty = await screen.findByTestId('saved-screens-empty')
    expect(empty).toHaveTextContent(TR('savedScreens.emptyTitle'))
    expect(empty).toHaveTextContent(TR('savedScreens.emptyBody'))
    expect(
      within(empty).getByRole('link', { name: TR('savedScreens.emptyAction') }),
    ).toHaveAttribute('href', '/screener')
  })

  it('shows a plain-language error with retry when the list fails to load', async () => {
    server.use(problemHandler(`${API_BASE}/me/screens`, 'INTERNAL'))

    renderSavedScreens()

    const error = await screen.findByTestId('saved-screens-error')
    expect(error).toHaveTextContent(TR('savedScreens.listErrorTitle'))
    expect(screen.getByRole('button', { name: TR('common.retry') })).toBeInTheDocument()
  })
})

describe('TC-SCR-030 — re-run on fresh data', () => {
  it('re-runs with the stored criteria and presents fresh results in the screener context', async () => {
    const user = userEvent.setup()
    let captured: { criteria: unknown } | null = null
    server.use(
      http.post(`${API_BASE}/screener/run`, async ({ request }) => {
        captured = (await request.json()) as { criteria: unknown }
        return HttpResponse.json(SCREENER_RUN_FIXTURE)
      }),
    )

    renderSavedScreens()
    await screen.findByTestId('saved-screens-list')

    await user.click(screen.getByTestId('rerun-1'))

    // The screener runs with the saved criteria (never a stored result set) …
    await waitFor(() => expect(captured).not.toBeNull())
    expect(captured!.criteria).toEqual(SAVED_SCREENS_FIXTURE[0].criteria)

    // … and the fresh results are presented in the screener context with the
    // current as-of date and the screen shown as active.
    expect(await screen.findByTestId('results')).toBeInTheDocument()
    expect(screen.getByTestId('results-asof')).toHaveTextContent(
      fill(TR('honestData.asOf'), {
        date: formatAsOfDate('tr', SCREENER_RUN_FIXTURE.asOf),
      }),
    )
    expect(screen.getByTestId('match-count')).toHaveTextContent(
      fill(TR('screener.matchCount'), { count: 3 }),
    )
    expect(screen.getByTestId('active-screen')).toHaveTextContent(
      fill(TR('screener.activeScreenLabel'), { name: 'Ucuz kaliteli' }),
    )
  })

  it('reports the criterion dropped on re-run (UXR-SCR-015)', async () => {
    const user = userEvent.setup()
    server.use(
      screenerRunHandler({ ...SCREENER_RUN_FIXTURE, droppedCriteria: ['roe'] }),
    )

    renderSavedScreens()
    await screen.findByTestId('saved-screens-list')

    await user.click(screen.getByTestId('rerun-1'))

    const notice = await screen.findByTestId('dropped-criteria')
    expect(notice).toHaveTextContent(
      fill(TR('screener.droppedCriteria'), { criteria: 'Özkaynak Kârlılığı' }),
    )
  })

  it('keeps the list unchanged and offers retry when a re-run fails', async () => {
    const user = userEvent.setup()
    server.use(problemHandler(`${API_BASE}/screener/run`, 'INTERNAL', { method: 'post' }))

    renderSavedScreens()
    await screen.findByTestId('saved-screens-list')

    await user.click(screen.getByTestId('rerun-1'))

    const error = await screen.findByTestId('rerun-error')
    expect(error).toHaveTextContent(TR('savedScreens.reRunErrorTitle'))
    // Still on the list, unchanged.
    expect(screen.getByTestId('saved-screen-name-1')).toHaveTextContent('Ucuz kaliteli')
  })
})

describe('TC-SCR-030 — rename in place', () => {
  it('renames a screen in place from the mutation response', async () => {
    const user = userEvent.setup()
    let patchCalls = 0
    server.use(
      http.patch(`${API_BASE}/me/screens/:id`, () => {
        patchCalls += 1
        return HttpResponse.json({
          ...SAVED_SCREENS_FIXTURE[0],
          name: 'Yeni ad',
        })
      }),
    )

    renderSavedScreens()
    await screen.findByTestId('saved-screens-list')

    await user.click(screen.getByTestId('rename-1'))
    const input = screen.getByTestId('rename-input-1')
    await user.clear(input)
    await user.type(input, 'Yeni ad')
    await user.click(screen.getByTestId('rename-save-1'))

    await waitFor(() =>
      expect(screen.getByTestId('saved-screen-name-1')).toHaveTextContent('Yeni ad'),
    )
    expect(patchCalls).toBe(1)
  })

  it('rejects a rename to a duplicate name inline, retaining the original', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/screens/:id`, 'DUPLICATE_NAME', {
        method: 'patch',
        params: { name: 'Büyüme' },
      }),
    )

    renderSavedScreens()
    await screen.findByTestId('saved-screens-list')

    await user.click(screen.getByTestId('rename-1'))
    const input = screen.getByTestId('rename-input-1')
    await user.clear(input)
    await user.type(input, 'Büyüme')
    await user.click(screen.getByTestId('rename-save-1'))

    const error = await screen.findByTestId('rename-error-1')
    expect(error).toHaveTextContent(
      fill(TR('savedScreens.renameDuplicate'), { name: 'Büyüme' }),
    )
    // The original name is retained — leaving the edit shows it unchanged.
    await user.click(screen.getByTestId('rename-cancel-1'))
    expect(screen.getByTestId('saved-screen-name-1')).toHaveTextContent('Ucuz kaliteli')
  })

  it('shows a plain-language error and retains the original on a rename failure', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/screens/:id`, 'INTERNAL', { method: 'patch' }),
    )

    renderSavedScreens()
    await screen.findByTestId('saved-screens-list')

    await user.click(screen.getByTestId('rename-1'))
    const input = screen.getByTestId('rename-input-1')
    await user.clear(input)
    await user.type(input, 'Yeni ad')
    await user.click(screen.getByTestId('rename-save-1'))

    const error = await screen.findByTestId('rename-error-1')
    expect(error).toHaveTextContent(TR('savedScreens.renameErrorBody'))
    // The original name is still the row’s name (leaving the edit shows it).
    await user.click(screen.getByTestId('rename-cancel-1'))
    expect(screen.getByTestId('saved-screen-name-1')).toHaveTextContent('Ucuz kaliteli')
  })
})

describe('TC-SCR-030 — delete behind a confirmation', () => {
  it('requires a confirmation stating the consequences, then removes the row', async () => {
    const user = userEvent.setup()
    let deleteCalls = 0
    server.use(
      http.delete(`${API_BASE}/me/screens/:id`, () => {
        deleteCalls += 1
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderSavedScreens()
    await screen.findByTestId('saved-screens-list')

    await user.click(screen.getByTestId('delete-1'))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveTextContent(
      fill(TR('savedScreens.deleteDescription'), { name: 'Ucuz kaliteli' }),
    )

    await user.click(within(dialog).getByRole('button', { name: TR('savedScreens.deleteConfirm') }))

    await waitFor(() =>
      expect(screen.queryByTestId('saved-screen-row-1')).not.toBeInTheDocument(),
    )
    expect(screen.getByTestId('saved-screen-row-2')).toBeInTheDocument()
    expect(deleteCalls).toBe(1)
  })

  it('retains the row when the confirmation is cancelled', async () => {
    const user = userEvent.setup()
    let deleteCalls = 0
    server.use(
      http.delete(`${API_BASE}/me/screens/:id`, () => {
        deleteCalls += 1
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderSavedScreens()
    await screen.findByTestId('saved-screens-list')

    await user.click(screen.getByTestId('delete-1'))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: TR('common.cancel') }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByTestId('saved-screen-row-1')).toBeInTheDocument()
    expect(deleteCalls).toBe(0)
  })

  it('clears the active-screen reference when the active screen is deleted', async () => {
    const user = userEvent.setup()

    renderSavedScreens()
    await screen.findByTestId('saved-screens-list')

    // Simulate screen 1 being the active screen in the screener context.
    useDraftStore.getState().setActiveScreen(1, 'Ucuz kaliteli')

    await user.click(screen.getByTestId('delete-1'))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: TR('savedScreens.deleteConfirm') }))

    await waitFor(() =>
      expect(useDraftStore.getState().activeScreenId).toBeNull(),
    )
    expect(useDraftStore.getState().activeScreenName).toBe('')
  })

  it('keeps the row and offers retry when deletion fails', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/screens/:id`, 'INTERNAL', { method: 'delete' }),
    )

    renderSavedScreens()
    await screen.findByTestId('saved-screens-list')

    await user.click(screen.getByTestId('delete-1'))
    const dialog = await screen.findByRole('dialog')
    await user.click(
      within(dialog).getByRole('button', { name: TR('savedScreens.deleteConfirm') }),
    )

    const error = await screen.findByTestId('delete-error')
    expect(error).toHaveTextContent(TR('savedScreens.deleteErrorTitle'))
    expect(
      within(error).getByRole('button', { name: TR('common.retry') }),
    ).toBeInTheDocument()
    expect(screen.getByTestId('saved-screen-row-1')).toBeInTheDocument()
  })
})
