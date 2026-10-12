import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import type {
  DcfBaselineResponse,
  DcfComputeRequest,
  DcfComputeResponse,
  DcfParams,
  SavedScenario,
} from '@/lib/api/contracts'
import { formatDecimal } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import {
  API_BASE,
  DCF_COMPUTE_FIXTURE,
  DCF_SCENARIO_ALFA,
  dcfBaselineHandler,
  dcfComputeHandler,
  problemHandler,
  scenariosListHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderDcf, resetDcfTest } from '@/screens/dcf/testSupport'

/**
 * SCR-006 scenario flows (TKT-val-007).
 *
 * TC-VAL-021 — Save/load scenario flows (L3): anonymous/unverified/duplicate/
 *   success save outcomes; load restores params + recomputes; load over unsaved
 *   changes requires the discard confirmation; pending locks; failures preserve
 *   all input.
 * TC-VAL-025 — Load exactly; unsaved-changes guard (L4, critical flows 3–4).
 * TC-VAL-026 — Missing inputs (L4, critical flow 5).
 */

const TR = (key: string) => catalogValue('tr', key)

/** Interpolate an i18n template the way i18next would. */
function fill(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{\{(\w+)\}\}/g, (_match, key: string) =>
    String(vars[key] ?? ''),
  )
}

/** An alternate compute response (0.15 discount rate → positive MoS). */
const ALT_COMPUTE: DcfComputeResponse = {
  fairValuePerShare: 25,
  marginOfSafety: 0.2,
  price: { value: 20, asOf: '2026-10-06', stale: false },
  sensitivity: DCF_COMPUTE_FIXTURE.sensitivity,
}

/** Derive the response from the posted params: ≥14% → the alternate result. */
const RECOMPUTE_ON_OVERRIDE = (body: DcfComputeRequest) =>
  body.discount_rate >= 0.14 ? ALT_COMPUTE : DCF_COMPUTE_FIXTURE

/**
 * PART's baseline (TC-VAL-008/026): no cash-flow statement, so the CF-derived
 * base FCF is missing and not zero-filled; the other inputs are served.
 */
const PART_BASELINE: DcfBaselineResponse = {
  symbol: 'PART',
  baseline: {
    version: 1,
    buildDate: '2026-10-05',
    params: {
      growth_rate: 0.1,
      horizon_years: 5,
      terminal_growth: 0.03,
      discount_rate: 0.12,
      debt: 350,
      cash: 150,
      share_count: 50,
    } as unknown as DcfParams,
    canonicalFactRefs: {
      debt: { asOf: '2026-09-30', restated: false },
      cash: { asOf: '2026-09-30', restated: false },
      share_count: { asOf: '2026-09-30', restated: false },
    },
  },
  price: { value: 5, asOf: '2026-10-06', stale: false },
  state: 'missing_inputs',
  missingInputs: ['base_fcf'],
}

beforeEach(resetDcfTest)

