import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { catalogValue } from '@/test/i18n'
import {
  METRIC_CATALOG_FIXTURE,
  SCREENER_RUN_FIXTURE,
  screenerMetricsHandler,
  screenerRunHandler,
} from '@/test/msw/handlers'
import type { MetricCatalogResponse, ScreenerRunResponse } from '@/lib/api/contracts'
import { server } from '@/test/msw/server'
import { fill, renderScreener, resetScreenerTest } from '@/screens/screener/testSupport'

/**
 * TC-SCR-022 — Criteria builder (L3).
 *
 * Traces UXR-SCR-001/002/003. Decision table: build / remove / clear. Metrics are
 * organized by the five families from the catalog; a criterion carries a bound
 * type (min/max/range) and a CAGR window for growth metrics; the controls carry
 * plain-language metric labels and units; individual removal and clear-all work;
 * controls are native and keyboard operable.
 *
 * TC-SCR-025 — Results, empty and exclusion-count display (L3).
 * TC-SCR-027 — Zero matches (critical flow 2, L4).
 */

const TR = (key: string) => catalogValue('tr', key)

function metricFixture(code: string) {
  for (const group of METRIC_CATALOG_FIXTURE.families) {
    const metric = group.metrics.find((candidate) => candidate.metricCode === code)
    if (metric) return metric
  }
  throw new Error(`Unknown fixture metric ${code}`)
}

async function addMetric(user: ReturnType<typeof userEvent.setup>, code: string) {
  await user.selectOptions(screen.getByTestId('metric-select'), code)
  await user.click(screen.getByTestId('add-criterion'))
}

function lastRow() {
  const rows = screen.getAllByTestId('criterion-row')
  return rows[rows.length - 1]
}

beforeEach(resetScreenerTest)

describe('TC-SCR-022 — criteria builder', () => {
  it('organizes the catalog by the five families and offers catalog metrics', async () => {
    renderScreener()
    await screen.findByTestId('metric-select')

    const families = screen.getByTestId('metric-families')
    for (const key of [
      'screener.families.valuation',
      'screener.families.quality',
      'screener.families.growth',
      'screener.families.financial_health',
      'screener.families.dividends',
    ]) {
      expect(within(families).getByText(TR(key))).toBeInTheDocument()
    }

    // The catalog metrics are offered (all 18) with their plain-language label.
    const options = within(screen.getByTestId('metric-select')).getAllByRole('option')
    expect(options).toHaveLength(METRIC_CATALOG_FIXTURE.families.reduce(
      (total, group) => total + group.metrics.length,
      0,
    ) + 1) // + the placeholder option
    expect(
      within(screen.getByTestId('metric-select')).getByRole('option', {
        name: new RegExp(metricFixture('pe').labelTr),
      }),
    ).toBeInTheDocument()
  })

  it('adds a criterion with min/max/range bounds labeled with the metric name and unit', async () => {
    const user = userEvent.setup()
    renderScreener()
    await screen.findByTestId('metric-select')

    await addMetric(user, 'pe')

    const row = lastRow()
    const label = metricFixture('pe').labelTr
    expect(within(row).getByTestId('criterion-label')).toHaveTextContent(label)
    expect(within(row).getByTestId('criterion-label')).toHaveTextContent('(x)')

    // Default bound is "at least": only the min input is present, labeled plainly.
    expect(within(row).getByTestId('criterion-bound')).toHaveValue('min')
    expect(
      within(row).getByLabelText(fill(TR('screener.minValue'), { metric: label })),
    ).toBeInTheDocument()
    expect(within(row).queryByTestId('criterion-max')).not.toBeInTheDocument()

    // Switching to a range bound reveals both value inputs.
    await user.selectOptions(within(row).getByTestId('criterion-bound'), 'range')
    expect(
      within(row).getByLabelText(fill(TR('screener.minValue'), { metric: label })),
    ).toBeInTheDocument()
    expect(
      within(row).getByLabelText(fill(TR('screener.maxValue'), { metric: label })),
    ).toBeInTheDocument()
  })

  it('gives each growth criterion a selectable 3/5/10 window', async () => {
    const user = userEvent.setup()
    renderScreener()
    await screen.findByTestId('metric-select')

    await addMetric(user, 'rev_cagr')

    const row = lastRow()
    const window = within(row).getByTestId('criterion-window')
    const options = within(window).getAllByRole('option')
    expect(options.map((option) => option.textContent)).toEqual([
      fill(TR('screener.windowYears'), { years: 3 }),
      fill(TR('screener.windowYears'), { years: 5 }),
      fill(TR('screener.windowYears'), { years: 10 }),
    ])
    expect(window).toHaveValue('5')

    // A non-growth metric offers no window selector.
    await addMetric(user, 'pe')
    expect(within(lastRow()).queryByTestId('criterion-window')).not.toBeInTheDocument()
  })

  it('removes an individual criterion', async () => {
    const user = userEvent.setup()
    renderScreener()
    await screen.findByTestId('metric-select')

    await addMetric(user, 'pe')
    await addMetric(user, 'roe')
    expect(screen.getAllByTestId('criterion-row')).toHaveLength(2)

    const peRow = screen
      .getAllByTestId('criterion-row')
      .find((row) => row.getAttribute('data-metric') === 'pe')!
    await user.click(within(peRow).getByTestId('criterion-remove'))

    const rows = screen.getAllByTestId('criterion-row')
    expect(rows).toHaveLength(1)
    expect(rows[0]).toHaveAttribute('data-metric', 'roe')
  })

  it('clears all criteria', async () => {
    const user = userEvent.setup()
    renderScreener()
    await screen.findByTestId('metric-select')

    await addMetric(user, 'pe')
    await addMetric(user, 'roe')
    await user.click(screen.getByTestId('clear-all'))

    expect(screen.queryAllByTestId('criterion-row')).toHaveLength(0)
    expect(screen.getByTestId('builder-empty')).toHaveTextContent(TR('screener.initialTitle'))
  })
})

