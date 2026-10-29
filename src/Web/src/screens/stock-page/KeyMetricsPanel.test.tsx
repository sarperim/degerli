import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'

import type { KeyMetricEntry, StockKeyMetricsResponse } from '@/lib/api/contracts'
import { formatDecimal, formatNumber, formatPercent } from '@/lib/format'
import { CATALOGS, catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import {
  BANK_KEY_METRICS_FIXTURE,
  KEY_METRICS_FIXTURE,
  renderStockPage,
  resetStockPageTest,
  stockKeyMetricsHandler,
} from '@/screens/stock-page/testSupport'
import { KEY_METRIC_CODES } from '@/screens/stock-page/keyMetricsView'

/**
 * TC-RES-041 — the SCR-005 key value metrics & moat panel and the capital &
 * dilution tracker (L3), rendered from TKT-foundation-096's served payload.
 *
 * The ticket pinned the id `TC-RES-038`, but that id already belongs to the
 * dividends-availability lane (TKT-foundation-052, `Dividends.test.tsx`); the
 * statements cases took 039/040, so this case takes the next free id 041 — the
 * same renumbering discipline the 095 lane recorded (ticket acceptance line and
 * `stock-research.md` §8 updated with it).
 *
 * Cases:
 *  (a) both row sets are complete — every served panel code and every tracker row;
 *  (b) a served value renders as served; a served-null value renders "—", never 0;
 *  (c) the 5Y company median renders its depth/observation disclosure and a
 *      withheld median is a dash (never a fabricated statistic);
 *  (d) the sector median renders with peer count and exclusion disclosure;
 *  (e) the regime-break marker renders on a spanning CAGR and not on a one-side one;
 *  (f) the TMS 29 alert renders from the served ratio (and not when unserved);
 *  (g) the bedelli/bedelsiz history renders dated with the adjusted-data disclaimer;
 *  (h) the share-count Y/Y change renders the honest no-count-history N/M;
 *  (i) ROE and EPS CAGR rows are labelled with their served group basis, never Parent;
 *  (j) a financial-sector profile shows only its profile-appropriate rows + notice;
 *  (k) the EN catalog labels the same row set (TR/EN parity).
 *
 * Copy is read from the catalogs, never hardcoded (§8.3).
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

function fill(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{\{(\w+)\}\}/g, (_match, key: string) =>
    String(vars[key] ?? ''),
  )
}

function panel(): HTMLElement {
  return screen.getByTestId('key-metrics-panel')
}

function tracker(): HTMLElement {
  return screen.getByTestId('capital-tracker')
}

function metricRow(code: string): HTMLElement {
  const found = panel().querySelector(`[data-metric="${code}"]`)
  expect(found).not.toBeNull()
  return found as HTMLElement
}

function percent(value: number): string {
  return formatPercent('tr', value, 2)
}

beforeEach(resetStockPageTest)

describe('TC-RES-041 (a) — both row sets are complete', () => {
  it('renders every served panel code and every capital-tracker row', async () => {
    server.use(stockKeyMetricsHandler(KEY_METRICS_FIXTURE))
    renderStockPage()

    await screen.findByTestId('key-metrics-panel')

    for (const code of KEY_METRIC_CODES) {
      expect(
        panel().querySelector(`[data-metric="${code}"]`),
        `${code} row is missing`,
      ).not.toBeNull()
    }
    expect(panel().querySelectorAll('[data-metric]')).toHaveLength(KEY_METRIC_CODES.length)

    for (const key of [
      'paidInCapital',
      'sharesOutstanding',
      'shareCountChangeYoy',
      'payoutRatio',
    ]) {
      expect(
        tracker().querySelector(`[data-row="${key}"]`),
        `${key} tracker row is missing`,
      ).not.toBeNull()
    }
    // The bedelli/bedelsiz history row is its own block.
    expect(
      within(tracker()).getByText(TR('stockPage.capitalTracker.rows.bedelliBedelsizHistory')),
    ).toBeInTheDocument()

    // The four display groups and their labels are rendered.
    for (const group of ['moat', 'valuation', 'health', 'growth']) {
      expect(panel().querySelector(`[data-group="${group}"]`)).not.toBeNull()
    }
  })
})

describe('TC-RES-041 (b) — served value vs. the honest dash', () => {
  it('renders the served current value and "—" for a served-null code, never 0', async () => {
    server.use(stockKeyMetricsHandler(KEY_METRICS_FIXTURE))
    renderStockPage()

    await screen.findByTestId('key-metrics-panel')

    // A served value renders exactly as served (no client computation).
    expect(metricRow('roic')).toHaveTextContent(percent(0.25))
    expect(metricRow('croic')).toHaveTextContent(percent(0.208333))
    expect(metricRow('pe')).toHaveTextContent(formatNumber('tr', 10, { maximumFractionDigits: 2 }))

    // A served-null value is the explicit dash — never "0".
    const roa = metricRow('roa')
    expect(roa.querySelector('[data-slot="no-value"]')).not.toBeNull()
    const roaCurrent = roa.children[1] as HTMLElement
    expect(roaCurrent).toHaveTextContent('—')
    expect(roaCurrent.textContent).toBe('—')
  })

  it('renders the tracker\'s served-null money/count rows as dashes', async () => {
    // The tracker's served-null money/count rows are dashes too.
    const unserved: StockKeyMetricsResponse = {
      ...KEY_METRICS_FIXTURE,
      metrics: Object.fromEntries(
        Object.entries(KEY_METRICS_FIXTURE.metrics).map(([code, entry]) => [
          code,
          {
            ...entry,
            value: null,
            notMeaningful: true,
            sectorMedian: null,
            peerCount: null,
            excludedCount: null,
          } satisfies KeyMetricEntry,
        ]),
      ),
      capitalTracker: {
        ...KEY_METRICS_FIXTURE.capitalTracker,
        paidInCapital: null,
        paidInCapitalAsOf: null,
        sharesOutstanding: null,
        sharesOutstandingAsOf: null,
        sharesOutstandingBasis: null,
        payoutRatio: { value: null, notMeaningful: true, asOf: '2026-10-06' },
      },
    }
    server.use(stockKeyMetricsHandler(unserved))
    renderStockPage()

    const track = await screen.findByTestId('capital-tracker')
    for (const key of ['paidInCapital', 'sharesOutstanding', 'payoutRatio']) {
      const row = track.querySelector(`[data-row="${key}"]`) as HTMLElement
      expect(row.querySelector('[data-slot="no-value"]')).toHaveTextContent('—')
      expect(row.textContent).not.toMatch(/(^|\D)0(\D|$)/)
    }
  })
})

describe('TC-RES-041 (c) — the 5Y company median is honest about its depth', () => {
  it('renders the served disclosure and withholds a thin median as a dash', async () => {
    server.use(stockKeyMetricsHandler(KEY_METRICS_FIXTURE))
    renderStockPage()

    await screen.findByTestId('key-metrics-panel')

    const roic = metricRow('roic')
    // The served median is rendered as served...
    expect(roic).toHaveTextContent(percent(0.22))
    // ...with its nominal window, observations, actual depth and floor disclosed.
    expect(roic.querySelector('[data-slot="company-median-disclosure"]')).toHaveTextContent(
      fill(TR('stockPage.keyMetrics.medianDisclosure'), {
        window: 5,
        observations: 14,
        depth: formatDecimal('tr', 3.5, 2),
        minimum: 12,
      }),
    )

    // Below the observation floor the served value is null → dash, never a
    // two-observation "median" dressed as a five-year statistic.
    const croic = metricRow('croic')
    expect(croic.querySelector('[data-slot="company-median-disclosure"]')).toHaveTextContent(
      fill(TR('stockPage.keyMetrics.medianDisclosure'), {
        window: 5,
        observations: 2,
        depth: formatDecimal('tr', 0.02, 2),
        minimum: 12,
      }),
    )
    const medianCell = croic.children[2] as HTMLElement
    expect(medianCell.querySelector('[data-slot="no-value"]')).toHaveTextContent('—')
    expect(medianCell).not.toHaveTextContent(percent(0.15))
  })
})

describe('TC-RES-041 (d) — sector median with peer and exclusion counts', () => {
  it('renders the served median, the peer count and the gated exclusion disclosure', async () => {
    server.use(stockKeyMetricsHandler(KEY_METRICS_FIXTURE))
    renderStockPage()

    await screen.findByTestId('key-metrics-panel')

    const pe = metricRow('pe')
    expect(pe.querySelector('[data-slot="sector-median"]')).toHaveTextContent(
      formatNumber('tr', 10, { maximumFractionDigits: 2 }),
    )
    expect(pe.querySelector('[data-slot="peer-count"]')).toHaveTextContent(
      fill(TR('stockPage.keyMetrics.peerCount'), { count: 3 }),
    )
    expect(pe.querySelector('[data-slot="exclusion-count"]')).toHaveTextContent(
      fill(TR('stockPage.keyMetrics.exclusionCount'), { count: 2 }),
    )

    // A code with no served sector median renders the dash, never a fabricated median.
    const roa = metricRow('roa')
    expect(roa.querySelector('[data-slot="sector-median"]')).toHaveTextContent('—')
  })
})

describe('TC-RES-041 (e) — the regime-break marker', () => {
  it('marks a spanning CAGR and leaves a one-side CAGR unmarked, with the footnote', async () => {
    server.use(stockKeyMetricsHandler(KEY_METRICS_FIXTURE))
    renderStockPage()

    await screen.findByTestId('key-metrics-panel')

    expect(
      metricRow('rev_cagr_3y').querySelector('[data-slot="regime-break-marker"]'),
    ).not.toBeNull()
    expect(
      metricRow('rev_cagr_5y').querySelector('[data-slot="regime-break-marker"]'),
    ).toBeNull()
    expect(panel().querySelector('[data-slot="regime-break-notice"]')).toHaveTextContent(
      TR('stockPage.keyMetrics.regimeBreakNotice'),
    )
    // The marker is distinguishable from the restatement asterisk.
    expect(
      metricRow('rev_cagr_3y').querySelector('[data-slot="restated-marker"]'),
    ).toBeNull()
  })
})

describe('TC-RES-041 (f) — the TMS 29 distortion alert', () => {
  it('renders the alert from the served monetary-gain ratio and not when unserved', async () => {
    server.use(stockKeyMetricsHandler(KEY_METRICS_FIXTURE))
    renderStockPage()

    await screen.findByTestId('key-metrics-panel')
    // The fixture's monetary_gain_pretax is honestly unserved → no alert.
    expect(screen.queryByTestId('tms29-alert')).toBeNull()

    server.use(
      stockKeyMetricsHandler({
        ...KEY_METRICS_FIXTURE,
        metrics: {
          ...KEY_METRICS_FIXTURE.metrics,
          monetary_gain_pretax: {
            ...KEY_METRICS_FIXTURE.metrics.monetary_gain_pretax,
            value: 0.4,
            notMeaningful: false,
          },
        },
      }),
    )
    renderStockPage()

    const alert = await screen.findByTestId('tms29-alert')
    expect(alert).toHaveTextContent(TR('stockPage.keyMetrics.tms29Alert.title'))
    expect(alert).toHaveTextContent(percent(0.4))
  })
})

describe('TC-RES-041 (g) — the bedelli/bedelsiz history', () => {
  it('renders the dated served actions with the corporate-action-adjusted disclaimer', async () => {
    server.use(stockKeyMetricsHandler(KEY_METRICS_FIXTURE))
    renderStockPage()

    const track = await screen.findByTestId('capital-tracker')

    const bonus = track.querySelector('[data-action-date="2026-03-02"]') as HTMLElement
    expect(bonus).toHaveTextContent(TR('stockPage.capitalTracker.actionKinds.bedelsiz'))
    expect(bonus.querySelector('[data-slot="action-percent"]')).toHaveTextContent('10%')

    const split = track.querySelector('[data-action-date="2025-06-02"]') as HTMLElement
    expect(split).toHaveTextContent(TR('stockPage.capitalTracker.actionKinds.split'))
    expect(split.querySelector('[data-slot="action-percent"]')).toHaveTextContent('500%')

    expect(track.querySelector('[data-slot="adjusted-notice"]')).toHaveTextContent(
      TR('stockPage.capitalTracker.adjustedDisclaimer'),
    )
  })
})

describe('TC-RES-041 (h) — the share-count Y/Y N/M', () => {
  it('renders the honest no-count-history state, never a guessed change', async () => {
    server.use(stockKeyMetricsHandler(KEY_METRICS_FIXTURE))
    renderStockPage()

    const track = await screen.findByTestId('capital-tracker')

    const row = track.querySelector('[data-row="shareCountChangeYoy"]') as HTMLElement
    expect(row.querySelector('[data-slot="no-value"]')).toHaveTextContent('—')
    expect(row).toHaveTextContent(TR('honestData.notMeaningful'))
    expect(row).toHaveTextContent(TR('stockPage.capitalTracker.noCountHistory'))
    // The label carries "%"; the value is never a percent figure.
    expect(row.textContent).not.toMatch(/\d+([.,]\d+)?%/)
  })
})

describe('TC-RES-041 (i) — basis honesty on ROE and EPS CAGR', () => {
  it('labels the served group-basis rows as group/consolidated, never Parent', async () => {
    server.use(stockKeyMetricsHandler(KEY_METRICS_FIXTURE))
    renderStockPage()

    await screen.findByTestId('key-metrics-panel')

    expect(metricRow('roe')).toHaveTextContent(TR('stockPage.keyMetrics.rows.roe'))
    expect(TR('stockPage.keyMetrics.rows.roe')).not.toMatch(/Ana Ortaklık|Parent/i)

    // The spec names "EPS Parent"; the served code is group basis, so the label
    // must never claim Parent (a mislabelled group figure is forbidden).
    for (const code of ['eps_cagr_3y', 'eps_cagr_5y']) {
      expect(metricRow(code)).toHaveTextContent(TR(`stockPage.keyMetrics.rows.${code}`))
      expect(metricRow(code).textContent).not.toMatch(/Ana Ortaklık|Parent/i)
    }
    expect(TR('stockPage.keyMetrics.rows.eps_cagr_3y')).toMatch(/Grup\/Konsolide/)
  })
})

describe('TC-RES-041 (j) — a financial-sector profile', () => {
  it('shows the notice and only the profile-appropriate rows', async () => {
    server.use(stockKeyMetricsHandler(BANK_KEY_METRICS_FIXTURE))
    renderStockPage()

    await screen.findByTestId('key-metrics-panel')

    expect(screen.getByTestId('key-metrics-sector-notice')).toHaveTextContent(
      'bank',
    )
    // Profile-appropriate rows stay (roe/pe/bvps CAGR compute for a financial).
    expect(panel().querySelector('[data-metric="roe"]')).not.toBeNull()
    expect(panel().querySelector('[data-metric="pe"]')).not.toBeNull()
    expect(panel().querySelector('[data-metric="bvps_cagr_3y"]')).not.toBeNull()
    // Industrial-shaped (served `profileGated`) rows are omitted, never dashed.
    expect(panel().querySelector('[data-metric="roic"]')).toBeNull()
    expect(panel().querySelector('[data-metric="ev_ebit"]')).toBeNull()
    expect(panel().querySelector('[data-metric="net_debt_fcf"]')).toBeNull()
  })
})

describe('TC-RES-041 (k) — EN labels the same row set', () => {
  it('renders the EN catalog copy in both blocks', async () => {
    server.use(stockKeyMetricsHandler(KEY_METRICS_FIXTURE))
    renderStockPage(['/stocks/ALFA'], { language: 'en' })

    await screen.findByTestId('key-metrics-panel')

    expect(screen.getByTestId('key-metrics-panel')).toHaveTextContent(
      EN('stockPage.keyMetrics.title'),
    )
    expect(metricRow('roic')).toHaveTextContent(EN('stockPage.keyMetrics.rows.roic'))
    expect(screen.getByTestId('capital-tracker')).toHaveTextContent(
      EN('stockPage.capitalTracker.title'),
    )
    expect(EN('stockPage.capitalTracker.rows.sharesOutstanding')).toContain(
      'Split-Adjusted',
    )
  })

  it('has TR/EN parity for every referenced catalog key', () => {
    // The parity gate owns the full scan; this asserts the panel's own families
    // exist in both catalogs (the container-level check).
    for (const lang of ['tr', 'en'] as const) {
      expect(CATALOGS[lang].stockPage.keyMetrics.title).toBeTruthy()
      expect(CATALOGS[lang].stockPage.capitalTracker.title).toBeTruthy()
      for (const code of KEY_METRIC_CODES) {
        expect(
          catalogValue(lang, `stockPage.keyMetrics.rows.${code}`),
          `${lang} label for ${code}`,
        ).toBeTruthy()
      }
    }
  })
})
