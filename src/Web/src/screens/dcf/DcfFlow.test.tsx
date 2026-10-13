import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import type { DcfComputeResponse, DcfSensitivity } from '@/lib/api/contracts'
import { formatDecimal, formatPercent } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import {
  DCF_COMPUTE_FIXTURE,
  DCF_SENSITIVITY_FIXTURE,
  dcfComputeHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderDcf, resetDcfTest } from '@/screens/dcf/testSupport'

/**
 * TC-VAL-023 — Value with own assumptions (critical flow 1, SC-004; L4).
 *
 * From the stock page, open the calculator: the baseline + immediate result are
 * shown; changing the discount rate recomputes the fair value, comparison,
 * verdict and sensitivity without a page reload, and the verdict states in plain
 * language how far the price sits from the user's fair value, framed on their
 * assumptions. (Runs as a component-level integration against MSW, mirroring the
 * SCR-003 L4 critical-flow tests.)
 */

const TR = (key: string) => catalogValue('tr', key)

function fill(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{\{(\w+)\}\}/g, (_match, key: string) =>
    String(vars[key] ?? ''),
  )
}

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

beforeEach(resetDcfTest)

describe('TC-VAL-023 — value with own assumptions (critical flow 1)', () => {
  it('opens from the stock page, shows the baseline result, and recomputes on change', async () => {
    server.use(
      dcfComputeHandler((body) =>
        body.discount_rate >= 0.14 ? ALT_COMPUTE : DCF_COMPUTE_FIXTURE,
      ),
    )
    const user = userEvent.setup()
    renderDcf(['/stocks/ALFA'])

    // The stock page is the entry point (FR-RES-018).
    expect(screen.getByTestId('stock-page-symbol')).toHaveTextContent('ALFA')
    await user.click(screen.getByTestId('open-dcf'))

    // Baseline + immediate result before any user change.
    await waitFor(() =>
      expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
        formatDecimal('tr', 13.1969, 4, 2),
      ),
    )
    expect(screen.getByTestId('dcf-verdict')).toHaveTextContent(
      fill(TR('dcf.verdict.above'), { percent: formatPercent('tr', 0.5155) }),
    )

    // Change the discount rate → everything recomputes without a reload.
    const input = screen.getByTestId('dcf-param-discount_rate')
    await user.clear(input)
    await user.type(input, '14')

    await waitFor(() =>
      expect(screen.getByTestId('dcf-fair-value')).toHaveTextContent(
        formatDecimal('tr', 25, 4, 2),
      ),
    )

    // Verdict now states the distance from the user's fair value, on their
    // assumptions, and the sensitivity table refreshed in place.
    expect(screen.getByTestId('dcf-verdict')).toHaveTextContent(
      fill(TR('dcf.verdict.below'), { percent: formatPercent('tr', 0.2) }),
    )
    expect(screen.getByTestId('dcf-verdict')).toHaveTextContent(TR('dcf.framing'))
    expect(screen.getByTestId('dcf-sensitivity-cell-0-0')).toHaveTextContent(
      formatDecimal('tr', 99.99, 4, 2),
    )
    // No page reload: we are still on the calculator.
    expect(screen.getByTestId('dcf-params')).toBeInTheDocument()
  })
})