describe('TC-SCR-025 — results, empty and exclusion-count display', () => {
  it('renders per-criterion columns, values, match count, exclusion count and as-of', async () => {
    const user = userEvent.setup()
    renderScreener()
    await screen.findByTestId('metric-select')

    await addMetric(user, 'pe')
    await user.selectOptions(
      within(lastRow()).getByTestId('criterion-bound'),
      'max',
    )
    await user.type(within(lastRow()).getByTestId('criterion-max'), '15')
    await addMetric(user, 'roe')
    await user.type(within(lastRow()).getByTestId('criterion-min'), '15')

    await user.click(screen.getByTestId('run-button'))

    const results = await screen.findByTestId('results')
    // Per-criterion columns.
    expect(within(results).getByTestId('result-col-pe')).toHaveTextContent(
      metricFixture('pe').labelTr,
    )
    expect(within(results).getByTestId('result-col-roe')).toHaveTextContent(
      metricFixture('roe').labelTr,
    )
    // Rows with per-criterion values.
    const alfa = within(results).getByTestId('result-row-ALFA')
    expect(within(alfa).getByTestId('result-value-ALFA-pe')).toHaveTextContent('10x')
    expect(within(alfa).getByTestId('result-value-ALFA-roe')).toHaveTextContent('20%')
    // Counts and as-of.
    expect(screen.getByTestId('match-count')).toHaveTextContent(
      fill(TR('screener.matchCount'), { count: 3 }),
    )
    expect(screen.getByTestId('excluded-count')).toHaveTextContent(
      fill(TR('screener.excludedCount'), { count: 2 }),
    )
    expect(screen.getByTestId('results-asof')).toHaveTextContent('2026')
  })

  it('hides the exclusion count when nothing was excluded', async () => {
    const user = userEvent.setup()
    server.use(
      screenerRunHandler({
        ...SCREENER_RUN_FIXTURE,
        excludedCount: 0,
      }),
    )
    renderScreener()
    await screen.findByTestId('metric-select')

    await addMetric(user, 'pe')
    await user.selectOptions(within(lastRow()).getByTestId('criterion-bound'), 'max')
    await user.type(within(lastRow()).getByTestId('criterion-max'), '15')
    await user.click(screen.getByTestId('run-button'))

    await screen.findByTestId('results')
    expect(screen.queryByTestId('excluded-count')).not.toBeInTheDocument()
  })
})