describe('TC-VAL-021 — save/load scenario flows', () => {
  it('prompts anonymous users to sign in or register, preserving assumptions and name', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/scenarios`, 'UNAUTHENTICATED', { method: 'post' }),
    )

    renderDcf()
    await screen.findByTestId('dcf-params')

    const rate = screen.getByTestId('dcf-param-discount_rate')
    await user.clear(rate)
    await user.type(rate, '14')
    await user.type(screen.getByTestId('dcf-scenario-name'), 'Benim senaryom')
    await user.click(screen.getByTestId('dcf-scenario-save'))

    const prompt = await screen.findByTestId('dcf-scenario-anonymous')
    expect(prompt).toHaveTextContent(TR('dcf.scenario.anonymousTitle'))
    expect(
      within(prompt).getByRole('link', { name: TR('dcf.scenario.signIn') }),
    ).toHaveAttribute('href', '/sign-in?returnUrl=%2Fstocks%2FALFA%2Fdcf')
    expect(
      within(prompt).getByRole('link', { name: TR('dcf.scenario.register') }),
    ).toHaveAttribute('href', '/register?returnUrl=%2Fstocks%2FALFA%2Fdcf')

    // A prompt, not an error; the assumptions and the entered name are preserved.
    expect(screen.queryByTestId('dcf-scenario-error')).not.toBeInTheDocument()
    expect(screen.getByTestId('dcf-scenario-name')).toHaveValue('Benim senaryom')
    expect(screen.getByTestId('dcf-param-discount_rate')).toHaveValue('14')
  })

  it('blocks an unverified account with a verify-your-e-mail message and path', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/scenarios`, 'EMAIL_NOT_VERIFIED', { method: 'post' }),
    )

    renderDcf()
    await screen.findByTestId('dcf-params')
    await user.type(screen.getByTestId('dcf-scenario-name'), 'Benim senaryom')
    await user.click(screen.getByTestId('dcf-scenario-save'))

    const gate = await screen.findByTestId('dcf-scenario-unverified')
    expect(gate).toHaveTextContent(TR('dcf.scenario.unverifiedTitle'))
    expect(gate).toHaveTextContent(TR('dcf.scenario.unverifiedBody'))
    expect(
      within(gate).getByRole('link', { name: TR('dcf.scenario.unverifiedLink') }),
    ).toHaveAttribute('href', '/settings')
    // Assumptions and name preserved.
    expect(screen.getByTestId('dcf-scenario-name')).toHaveValue('Benim senaryom')
  })

  it('shows an inline duplicate-name error per stock, losing nothing', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/scenarios`, 'DUPLICATE_NAME', {
        method: 'post',
        params: { name: 'Benim senaryom' },
      }),
    )

    renderDcf()
    await screen.findByTestId('dcf-params')
    await user.type(screen.getByTestId('dcf-scenario-name'), 'Benim senaryom')
    await user.click(screen.getByTestId('dcf-scenario-save'))

    const error = await screen.findByTestId('dcf-scenario-duplicate')
    expect(error).toHaveTextContent(
      fill(TR('dcf.scenario.duplicateName'), { name: 'Benim senaryom' }),
    )
    expect(screen.getByTestId('dcf-scenario-name')).toHaveValue('Benim senaryom')
  })

  it('confirms a successful save and shows the scenario in the picker', async () => {
    const user = userEvent.setup()
    const store: SavedScenario[] = []
    server.use(
      scenariosListHandler(() => store),
      http.post(`${API_BASE}/me/scenarios`, async ({ request }) => {
        const body = (await request.json()) as {
          symbol: string
          name: string
          params: DcfParams
        }
        const created: SavedScenario = {
          id: 1,
          symbol: body.symbol,
          name: body.name,
          params: body.params,
          createdAt: '2026-10-06T20:00:00Z',
          updatedAt: '2026-10-06T20:00:00Z',
        }
        store.push(created)
        return HttpResponse.json(created, { status: 201 })
      }),
    )

    renderDcf()
    await screen.findByTestId('dcf-params')
    expect(await screen.findByTestId('dcf-scenario-picker-empty')).toBeInTheDocument()

    await user.type(screen.getByTestId('dcf-scenario-name'), 'Benim senaryom')
    await user.click(screen.getByTestId('dcf-scenario-save'))

    const confirmation = await screen.findByTestId('dcf-scenario-saved')
    expect(confirmation).toHaveTextContent(
      fill(TR('dcf.scenario.savedConfirmation'), { name: 'Benim senaryom' }),
    )
    // Saved → the per-stock picker refreshes without a manual reload (M-2 §8.4).
    expect(await screen.findByTestId('dcf-scenario-item-1')).toHaveTextContent(
      'Benim senaryom',
    )
  })

  it('locks the save trigger while the mutation is pending', async () => {
    const user = userEvent.setup()
    let release!: () => void
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    server.use(
      http.post(`${API_BASE}/me/scenarios`, async () => {
        await gate
        return HttpResponse.json(DCF_SCENARIO_ALFA, { status: 201 })
      }),
    )

    renderDcf()
    await screen.findByTestId('dcf-params')
    await user.type(screen.getByTestId('dcf-scenario-name'), 'Benim senaryom')
    await user.click(screen.getByTestId('dcf-scenario-save'))

    expect(
      screen.getByRole('button', { name: TR('dcf.scenario.saving') }),
    ).toBeDisabled()
    expect(screen.getByTestId('dcf-scenario-name')).toBeDisabled()

    release()
    await waitFor(() =>
      expect(
        screen.queryByRole('button', { name: TR('dcf.scenario.saving') }),
      ).not.toBeInTheDocument(),
    )
  })

  it('renders a plain-language error with retry on failure, preserving input', async () => {
    const user = userEvent.setup()
    server.use(problemHandler(`${API_BASE}/me/scenarios`, 'INTERNAL', { method: 'post' }))

    renderDcf()
    await screen.findByTestId('dcf-params')
    const rate = screen.getByTestId('dcf-param-discount_rate')
    await user.clear(rate)
    await user.type(rate, '14')
    await user.type(screen.getByTestId('dcf-scenario-name'), 'Benim senaryom')
    await user.click(screen.getByTestId('dcf-scenario-save'))

    const error = await screen.findByTestId('dcf-scenario-error')
    expect(error).toHaveTextContent(TR('dcf.scenario.saveErrorTitle'))
    expect(error).toHaveTextContent(TR('dcf.scenario.saveErrorBody'))
    expect(
      within(error).getByRole('button', { name: TR('common.retry') }),
    ).toBeInTheDocument()
    expect(screen.getByTestId('dcf-scenario-name')).toHaveValue('Benim senaryom')
    expect(screen.getByTestId('dcf-param-discount_rate')).toHaveValue('14')
  })

  it('requires the discard confirmation when loading over unsaved changes', async () => {
    const user = userEvent.setup()
    server.use(
      scenariosListHandler([DCF_SCENARIO_ALFA]),
      dcfComputeHandler(RECOMPUTE_ON_OVERRIDE),
    )

    renderDcf()
    await screen.findByTestId('dcf-params')
    const rate = screen.getByTestId('dcf-param-discount_rate')
    await user.clear(rate)
    await user.type(rate, '14')
    expect(await screen.findByTestId('dcf-unsaved')).toBeInTheDocument()

    await user.click(await screen.findByTestId('dcf-scenario-load-1'))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent(TR('dcf.scenario.loadConfirmTitle'))
    expect(dialog).toHaveTextContent(
      fill(TR('dcf.scenario.loadConfirmDescription'), { name: 'Muhafazakâr' }),
    )

    // Cancel keeps the unsaved draft exactly as it was.
    await user.click(within(dialog).getByRole('button', { name: TR('common.cancel') }))
    expect(screen.getByTestId('dcf-param-discount_rate')).toHaveValue('14')
    expect(screen.getByTestId('dcf-unsaved')).toBeInTheDocument()
  })
})

describe('TC-VAL-025 — load exactly; unsaved-changes guard', () => {
  it('restores every parameter exactly (the 0.15 override) and recomputes', async () => {
    const user = userEvent.setup()
    const bodies: DcfComputeRequest[] = []
    server.use(
      scenariosListHandler([DCF_SCENARIO_ALFA]),
      dcfComputeHandler((body) => {
        bodies.push(body)
        return RECOMPUTE_ON_OVERRIDE(body)
      }),
    )

    renderDcf()
    await screen.findByTestId('dcf-params')
    // Baseline result before any load.
    await waitFor(() =>
      expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
        formatDecimal('tr', 13.1969, 4, 2),
      ),
    )

    // An unmodified calculator loads without a confirmation dialog.
    await user.click(await screen.findByTestId('dcf-scenario-load-1'))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    // Every parameter restored exactly as saved — including the 0.15 override.
    const expected: Record<string, string> = {
      base_fcf: '100',
      growth_rate: '10',
      horizon_years: '5',
      terminal_growth: '3',
      discount_rate: '15',
      debt: '400',
      cash: '200',
      share_count: '100',
    }
    await waitFor(() =>
      expect(screen.getByTestId('dcf-param-discount_rate')).toHaveValue('15'),
    )
    for (const [key, value] of Object.entries(expected)) {
      expect(screen.getByTestId(`dcf-param-${key}`)).toHaveValue(value)
    }

    // Outputs recompute from the loaded params, and the draft is no longer dirty.
    await waitFor(() =>
      expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
        formatDecimal('tr', 25, 4, 2),
      ),
    )
    expect(
      bodies.some((body) => Math.abs(body.discount_rate - 0.15) < 1e-9),
    ).toBe(true)
    expect(screen.queryByTestId('dcf-unsaved')).not.toBeInTheDocument()
  })

  it('discards the draft only after the confirmation is accepted', async () => {
    const user = userEvent.setup()
    server.use(
      scenariosListHandler([DCF_SCENARIO_ALFA]),
      dcfComputeHandler(RECOMPUTE_ON_OVERRIDE),
    )

    renderDcf()
    await screen.findByTestId('dcf-params')
    const rate = screen.getByTestId('dcf-param-discount_rate')
    await user.clear(rate)
    await user.type(rate, '14')
    expect(await screen.findByTestId('dcf-unsaved')).toBeInTheDocument()

    await user.click(await screen.findByTestId('dcf-scenario-load-1'))
    const dialog = await screen.findByRole('dialog')
    await user.click(
      within(dialog).getByRole('button', { name: TR('dcf.scenario.loadConfirm') }),
    )

    // Confirmed → the scenario's params replace the draft exactly.
    await waitFor(() =>
      expect(screen.getByTestId('dcf-param-discount_rate')).toHaveValue('15'),
    )
    expect(screen.queryByTestId('dcf-unsaved')).not.toBeInTheDocument()
  })
})

describe('TC-VAL-026 — missing inputs (critical flow 5)', () => {
  it('explains what is missing instead of computing on partial data, then computes once supplied', async () => {
    const user = userEvent.setup()
    const bodies: DcfComputeRequest[] = []
    server.use(
      dcfBaselineHandler(PART_BASELINE),
      dcfComputeHandler((body) => {
        bodies.push(body)
        return DCF_COMPUTE_FIXTURE
      }),
    )

    renderDcf(['/stocks/PART/dcf'])

    // The calculator explains what is missing; it names the missing input.
    const notice = await screen.findByTestId('dcf-missing-inputs')
    expect(notice).toHaveTextContent(TR('dcf.missingInputs.title'))
    expect(notice).toHaveTextContent(TR('dcf.param.base_fcf'))

    // No computation on partial data, and no fair value is presented.
    await new Promise((resolve) => setTimeout(resolve, 400))
    expect(bodies).toHaveLength(0)
    expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent('—')

    // Supplying the user's own value makes the model compute (user's model).
    await user.type(screen.getByTestId('dcf-param-base_fcf'), '100')
    await waitFor(() => expect(bodies.length).toBeGreaterThan(0))
    expect(bodies[bodies.length - 1].base_fcf).toBe(100)

    await waitFor(() =>
      expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
        formatDecimal('tr', 13.1969, 4, 2),
      ),
    )
  })
})
