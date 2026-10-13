import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import type { SavedScenario } from '@/lib/api/contracts'
import { catalogValue } from '@/test/i18n'
import {
  ANONYMOUS_SESSION_FIXTURE,
  API_BASE,
  DCF_SCENARIO_ALFA,
  problemHandler,
  scenariosListHandler,
  sessionHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { fill, renderScenarios, resetScenariosTest } from '@/screens/dcfScenarios/testSupport'

/**
 * TC-VAL-022 — My DCF Scenarios list (L3, SCR-011).
 *
 * Traces UXR-VAL-016..020, SCR-011. Covers the grouped/empty/anonymous classes
 * of the signed-in scenarios surface: scenarios grouped by stock, activating one
 * opens that stock's calculator loaded with it, rename in place with collision
 * rejection, delete behind confirmation, anonymous sign-in path, empty guidance.
 */

const TR = (key: string) => catalogValue('tr', key)

/** A second ALFA scenario (same stock, distinct name) for grouping/collision. */
const ALFA_AGGRESSIVE: SavedScenario = {
  ...DCF_SCENARIO_ALFA,
  id: 2,
  name: 'Agresif',
}

/** A BETA scenario, so the list must group across stocks. */
const BETA_BASE: SavedScenario = {
  id: 3,
  symbol: 'BETA',
  name: 'Beta temel',
  params: DCF_SCENARIO_ALFA.params,
  createdAt: '2026-10-06T20:30:00Z',
  updatedAt: '2026-10-06T20:30:00Z',
}

const GROUPED: SavedScenario[] = [DCF_SCENARIO_ALFA, ALFA_AGGRESSIVE, BETA_BASE]

beforeEach(() => resetScenariosTest())

describe('TC-VAL-022 — My DCF Scenarios list', () => {
  it('lists the signed-in user’s scenarios grouped by stock', async () => {
    server.use(scenariosListHandler(GROUPED))

    renderScenarios()

    const list = await screen.findByTestId('dcf-scenarios-list')
    const alfa = within(list).getByTestId('dcf-scenarios-group-ALFA')
    expect(within(alfa).getByTestId('dcf-scenario-name-1')).toHaveTextContent('Muhafazakâr')
    expect(within(alfa).getByTestId('dcf-scenario-name-2')).toHaveTextContent('Agresif')

    const beta = within(list).getByTestId('dcf-scenarios-group-BETA')
    expect(within(beta).getByTestId('dcf-scenario-name-3')).toHaveTextContent('Beta temel')
  })

  it('offers an open action that loads the stock’s calculator with the scenario', async () => {
    server.use(scenariosListHandler(GROUPED))

    renderScenarios()

    const open = await screen.findByTestId('dcf-scenario-open-1')
    expect(open).toHaveAttribute('href', '/stocks/ALFA/dcf?scenario=1')
  })

  it('presents the sign-in path on anonymous direct access', async () => {
    server.use(sessionHandler(ANONYMOUS_SESSION_FIXTURE))

    renderScenarios()

    const anonymous = await screen.findByTestId('dcf-scenarios-anonymous')
    expect(
      within(anonymous).getByRole('link', { name: TR('dcfScenarios.signIn') }),
    ).toHaveAttribute('href', '/sign-in?returnUrl=%2Fscenarios')
    // No list request is made while anonymous (the sign-in path is scoped to this feature).
    expect(screen.queryByTestId('dcf-scenarios-list')).not.toBeInTheDocument()
  })

  it('shows a guidance empty state explaining how to create one in the calculator', async () => {
    server.use(scenariosListHandler([]))

    renderScenarios()

    const empty = await screen.findByTestId('dcf-scenarios-empty')
    expect(empty).toHaveTextContent(TR('dcfScenarios.emptyTitle'))
    expect(empty).toHaveTextContent(TR('dcfScenarios.emptyBody'))
    expect(
      within(empty).getByRole('link', { name: TR('dcfScenarios.emptyAction') }),
    ).toHaveAttribute('href', '/stocks')
  })

  it('shows a plain-language error with retry when the list fails to load', async () => {
    server.use(problemHandler(`${API_BASE}/me/scenarios`, 'INTERNAL'))

    renderScenarios()

    const error = await screen.findByTestId('dcf-scenarios-error')
    expect(error).toHaveTextContent(TR('dcfScenarios.listErrorTitle'))
    expect(
      within(error).getByRole('button', { name: TR('common.retry') }),
    ).toBeInTheDocument()
  })
})

describe('TC-VAL-022 — rename in place', () => {
  it('renames a scenario in place from the mutation response', async () => {
    const user = userEvent.setup()
    let patchCalls = 0
    server.use(
      scenariosListHandler(GROUPED),
      http.patch(`${API_BASE}/me/scenarios/:id`, () => {
        patchCalls += 1
        return HttpResponse.json({ ...DCF_SCENARIO_ALFA, name: 'Yeni ad' })
      }),
    )

    renderScenarios()
    await screen.findByTestId('dcf-scenarios-list')

    await user.click(screen.getByTestId('rename-1'))
    const input = screen.getByTestId('rename-input-1')
    await user.clear(input)
    await user.type(input, 'Yeni ad')
    await user.click(screen.getByTestId('rename-save-1'))

    await waitFor(() =>
      expect(screen.getByTestId('dcf-scenario-name-1')).toHaveTextContent('Yeni ad'),
    )
    expect(patchCalls).toBe(1)
  })

  it('rejects a rename to a duplicate within the same stock inline, retaining the original', async () => {
    const user = userEvent.setup()
    server.use(
      scenariosListHandler(GROUPED),
      problemHandler(`${API_BASE}/me/scenarios/:id`, 'DUPLICATE_NAME', {
        method: 'patch',
        params: { name: 'Agresif' },
      }),
    )

    renderScenarios()
    await screen.findByTestId('dcf-scenarios-list')

    await user.click(screen.getByTestId('rename-1'))
    const input = screen.getByTestId('rename-input-1')
    await user.clear(input)
    await user.type(input, 'Agresif')
    await user.click(screen.getByTestId('rename-save-1'))

    const error = await screen.findByTestId('rename-error-1')
    expect(error).toHaveTextContent(
      fill(TR('dcfScenarios.renameDuplicate'), { name: 'Agresif' }),
    )
    // The original name is retained — leaving the edit shows it unchanged.
    await user.click(screen.getByTestId('rename-cancel-1'))
    expect(screen.getByTestId('dcf-scenario-name-1')).toHaveTextContent('Muhafazakâr')
  })

  it('shows a plain-language error and retains the original on a rename failure', async () => {
    const user = userEvent.setup()
    server.use(
      scenariosListHandler(GROUPED),
      problemHandler(`${API_BASE}/me/scenarios/:id`, 'INTERNAL', { method: 'patch' }),
    )

    renderScenarios()
    await screen.findByTestId('dcf-scenarios-list')

    await user.click(screen.getByTestId('rename-1'))
    const input = screen.getByTestId('rename-input-1')
    await user.clear(input)
    await user.type(input, 'Yeni ad')
    await user.click(screen.getByTestId('rename-save-1'))

    const error = await screen.findByTestId('rename-error-1')
    expect(error).toHaveTextContent(TR('dcfScenarios.renameErrorBody'))
    await user.click(screen.getByTestId('rename-cancel-1'))
    expect(screen.getByTestId('dcf-scenario-name-1')).toHaveTextContent('Muhafazakâr')
  })
})

describe('TC-VAL-022 — delete behind a confirmation', () => {
  it('requires a confirmation stating the consequences, then removes the row', async () => {
    const user = userEvent.setup()
    let deleteCalls = 0
    server.use(
      scenariosListHandler(GROUPED),
      http.delete(`${API_BASE}/me/scenarios/:id`, () => {
        deleteCalls += 1
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderScenarios()
    await screen.findByTestId('dcf-scenarios-list')

    await user.click(screen.getByTestId('delete-1'))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveTextContent(
      fill(TR('dcfScenarios.deleteDescription'), { name: 'Muhafazakâr' }),
    )

    await user.click(
      within(dialog).getByRole('button', { name: TR('dcfScenarios.deleteConfirm') }),
    )

    await waitFor(() =>
      expect(screen.queryByTestId('dcf-scenario-row-1')).not.toBeInTheDocument(),
    )
    expect(screen.getByTestId('dcf-scenario-row-2')).toBeInTheDocument()
    expect(deleteCalls).toBe(1)
  })

  it('retains the row when the confirmation is cancelled', async () => {
    const user = userEvent.setup()
    let deleteCalls = 0
    server.use(
      scenariosListHandler(GROUPED),
      http.delete(`${API_BASE}/me/scenarios/:id`, () => {
        deleteCalls += 1
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderScenarios()
    await screen.findByTestId('dcf-scenarios-list')

    await user.click(screen.getByTestId('delete-1'))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: TR('common.cancel') }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByTestId('dcf-scenario-row-1')).toBeInTheDocument()
    expect(deleteCalls).toBe(0)
  })

  it('keeps the row and offers retry when deletion fails', async () => {
    const user = userEvent.setup()
    server.use(
      scenariosListHandler(GROUPED),
      problemHandler(`${API_BASE}/me/scenarios/:id`, 'INTERNAL', { method: 'delete' }),
    )

    renderScenarios()
    await screen.findByTestId('dcf-scenarios-list')

    await user.click(screen.getByTestId('delete-1'))
    const dialog = await screen.findByRole('dialog')
    await user.click(
      within(dialog).getByRole('button', { name: TR('dcfScenarios.deleteConfirm') }),
    )

    const error = await screen.findByTestId('dcf-scenarios-delete-error')
    expect(error).toHaveTextContent(TR('dcfScenarios.deleteErrorTitle'))
    expect(
      within(error).getByRole('button', { name: TR('common.retry') }),
    ).toBeInTheDocument()
    expect(screen.getByTestId('dcf-scenario-row-1')).toBeInTheDocument()
  })
})