describe('TC-SCR-027 — zero matches', () => {
  it('shows an explicit zero-results state, not a blank list and not an error', async () => {
    const user = userEvent.setup()
    server.use(
      screenerRunHandler({
        ...SCREENER_RUN_FIXTURE,
        matchCount: 0,
        excludedCount: 0,
        rows: [],
      }),
    )
    renderScreener()
    await screen.findByTestId('metric-select')

    await addMetric(user, 'pe')
    await user.selectOptions(within(lastRow()).getByTestId('criterion-bound'), 'max')
    await user.type(within(lastRow()).getByTestId('criterion-max'), '5')
    await user.click(screen.getByTestId('run-button'))

    const zero = await screen.findByTestId('zero-results')
    expect(zero).toHaveTextContent(TR('screener.zeroTitle'))
    expect(zero).toHaveTextContent(TR('screener.zeroBody'))
    expect(screen.queryByTestId('run-error')).not.toBeInTheDocument()
    await waitFor(() => expect(screen.queryByTestId('results')).not.toBeInTheDocument())
  })
})

/**
 * TKT-foundation-072 (i) — display-side unit semantics for the screener.
 *
 * `03` §1 principle 1: the served `unit` is a code, not display prose. The criteria
 * editor, the metric select and the results table render the localized label from the
 * single unit map (`src/lib/units.ts`) — a prose code such as `TRY` never reaches the
 * screen. The catalog is server-driven and its `unit` is an open string (`03` §4), so
 * an unseen code must still degrade honestly rather than leak.
 */
describe('TKT-foundation-072 (i) — screener units are localized labels, not server codes', () => {
  const CATALOG_WITH_PROSE_UNIT: MetricCatalogResponse = {
    families: [
      {
        family: 'valuation',
        metrics: [
          { metricCode: 'pe', unit: 'x', labelTr: 'F/K', labelEn: 'P/E', isCagr: false, sortOrder: 1 },
          {
            metricCode: 'market_cap',
            unit: 'TRY',
            labelTr: 'Piyasa değeri',
            labelEn: 'Market cap',
            isCagr: false,
            sortOrder: 2,
          },
        ],
      },
    ],
  }

  const RUN_WITH_CAPITALISATION: ScreenerRunResponse = {
    asOf: '2026-10-06',
    stale: false,
    matchCount: 1,
    excludedCount: 0,
    droppedCriteria: [],
    rows: [
      {
        symbol: 'ALFA',
        name: 'Alfa Teknoloji A.Ş.',
        sector: 'A',
        values: { market_cap: 174_500_000 },
      },
    ],
  }

  beforeEach(() => {
    server.use(
      screenerMetricsHandler(CATALOG_WITH_PROSE_UNIT),
      screenerRunHandler(RUN_WITH_CAPITALISATION),
    )
  })

  it('renders the localized unit label in the metric select, not the raw code', async () => {
    renderScreener()
    await screen.findByTestId('metric-select')

    const option = within(screen.getByTestId('metric-select')).getByRole('option', {
      name: /Piyasa değeri/,
    })
    expect(option).toHaveTextContent(`(${TR('units.currencyTry')})`)
    expect(option).not.toHaveTextContent('TRY')
  })

  it('renders the localized unit label in the criterion row, not the raw code', async () => {
    const user = userEvent.setup()
    renderScreener()
    await screen.findByTestId('metric-select')

    await addMetric(user, 'market_cap')

    const label = within(lastRow()).getByTestId('criterion-label')
    expect(label).toHaveTextContent(TR('units.currencyTry'))
    expect(label).not.toHaveTextContent('TRY')
  })

  it('renders the localized unit label on result values, not the raw code', async () => {
    const user = userEvent.setup()
    renderScreener()
    await screen.findByTestId('metric-select')

    await addMetric(user, 'market_cap')
    await user.type(within(lastRow()).getByTestId('criterion-min'), '1000')
    await user.click(screen.getByTestId('run-button'))

    const cell = await screen.findByTestId('result-value-ALFA-market_cap')
    expect(cell).toHaveTextContent(TR('units.currencyTry'))
    expect(cell).not.toHaveTextContent('TRY')
  })
})
