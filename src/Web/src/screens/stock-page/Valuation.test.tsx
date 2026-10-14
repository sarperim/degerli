import { beforeEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'

import { formatNumber } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'
import { server } from '@/test/msw/server'
import { fill } from '@/screens/stock-page/testSupport'
import {
  BETA_VALUATION_FIXTURE,
  EPSL_VALUATION_FIXTURE,
  NO_MEDIAN_VALUATION_FIXTURE,
  RAW_VALUATION_FIXTURE,
  resetStockPageTest,
  stockValuationHandler,
  VALUATION_FIXTURE,
} from '@/screens/stock-page/testSupport'
import {
  ValuationSection,
  VsSectorStrip,
} from '@/screens/stock-page/ValuationSection'
import { valuationHistoryPoints } from '@/screens/stock-page/valuation'

/**
 * SCR-005 Valuation section component tests (TKT-res-009).
 *
 * TC-RES-025 — Vs.-sector strip component (L3; decision table — populated /
 * not-meaningful / no-median classes). Traces UXR-RES-013/024/025.
 * TC-RES-032 — Adjusted historical data (L4; SCR-005 critical flow 5). Traces
 * UXR-RES-020. Expected results are the un-fragmented cases from
 * `.pipeline/testing/stock-research.md`; copy is read from the catalogs.
 */

const TR = (key: string) => catalogValue('tr', key)

function metric(container: HTMLElement, code: string): HTMLElement {
  const node = container.querySelector(`[data-metric="${code}"]`)
  if (!(node instanceof HTMLElement)) {
    throw new Error(`strip metric "${code}" not found`)
  }
  return node
}

beforeEach(resetStockPageTest)

describe('TC-RES-025 — vs.-sector strip component', () => {
  it('shows the stock value next to the sector median with its peer count and the P/E exclusion disclosure', () => {
    const { container } = renderWithProviders(
      <VsSectorStrip strip={VALUATION_FIXTURE.strip} />,
    )

    const pe = metric(container, 'pe')
    expect(pe.querySelector('[data-slot="stock-value"]')).toHaveTextContent('10')
    expect(pe.querySelector('[data-slot="sector-median"]')).toHaveTextContent('10')
    expect(pe.querySelector('[data-slot="peer-count"]')).toHaveTextContent(
      fill(TR('stockPage.valuation.strip.peerCount'), { count: 3 }),
    )

    // The loss-maker exclusion behind the P/E median is disclosed inline (UXR-RES-013).
    expect(container.querySelector('[data-slot="pe-exclusion"]')).toHaveTextContent(
      fill(TR('stockPage.valuation.strip.exclusionDisclosure'), { count: 2 }),
    )

    // A metric with no exclusions carries no P/E-specific disclosure of its own.
    const pb = metric(container, 'pb')
    expect(pb.querySelector('[data-slot="peer-count"]')).toHaveTextContent(
      fill(TR('stockPage.valuation.strip.peerCount'), { count: 5 }),
    )
  })

  it('shows the not-meaningful state for the stock’s own value while keeping the median and peer count', () => {
    const { container } = renderWithProviders(
      <VsSectorStrip strip={BETA_VALUATION_FIXTURE.strip} />,
    )

    const pe = metric(container, 'pe')
    expect(pe.querySelector('[data-slot="stock-value"]')).toHaveTextContent(
      TR('honestData.notMeaningful'),
    )
    // The comparison basis is still served honestly (TC-RES-008 / UXR-RES-025).
    expect(pe.querySelector('[data-slot="sector-median"]')).toHaveTextContent(
      formatNumber('tr', 12.5),
    )
    expect(pe.querySelector('[data-slot="peer-count"]')).toHaveTextContent(
      fill(TR('stockPage.valuation.strip.peerCount'), { count: 4 }),
    )
  })

  it('degrades a metric with no sector median to an honest no-data state', () => {
    const { container } = renderWithProviders(
      <VsSectorStrip strip={NO_MEDIAN_VALUATION_FIXTURE.strip} />,
    )

    const evFcf = metric(container, 'ev_fcf')
    expect(evFcf.querySelector('[data-slot="stock-value"]')).toHaveTextContent('22')
    expect(evFcf.querySelector('[data-slot="sector-median"]')).toHaveTextContent(
      TR('stockPage.valuation.strip.noMedian'),
    )
    expect(evFcf.querySelector('[data-slot="peer-count"]')).not.toBeInTheDocument()
  })
})

describe('TC-RES-032 — adjusted historical data', () => {
  it('selects the adjusted close when the series is on the adjusted basis', () => {
    const points = valuationHistoryPoints(EPSL_VALUATION_FIXTURE.historical)

    // The split cliff (raw 100 → 20) is not plotted; the adjusted close is (50 → 49).
    expect(points.map((point) => point.close)).toEqual([50, 49])
    expect(points.every((point) => point.adjusted)).toBe(true)
  })

  it('keeps the raw close when the series is on the raw basis', () => {
    const points = valuationHistoryPoints(RAW_VALUATION_FIXTURE.historical)

    expect(points.map((point) => point.close)).toEqual([100, 98])
    expect(points.every((point) => point.adjusted)).toBe(false)
  })

  it('renders the adjusted series and carries the adjusted-data disclaimer for a split history', async () => {
    server.use(stockValuationHandler(EPSL_VALUATION_FIXTURE))
    renderWithProviders(<ValuationSection symbol="EPSL" />)

    const chart = await screen.findByTestId('valuation-history-chart')
    expect(chart).toHaveAttribute('data-basis', 'adjusted')
    expect(chart.querySelector('svg')).toBeInTheDocument()
    expect(screen.getByText(TR('honestData.adjustedNotice'))).toBeInTheDocument()
  })

  it('does not claim the adjusted disclaimer over a raw-basis series', async () => {
    server.use(stockValuationHandler(RAW_VALUATION_FIXTURE))
    renderWithProviders(<ValuationSection symbol="ALFA" />)

    const chart = await screen.findByTestId('valuation-history-chart')
    expect(chart).toHaveAttribute('data-basis', 'raw')
    expect(
      screen.queryByText(TR('honestData.adjustedNotice')),
    ).not.toBeInTheDocument()
  })
})
