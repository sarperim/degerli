import { beforeEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'

import { formatAsOfDate, formatNumber } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'
import { server } from '@/test/msw/server'
import { fill } from '@/screens/stock-page/testSupport'
import {
  BETA_VALUATION_FIXTURE,
  EPSL_VALUATION_FIXTURE,
  NO_MEDIAN_VALUATION_FIXTURE,
  NO_PEERS_VALUATION_FIXTURE,
  RAW_VALUATION_FIXTURE,
  resetStockPageTest,
  stockValuationHandler,
  VALUATION_FIXTURE,
} from '@/screens/stock-page/testSupport'
import {
  ValuationHistoryChart,
  ValuationSection,
  ValuationTooltipContent,
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

/**
 * TKT-foundation-055 — the sector comparison's honest empty must not take the
 * stock's own canonical multiples down with it. The own-multiple rows carry the
 * same `data-metric` / `data-slot="stock-value"` hooks as the strip rows, so the
 * canonical figure stays machine-readable off the valuation region when no peer
 * median is served (the TC-RES-028 / TC-XC-008 contract).
 */
describe('TKT-foundation-055 — current multiples stay addressable while the strip is empty', () => {
  it('keeps the canonical own P/E hooked when no peer median is served', async () => {
    server.use(
      stockValuationHandler({
        ...NO_PEERS_VALUATION_FIXTURE,
        current: VALUATION_FIXTURE.current,
      }),
    )
    const { container } = renderWithProviders(<ValuationSection symbol="ALFA" />)

    // The comparison block states its one honest empty…
    await screen.findByTestId('vs-sector-no-data')

    // …and the canonical own multiple is still rendered and addressable.
    const ownPe = VALUATION_FIXTURE.current.pe
    if (ownPe === null) throw new Error('fixture canonical P/E missing')
    const pe = container.querySelector('[data-metric="pe"] [data-slot="stock-value"]')
    expect(pe).toHaveTextContent(formatNumber('tr', ownPe))
    // The empty strip contributes no per-row hooks of its own.
    expect(
      container.querySelectorAll('[data-slot="vs-sector-strip"] [data-metric]'),
    ).toHaveLength(0)
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

/**
 * TKT-res-012 — the historical price chart exposes per-point values on hover.
 *
 * Explicit-criteria acceptance for the user-reported defect of 2026-10-18 (hover
 * highlights a point but shows neither date nor price). No tooltip-specific TC
 * exists in `.pipeline/testing/stock-research.md` yet — the test-planner follow-up
 * is recorded on the ticket — so these specs are named for the ticket rather than
 * an invented TC ID.
 *
 * Determinism: the tooltip content is rendered directly with synthetic Recharts
 * props instead of simulating a mouse move (Recharts hover geometry is not
 * reliably reproducible in jsdom), and every formatted figure is compared against
 * the shared `@/lib/format` helpers in both locales.
 */
describe('TKT-res-012 — valuation history chart hover tooltip', () => {
  it('mounts a Recharts tooltip inside the history chart', () => {
    const { container } = renderWithProviders(
      <ValuationHistoryChart historical={VALUATION_FIXTURE.historical} />,
    )

    expect(
      container.querySelector('.recharts-tooltip-wrapper'),
    ).toBeInTheDocument()
  })

  it('shows the hovered point’s date and plotted close in the TR locale', () => {
    const point = valuationHistoryPoints(VALUATION_FIXTURE.historical)[0]
    const { container } = renderWithProviders(
      <ValuationTooltipContent
        active
        payload={[{ graphicalItemId: 'close', payload: point }]}
      />,
    )

    expect(container.querySelector('[data-slot="tooltip-date"]')).toHaveTextContent(
      formatAsOfDate('tr', point.date),
    )
    expect(container.querySelector('[data-slot="tooltip-close"]')).toHaveTextContent(
      `${TR('stockPage.valuation.history.close')}: ${formatNumber('tr', point.close)}`,
    )
  })

  it('renders the same figures in the EN locale (no hardcoded locale)', () => {
    const point = valuationHistoryPoints(VALUATION_FIXTURE.historical)[1]
    const { container } = renderWithProviders(
      <ValuationTooltipContent
        active
        payload={[{ graphicalItemId: 'close', payload: point }]}
      />,
      { language: 'en' },
    )

    expect(container.querySelector('[data-slot="tooltip-date"]')).toHaveTextContent(
      formatAsOfDate('en', point.date),
    )
    expect(container.querySelector('[data-slot="tooltip-close"]')).toHaveTextContent(
      `${catalogValue('en', 'stockPage.valuation.history.close')}: ${formatNumber(
        'en',
        point.close,
      )}`,
    )
  })

  it('carries the plotted (adjusted) close, never the raw close behind a split', () => {
    const point = valuationHistoryPoints(EPSL_VALUATION_FIXTURE.historical)[0]
    const { container } = renderWithProviders(
      <ValuationTooltipContent
        active
        payload={[{ graphicalItemId: 'close', payload: point }]}
      />,
    )

    const close = container.querySelector('[data-slot="tooltip-close"]')
    expect(close).toHaveTextContent(formatNumber('tr', 50))
    expect(close).not.toHaveTextContent(formatNumber('tr', 100))
  })

  it('renders no content when no point is active', () => {
    renderWithProviders(<ValuationTooltipContent active={false} payload={[]} />)

    expect(screen.queryByTestId('valuation-tooltip')).not.toBeInTheDocument()
  })
})
