import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import type {
  DcfComputeRequest,
  DcfComputeResponse,
  DcfSensitivity,
} from '@/lib/api/contracts'
import { formatDecimal, formatPercent } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import {
  DCF_BASELINE_FIXTURE,
  DCF_COMPUTE_FIXTURE,
  DCF_SENSITIVITY_FIXTURE,
  dcfBaselineHandler,
  dcfComputeHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderDcf, resetDcfTest } from '@/screens/dcf/testSupport'

/**
 * SCR-006 DCF Calculator core (TKT-val-006).
 *
 * TC-VAL-017 — Calculator open: baseline, immediate result, all 8 params (L3).
 * TC-VAL-018 — Recompute on change; invalid input handling (L3).
 * TC-VAL-019 — Plain-language verdict rendering (L3).
 * TC-VAL-020 — Sensitivity table component (L3).
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

/** Interpolate an i18n template the way i18next would. */
function fill(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{\{(\w+)\}\}/g, (_match, key: string) =>
    String(vars[key] ?? ''),
  )
}

/** An alternate compute response (positive MoS → price below fair value). */
const ALT_SENSITIVITY: DcfSensitivity = {
  ...DCF_SENSITIVITY_FIXTURE,
  fairValues: DCF_SENSITIVITY_FIXTURE.fairValues.map((row, index) =>
    index === 0 ? [99.99, ...row.slice(1)] : row,
  ),
}

const ALT_COMPUTE: DcfComputeResponse = {
  fairValuePerShare: 25,
  marginOfSafety: 0.2,
  price: { value: 20, asOf: '2026-10-06', stale: false },
  sensitivity: ALT_SENSITIVITY,
}

/** A grid with honest not-computable holes (r ≤ g_t) for TC-VAL-020. */
const NULL_SENSITIVITY: DcfSensitivity = {
  discountRates: [0.02, 0.03, 0.04, 0.05, 0.06],
  terminalGrowths: [0.01, 0.02, 0.03, 0.04, 0.05],
  fairValues: [
    [10, null, null, null, null],
    [12, 11, null, null, null],
    [14, 13, 12, null, null],
    [16, 15, 14, 13, null],
    [18, 17, 16, 15, 14],
  ],
}

beforeEach(resetDcfTest)

describe('TC-VAL-017 — calculator open: baseline, immediate result, all 8 params', () => {
  it('shows all eight parameters, editable, seeded from the baseline', async () => {
    renderDcf()
    await screen.findByTestId('dcf-params')

    const expected: Record<string, string> = {
      base_fcf: '100',
      growth_rate: '10',
      horizon_years: '5',
      terminal_growth: '3',
      discount_rate: '12',
      debt: '400',
      cash: '200',
      share_count: '100',
    }

    for (const [key, value] of Object.entries(expected)) {
      const input = screen.getByTestId(`dcf-param-${key}`)
      expect(input).toHaveValue(value)
      expect(input).not.toBeDisabled()
      expect(input).not.toHaveAttribute('readonly')
    }
  })

  it('labels canonical-fact defaults with their source/as-of date', async () => {
    renderDcf()
    await screen.findByTestId('dcf-params')

    for (const key of ['base_fcf', 'debt', 'cash', 'share_count']) {
      expect(screen.getByTestId(`dcf-param-source-${key}`)).toHaveTextContent('2026')
    }
    // Baseline defaults (non-canonical) carry no canonical source note.
    expect(screen.queryByTestId('dcf-param-source-growth_rate')).not.toBeInTheDocument()
    expect(screen.queryByTestId('dcf-param-source-discount_rate')).not.toBeInTheDocument()
  })

  it('immediately shows the baseline fair value, price with as-of, and comparison', async () => {
    renderDcf()

    await waitFor(() =>
      expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
        formatDecimal('tr', 13.1969, 4, 2),
      ),
    )
    expect(screen.getByTestId('dcf-price')).toHaveTextContent(
      formatDecimal('tr', 20, 2, 2),
    )
    expect(screen.getByTestId('dcf-price-asof')).toHaveTextContent('2026')
    expect(screen.getByTestId('dcf-verdict')).toHaveTextContent(
      fill(TR('dcf.verdict.above'), { percent: formatPercent('tr', 0.5155) }),
    )
  })

  it('renders restatement markings carried by the payload', async () => {
    server.use(
      dcfBaselineHandler({
        ...DCF_BASELINE_FIXTURE,
        baseline: {
          ...DCF_BASELINE_FIXTURE.baseline!,
          canonicalFactRefs: {
            base_fcf: { asOf: '2026-09-30', restated: true },
            debt: { asOf: '2026-09-30', restated: false },
            cash: { asOf: '2026-09-30', restated: false },
            share_count: { asOf: '2026-09-30', restated: false },
          },
        },
      }),
    )
    renderDcf()
    await screen.findByTestId('dcf-params')

    expect(screen.getByTestId('dcf-param-restated-base_fcf')).toBeInTheDocument()
    expect(screen.queryByTestId('dcf-param-restated-debt')).not.toBeInTheDocument()
    expect(screen.getByText(TR('honestData.restatedNotice'))).toBeInTheDocument()
  })

  it('marks a stale price with its as-of date', async () => {
    server.use(
      dcfBaselineHandler({
        ...DCF_BASELINE_FIXTURE,
        price: { value: 20, asOf: '2026-09-26', stale: true },
      }),
    )
    renderDcf()
    await screen.findByTestId('dcf-params')

    expect(screen.getByText(TR('honestData.stale'))).toBeInTheDocument()
    expect(screen.getByTestId('dcf-price-asof')).toHaveTextContent('2026')
  })
})

