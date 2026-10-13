import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import type {
  DcfComputeRequest,
  DcfComputeResponse,
  SavedScenario,
} from '@/lib/api/contracts'
import { formatDecimal } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import {
  API_BASE,
  DCF_COMPUTE_FIXTURE,
  DCF_SCENARIO_ALFA,
  dcfComputeHandler,
  problemHandler,
  scenariosListHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { fill, renderScenarios, resetScenariosTest } from '@/screens/dcfScenarios/testSupport'

/**
 * TC-VAL-027 — My DCF Scenarios management (L4, SCR-011 flows).
 *
 * Traces UXR-VAL-016..020, UC-VAL-002. Cross-screen: opening a scenario from
 * SCR-011 loads its stock's calculator with the scenario's assumptions and
 * recomputes; a rename to a duplicate within the stock is rejected inline with
 * the original retained; a confirmed delete removes the scenario from the list
 * and from the calculator's picker without a manual reload (M-2, §8.4).
 */

const TR = (key: string) => catalogValue('tr', key)

/** A second ALFA scenario, so a rename can collide within the same stock. */
const ALFA_AGGRESSIVE: SavedScenario = {
  ...DCF_SCENARIO_ALFA,
  id: 2,
  name: 'Agresif',
}

/** An alternate compute result (≥14% discount → positive MoS), for the override. */
const ALT_COMPUTE: DcfComputeResponse = {
  fairValuePerShare: 25,
  marginOfSafety: 0.2,
  price: DCF_COMPUTE_FIXTURE.price,
  sensitivity: DCF_COMPUTE_FIXTURE.sensitivity,
}

const RECOMPUTE_ON_OVERRIDE = (body: DcfComputeRequest) =>
  body.discount_rate >= 0.14 ? ALT_COMPUTE : DCF_COMPUTE_FIXTURE

beforeEach(() => resetScenariosTest())

describe('TC-VAL-027 — open a scenario into the calculator', () => {
  it('opens the stock’s calculator loaded with the scenario and recomputes', async () => {
    const user = userEvent.setup()
    const bodies: DcfComputeRequest[] = []
    server.use(
      scenariosListHandler([DCF_SCENARIO_ALFA, ALFA_AGGRESSIVE]),
      dcfComputeHandler((body) => {
        bodies.push(body)
        return RECOMPUTE_ON_OVERRIDE(body)
      }),
    )

    renderScenarios()
    await screen.findByTestId('dcf-scenarios-list')

    await user.click(screen.getByTestId('dcf-scenario-open-1'))

    // The calculator mounts with every parameter restored exactly as saved …
    await screen.findByTestId('dcf-params')
    await waitFor(() =>
      expect(screen.getByTestId('dcf-param-discount_rate')).toHaveValue('15'),
    )

    // … and recomputes from those params (the ≥14% override result).
    await waitFor(() =>
      expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
        formatDecimal('tr', 25, 4, 2),
      ),
    )
    expect(
      bodies.some((body) => Math.abs(body.discount_rate - 0.15) < 1e-9),
    ).toBe(true)
  })
})

describe('TC-VAL-027 — rename with collision', () => {
  it('rejects a rename to a duplicate within the stock inline, retaining the original', async () => {
    const user = userEvent.setup()
    server.use(
      scenariosListHandler([DCF_SCENARIO_ALFA, ALFA_AGGRESSIVE]),
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

    await user.click(screen.getByTestId('rename-cancel-1'))
    expect(screen.getByTestId('dcf-scenario-name-1')).toHaveTextContent('Muhafazakâr')
  })
})

describe('TC-VAL-027 — delete with confirmation', () => {
  it('removes the scenario from the list and the calculator’s picker', async () => {
    const user = userEvent.setup()
    const store: SavedScenario[] = [DCF_SCENARIO_ALFA]
    server.use(
      scenariosListHandler(() => store),
      http.delete(`${API_BASE}/me/scenarios/:id`, ({ params }) => {
        const id = Number(params.id)
        const index = store.findIndex((scenario) => scenario.id === id)
        if (index >= 0) store.splice(index, 1)
        return new HttpResponse(null, { status: 204 })
      }),
      dcfComputeHandler(),
    )

    // Start on the calculator so its picker cache is populated first.
    renderScenarios(['/stocks/ALFA/dcf'])
    await screen.findByTestId('dcf-params')
    await screen.findByTestId('dcf-scenario-item-1')

    // From SCR-011, delete the scenario behind its confirmation.
    await user.click(screen.getByTestId('go-scenarios'))
    await screen.findByTestId('dcf-scenarios-list')
    await user.click(screen.getByTestId('delete-1'))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent(TR('dcfScenarios.deleteTitle'))
    await user.click(
      within(dialog).getByRole('button', { name: TR('dcfScenarios.deleteConfirm') }),
    )
    await waitFor(() =>
      expect(screen.queryByTestId('dcf-scenario-row-1')).not.toBeInTheDocument(),
    )

    // Back on the calculator, the invalidated picker no longer shows it (M-2).
    await user.click(screen.getByTestId('go-calculator'))
    await screen.findByTestId('dcf-params')
    await waitFor(() =>
      expect(screen.queryByTestId('dcf-scenario-item-1')).not.toBeInTheDocument(),
    )
    expect(await screen.findByTestId('dcf-scenario-picker-empty')).toBeInTheDocument()
  })
})