describe('TC-VAL-018 — recompute on change; invalid input handling', () => {
  it('recomputes once per settled change and mirrors the displayed inputs', async () => {
    const bodies: DcfComputeRequest[] = []
    server.use(
      dcfComputeHandler((body) => {
        bodies.push(body)
        return body.discount_rate >= 0.14 ? ALT_COMPUTE : DCF_COMPUTE_FIXTURE
      }),
    )

    renderDcf()
    await waitFor(() =>
      expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
        formatDecimal('tr', 13.1969, 4, 2),
      ),
    )
    expect(bodies).toHaveLength(1)
    expect(bodies[0].discount_rate).toBeCloseTo(0.12, 6)

    // One settled change (a single edit event) → exactly one compute.
    fireEvent.change(screen.getByTestId('dcf-param-discount_rate'), {
      target: { value: '14' },
    })

    await waitFor(() =>
      expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
        formatDecimal('tr', 25, 4, 2),
      ),
    )
    // Exactly one compute for the settled change; the request mirrors the input.
    expect(bodies).toHaveLength(2)
    expect(bodies[1].discount_rate).toBeCloseTo(0.14, 6)
  })

  it('shows inline validation, suppresses recompute, and keeps the last valid result', async () => {
    const bodies: DcfComputeRequest[] = []
    server.use(
      dcfComputeHandler((body) => {
        bodies.push(body)
        return DCF_COMPUTE_FIXTURE
      }),
    )

    const user = userEvent.setup()
    renderDcf()
    await waitFor(() =>
      expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
        formatDecimal('tr', 13.1969, 4, 2),
      ),
    )
    const callsBefore = bodies.length

    const input = screen.getByTestId('dcf-param-base_fcf')
    await user.clear(input)
    await user.type(input, 'abc')

    expect(await screen.findByText(TR('dcf.error.notNumber'))).toBeInTheDocument()
    expect(input).toHaveAttribute('aria-invalid', 'true')

    // No recompute is issued and the last valid result stays visible.
    await new Promise((resolve) => setTimeout(resolve, 400))
    expect(bodies).toHaveLength(callsBefore)
    expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
      formatDecimal('tr', 13.1969, 4, 2),
    )
  })

  it('rejects an out-of-range horizon inline without recomputing', async () => {
    const bodies: DcfComputeRequest[] = []
    server.use(
      dcfComputeHandler((body) => {
        bodies.push(body)
        return DCF_COMPUTE_FIXTURE
      }),
    )

    renderDcf()
    await screen.findByTestId('dcf-params')
    await waitFor(() => expect(bodies).toHaveLength(1))

    // A single edit event to an out-of-range horizon (11) — no recompute.
    fireEvent.change(screen.getByTestId('dcf-param-horizon_years'), {
      target: { value: '11' },
    })

    expect(await screen.findByText(TR('dcf.error.outOfRange'))).toBeInTheDocument()
    await new Promise((resolve) => setTimeout(resolve, 400))
    expect(bodies).toHaveLength(1)
  })

  it('indicates unsaved changes when deviating from the baseline, and resets', async () => {
    const user = userEvent.setup()
    renderDcf()
    await screen.findByTestId('dcf-params')
    expect(screen.queryByTestId('dcf-unsaved')).not.toBeInTheDocument()

    const input = screen.getByTestId('dcf-param-discount_rate')
    await user.clear(input)
    await user.type(input, '14')

    expect(await screen.findByTestId('dcf-unsaved')).toBeInTheDocument()

    await user.click(screen.getByTestId('dcf-reset'))
    expect(screen.queryByTestId('dcf-unsaved')).not.toBeInTheDocument()
    expect(screen.getByTestId('dcf-param-discount_rate')).toHaveValue('12')
  })
})

describe('TC-VAL-019 — plain-language verdict rendering', () => {
  it('renders a single framed sentence with the MoS percentage (TR)', async () => {
    renderDcf()
    const verdict = await screen.findByTestId('dcf-verdict')

    expect(verdict.textContent).toBe(
      fill(TR('dcf.verdict.above'), { percent: formatPercent('tr', 0.5155) }),
    )
    // Direction is carried in the text, not by colour alone.
    expect(verdict).toHaveAttribute('data-direction', 'above')
    expect(verdict).toHaveTextContent(TR('dcf.framing'))
    // Framing + informational-only disclaimer are both visible.
    expect(screen.getByText(TR('dcf.framing'))).toBeInTheDocument()
    expect(screen.getByText(TR('disclaimer.text'))).toBeInTheDocument()
  })

  it('renders the verdict from the EN catalog when the language is English', async () => {
    renderDcf(['/stocks/ALFA/dcf'], 'en')
    const verdict = await screen.findByTestId('dcf-verdict')

    expect(verdict.textContent).toBe(
      fill(EN('dcf.verdict.above'), { percent: formatPercent('en', 0.5155) }),
    )
    expect(screen.getByText(EN('dcf.framing'))).toBeInTheDocument()
  })

  it('renders the below-fair-value class for a positive margin of safety', async () => {
    server.use(dcfComputeHandler(() => ALT_COMPUTE))
    renderDcf()
    const verdict = await screen.findByTestId('dcf-verdict')

    expect(verdict.textContent).toBe(
      fill(TR('dcf.verdict.below'), { percent: formatPercent('tr', 0.2) }),
    )
    expect(verdict).toHaveAttribute('data-direction', 'below')
  })
})

describe('TC-VAL-020 — sensitivity table component', () => {
  it('renders the grid with r × g_t headers and marks the current cell', async () => {
    renderDcf()
    await screen.findByTestId('dcf-sensitivity')

    expect(screen.getByTestId('dcf-sensitivity-corner')).toHaveTextContent(
      TR('dcf.sensitivity.axisLabel'),
    )
    expect(screen.getByTestId('dcf-sensitivity-col-0')).toHaveTextContent(
      formatPercent('tr', 0.01),
    )
    expect(screen.getByTestId('dcf-sensitivity-rowhead-0')).toHaveTextContent(
      formatPercent('tr', 0.1),
    )

    // The center cell (user's own discount rate × terminal growth) is identified.
    const current = screen.getByTestId('dcf-sensitivity-cell-2-2')
    expect(current).toHaveAttribute('data-current', 'true')
    expect(current).toHaveTextContent(formatDecimal('tr', 13.1969, 4, 2))
  })

  it('renders not-computable cells honestly as NULL, not as zero', async () => {
    server.use(
      dcfComputeHandler(() => ({
        ...DCF_COMPUTE_FIXTURE,
        sensitivity: NULL_SENSITIVITY,
      })),
    )
    renderDcf()
    await screen.findByTestId('dcf-sensitivity')

    const hole = screen.getByTestId('dcf-sensitivity-null-0-1')
    expect(hole).toHaveTextContent(TR('dcf.sensitivity.notComputable'))
    expect(screen.getByTestId('dcf-sensitivity-null-0-4')).toBeInTheDocument()
    expect(screen.getByTestId('dcf-sensitivity-cell-0-0')).toHaveTextContent(
      formatDecimal('tr', 10, 4, 2),
    )
  })

  it('recomputes the cells when a parameter changes', async () => {
    server.use(
      dcfComputeHandler((body) =>
        body.discount_rate >= 0.14 ? ALT_COMPUTE : DCF_COMPUTE_FIXTURE,
      ),
    )
    renderDcf()
    await screen.findByTestId('dcf-sensitivity')
    expect(screen.getByTestId('dcf-sensitivity-cell-0-0')).toHaveTextContent(
      formatDecimal('tr', 18.5, 4, 2),
    )

    fireEvent.change(screen.getByTestId('dcf-param-discount_rate'), {
      target: { value: '14' },
    })

    await waitFor(() =>
      expect(screen.getByTestId('dcf-sensitivity-cell-0-0')).toHaveTextContent(
        formatDecimal('tr', 99.99, 4, 2),
      ),
    )
  })
})
