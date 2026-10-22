/* eslint-disable react-refresh/only-export-components -- test harness: stub route
   components + fixtures/handlers/render helpers intentionally share one test-only module. */
import { Link, MemoryRouter, Route, Routes, useParams } from 'react-router'
import { http, HttpResponse } from 'msw'

import type {
  MissingDataResponse,
  StockBalanceSheetResponse,
  StockFinancialsResponse,
  StockGrowthResponse,
  StockOverviewResponse,
  StockProfitabilityResponse,
  StockValuationResponse,
} from '@/lib/api/contracts'
import type {
  StockDividendsSectionData,
  StockDividendsSectionMissing,
  StockDividendsSectionResponse,
} from '@/screens/stock-page/api'
import { StockPage } from '@/screens/stock-page/StockPage'
import {
  API_BASE,
  PRICE_STATE_CLOSE,
  PRICE_STATE_COLD_START,
  PRICE_STATE_INTRADAY,
  PRICE_STATE_INTRADAY_AFTER_CLOSE,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'
import { useDraftStore } from '@/store/draftStore'

/**
 * Shared harness for the SCR-005 component tests (TKT-res-008).
 *
 * Fixtures mirror the shapes `03-api-design.md` §3 serves, including the honest
 * envelope and the explicit missing-data payload. Handlers are keyed by
 * `:symbol` so a single registration serves any stock; tests override one
 * section's handler to exercise per-section independence (M-5).
 */

export const PUBLISHED_OVERVIEW: StockOverviewResponse = {
  asOf: '2026-10-06',
  stale: false,
  status: 'published',
  textTr: 'Alfa Teknoloji açıklaması.',
  textEn: 'Alfa Teknoloji description.',
  lastReviewedAt: '2026-09-30',
}

export const PREPARING_OVERVIEW: StockOverviewResponse = {
  asOf: '2026-10-06',
  stale: false,
  status: 'preparing',
}

/**
 * SCR-005 price-header doubles (TKT-foundation-049): the same published overview
 * carrying each frozen `priceState` shape. The default fixtures stay without
 * `priceState` so the pre-049 suites keep exercising the EOD degradation path.
 */
export const INTRADAY_OVERVIEW: StockOverviewResponse = {
  ...PUBLISHED_OVERVIEW,
  asOf: '2026-10-22',
  priceState: PRICE_STATE_INTRADAY,
}

/** The 18:00→EOD window: still `intraday`, with the last sweep's `asOf`. */
export const INTRADAY_AFTER_CLOSE_OVERVIEW: StockOverviewResponse = {
  ...INTRADAY_OVERVIEW,
  priceState: PRICE_STATE_INTRADAY_AFTER_CLOSE,
}

/** After the EOD chain: the completed session's close, dated `sessionDate`. */
export const CLOSE_OVERVIEW: StockOverviewResponse = {
  ...PUBLISHED_OVERVIEW,
  asOf: '2026-10-22',
  priceState: PRICE_STATE_CLOSE,
}

/**
 * An instrument's **cold start**: a real `lastPrice` but no previous stored
 * close, so `prevClose`/`changePct` are `null` (TKT-foundation-047). The header
 * must show the value and no change at all.
 */
export const COLD_START_OVERVIEW: StockOverviewResponse = {
  ...INTRADAY_OVERVIEW,
  priceState: PRICE_STATE_COLD_START,
}

export const FINANCIALS_FIXTURE: StockFinancialsResponse = {
  asOf: '2026-10-06',
  stale: false,
  periods: [
    {
      periodType: 'FY',
      fiscalYear: 2025,
      periodEndDate: '2025-12-31',
      revenue: { value: 400, restated: false },
      ebitda: { value: 140, restated: false },
      ebit: { value: 120, restated: false },
      netIncome: { value: 60, restated: false },
      fcf: { value: 50, restated: false },
    },
  ],
}

/**
 * AEFES's real published shape (TKT-foundation-057): the API serves FY2022 beside
 * the Q1-2023 period (24.5B vs 90.5B), keyed by `periodType` + `periodEndDate`.
 * The table must label the second row as a quarter — never "FY2023", which would
 * read as a fabricated −73% collapse.
 */
export const QUARTER_FINANCIALS_FIXTURE: StockFinancialsResponse = {
  asOf: '2026-10-06',
  stale: false,
  periods: [
    {
      periodType: 'FY',
      fiscalYear: 2022,
      periodEndDate: '2022-12-31',
      revenue: { value: 90_500_000_000, restated: false },
      ebitda: { value: 12_000_000_000, restated: false },
      ebit: { value: 9_000_000_000, restated: false },
      netIncome: { value: 5_000_000_000, restated: false },
      fcf: { value: 3_000_000_000, restated: false },
    },
    {
      periodType: 'Q',
      fiscalYear: 2023,
      periodEndDate: '2023-03-31',
      revenue: { value: 24_500_000_000, restated: false },
      ebitda: { value: 3_200_000_000, restated: false },
      ebit: { value: 2_400_000_000, restated: false },
      netIncome: { value: 1_300_000_000, restated: false },
      fcf: { value: 900_000_000, restated: false },
    },
  ],
}

export const RESTATED_FINANCIALS_FIXTURE: StockFinancialsResponse = {
  asOf: '2026-10-06',
  stale: false,
  periods: [
    {
      periodType: 'FY',
      fiscalYear: 2025,
      periodEndDate: '2025-12-31',
      revenue: { value: 400, restated: false },
      ebitda: { value: 140, restated: false },
      ebit: { value: 120, restated: false },
      netIncome: { value: 75, restated: true },
      fcf: { value: 50, restated: false },
    },
  ],
}

export const PROFITABILITY_FIXTURE: StockProfitabilityResponse = {
  asOf: '2026-10-06',
  stale: false,
  metrics: {
    roic: { value: 0.25, notMeaningful: false },
    roe: { value: 0.2, notMeaningful: false },
    gross_margin: { value: 0.35, notMeaningful: false },
    operating_margin: { value: 0.3, notMeaningful: false },
  },
}

export const GROWTH_FIXTURE: StockGrowthResponse = {
  asOf: '2026-10-06',
  stale: false,
  metrics: {
    rev: [
      { window: 3, windowYears: 3, value: 0.12 },
      { window: 5, windowYears: 3, value: 0.1 },
      { window: 10, windowYears: 3, value: 0.08 },
    ],
    eps: [
      { window: 3, windowYears: 3, value: 0.2 },
      { window: 5, windowYears: 3, value: 0.15 },
      { window: 10, windowYears: 3, value: 0.1 },
    ],
    fcf: [
      { window: 3, windowYears: 3, value: 0.05 },
      { window: 5, windowYears: 3, value: 0.04 },
      { window: 10, windowYears: 3, value: 0.03 },
    ],
    div: [
      { window: 3, windowYears: 3, value: 0.01 },
      { window: 5, windowYears: 3, value: 0.01 },
      { window: 10, windowYears: 3, value: 0.01 },
    ],
  },
}

export const NO_GROWTH_FIXTURE: MissingDataResponse = {
  asOf: '2026-10-06',
  stale: false,
  state: 'no-data',
  coverage: {
    availableFrom: '2025-08-01',
    boundaryNote: 'insufficient financial history',
  },
}

export const BALANCE_SHEET_FIXTURE: StockBalanceSheetResponse = {
  asOf: '2026-10-06',
  stale: false,
  periodType: 'FY',
  fiscalYear: 2025,
  periodEndDate: '2025-12-31',
  restated: false,
  lines: {
    cash: { value: 100, restated: false },
    shortTermDebt: { value: 40, restated: false },
    longTermDebt: { value: 60, restated: false },
    totalEquity: { value: 500, restated: false },
    currentAssets: { value: 220, restated: false },
    currentLiabilities: { value: 120, restated: false },
    sharesDiluted: { value: 100, restated: false },
    sharesOutstanding: { value: 100, restated: false },
  },
  bookValue: { value: 500, restated: false },
  bookValuePerShare: { value: 5, restated: false },
}

export const DIVIDENDS_FIXTURE: StockDividendsSectionData = {
  asOf: '2026-10-06',
  stale: false,
  dividendsAvailable: true,
  dividends: [
    { exDate: '2026-04-01', payDate: '2026-04-10', amountPerShare: 1.5, currency: 'TRY' },
  ],
  ttmPerShare: 1.5,
}

export const NO_DIVIDENDS_FIXTURE: MissingDataResponse = {
  asOf: '2026-10-06',
  stale: false,
  state: 'no-data',
  coverage: { availableFrom: '2016-04-12', boundaryNote: 'no dividends paid' },
}

/**
 * SCR-005 §7 dividends doubles (TKT-foundation-052): the three honest states the
 * server's availability signal distinguishes. A zero-row table is only "never paid"
 * when the lane reports the data available; otherwise it is the neutral "no data yet".
 */
export const ZERO_DIVIDENDS_FIXTURE: StockDividendsSectionMissing = {
  asOf: '2026-10-06',
  stale: false,
  state: 'no-data',
  dividendsAvailable: true,
  coverage: { availableFrom: '2016-04-12', boundaryNote: 'no dividends paid' },
}

/** No rows and the dividends lane has not covered this instrument yet. */
export const DIVIDENDS_UNAVAILABLE_FIXTURE: StockDividendsSectionMissing = {
  asOf: '2026-10-06',
  stale: false,
  state: 'no-data',
  dividendsAvailable: false,
  coverage: {
    availableFrom: '2016-04-12',
    boundaryNote: 'dividends data not ingested yet',
  },
}

/**
 * A pre-052 payload: the missing-data envelope with **no** availability signal. The
 * section must degrade to the neutral state — an absent signal never licenses the
 * "never paid" claim.
 */
export const LEGACY_DIVIDENDS_FIXTURE: StockDividendsSectionMissing = {
  asOf: '2026-10-06',
  stale: false,
  state: 'no-data',
  coverage: { availableFrom: '2016-04-12', boundaryNote: 'no dividends paid' },
}

/* ------------------------------------------------------------------ *
 * Valuation section (TKT-res-009) — the `GET /stocks/{symbol}/valuation`
 * payload mirroring `StockOverviewAndValuationEndpoints` (`03` §3).
 * ------------------------------------------------------------------ */

/**
 * ALFA's populated valuation (FU §11.4 strip goldens): the adjusted basis with
 * P/E 10.0 next to a 10.0 sector median over 3 peers, 2 loss-makers excluded.
 */
export const VALUATION_FIXTURE: StockValuationResponse = {
  asOf: '2026-10-06',
  stale: false,
  current: { pe: 10.0, pb: 2.0, ev_ebitda: 5.5, ev_fcf: 22.0, fcf_yield: 0.05 },
  historical: {
    adjusted: true,
    series: [
      { date: '2026-10-01', closeRaw: 100, closeAdjusted: 10.0 },
      { date: '2026-10-02', closeRaw: 98, closeAdjusted: 9.8 },
      { date: '2026-10-05', closeRaw: 105, closeAdjusted: 10.5 },
    ],
  },
  strip: {
    pe: { value: 10.0, notMeaningful: false, sectorMedian: 10.0, peerCount: 3, excludedCount: 2 },
    pb: { value: 2.0, notMeaningful: false, sectorMedian: 1.5, peerCount: 5, excludedCount: 0 },
    ev_ebitda: { value: 5.5, notMeaningful: false, sectorMedian: 6.2, peerCount: 5, excludedCount: 0 },
    ev_fcf: { value: 22.0, notMeaningful: false, sectorMedian: 18.4, peerCount: 5, excludedCount: 0 },
    fcf_yield: { value: 0.05, notMeaningful: false, sectorMedian: 0.04, peerCount: 5, excludedCount: 0 },
  },
}

/** BETA's loss-maker P/E (TC-RES-008): own value not-meaningful, median/counts retained. */
export const BETA_VALUATION_FIXTURE: StockValuationResponse = {
  ...VALUATION_FIXTURE,
  current: { ...VALUATION_FIXTURE.current, pe: null },
  strip: {
    ...VALUATION_FIXTURE.strip,
    pe: { value: null, notMeaningful: true, sectorMedian: 12.5, peerCount: 4, excludedCount: 3 },
  },
}

/** A strip whose EV/FCF median is unavailable — honest no-data for that metric only. */
export const NO_MEDIAN_VALUATION_FIXTURE: StockValuationResponse = {
  ...VALUATION_FIXTURE,
  strip: {
    ...VALUATION_FIXTURE.strip,
    ev_fcf: { value: 22.0, notMeaningful: false, sectorMedian: null, peerCount: null, excludedCount: null },
  },
}

/**
 * EPSL's adjusted series (TC-RES-032, TC-MDF-030): the raw close carries a split
 * cliff (100 → 20) while the adjusted close is continuous (50 → 49), so asserting
 * the adjusted basis is chosen proves no fabricated 80% crash is plotted.
 */
export const EPSL_VALUATION_FIXTURE: StockValuationResponse = {
  ...VALUATION_FIXTURE,
  historical: {
    adjusted: true,
    series: [
      { date: '2025-06-02', closeRaw: 100, closeAdjusted: 50 },
      { date: '2025-06-03', closeRaw: 20, closeAdjusted: 49 },
    ],
  },
}

/** A raw-basis history — the disclaimer must not be claimed over raw data. */
export const RAW_VALUATION_FIXTURE: StockValuationResponse = {
  ...VALUATION_FIXTURE,
  historical: {
    adjusted: false,
    series: [
      { date: '2026-10-01', closeRaw: 100, closeAdjusted: null },
      { date: '2026-10-02', closeRaw: 98, closeAdjusted: null },
    ],
  },
}

/** `GET /stocks/:symbol/overview`. */
export function stockOverviewHandler(body: StockOverviewResponse = PUBLISHED_OVERVIEW) {
  return http.get(`${API_BASE}/stocks/:symbol/overview`, () => HttpResponse.json(body))
}

export function stockFinancialsHandler(body: StockFinancialsResponse = FINANCIALS_FIXTURE) {
  return http.get(`${API_BASE}/stocks/:symbol/financials`, () =>
    HttpResponse.json(body),
  )
}

export function stockProfitabilityHandler(
  body: StockProfitabilityResponse = PROFITABILITY_FIXTURE,
) {
  return http.get(`${API_BASE}/stocks/:symbol/profitability`, () =>
    HttpResponse.json(body),
  )
}

export function stockGrowthHandler(body: StockGrowthResponse = GROWTH_FIXTURE) {
  return http.get(`${API_BASE}/stocks/:symbol/growth`, () => HttpResponse.json(body))
}

export function stockBalanceSheetHandler(
  body: StockBalanceSheetResponse = BALANCE_SHEET_FIXTURE,
) {
  return http.get(`${API_BASE}/stocks/:symbol/balance-sheet`, () =>
    HttpResponse.json(body),
  )
}

export function stockDividendsHandler(
  body: StockDividendsSectionResponse = DIVIDENDS_FIXTURE,
) {
  return http.get(`${API_BASE}/stocks/:symbol/dividends`, () =>
    HttpResponse.json(body),
  )
}

export function stockValuationHandler(body: StockValuationResponse = VALUATION_FIXTURE) {
  return http.get(`${API_BASE}/stocks/:symbol/valuation`, () =>
    HttpResponse.json(body),
  )
}

/** Register the default handlers for every stock-page section. */
export function registerStockPageHandlers() {
  server.use(
    stockOverviewHandler(),
    stockValuationHandler(),
    stockFinancialsHandler(),
    stockProfitabilityHandler(),
    stockGrowthHandler(),
    stockBalanceSheetHandler(),
    stockDividendsHandler(),
  )
}

/** Reset the persisted draft store and register the default section handlers. */
export function resetStockPageTest() {
  sessionStorage.clear()
  useDraftStore.getState().resetDrafts()
  registerStockPageHandlers()
}

function DcfStub() {
  const { symbol } = useParams()
  return (
    <div data-testid="dcf-stub">
      <span data-testid="dcf-symbol">{symbol}</span>
      <Link to={`/stocks/${symbol}`}>back-to-stock</Link>
    </div>
  )
}

/** Render the SCR-005 route (plus a DCF hand-off landing) in memory. */
export function renderStockPage(
  initialEntries: string[] = ['/stocks/ALFA'],
  options: { language?: 'tr' | 'en' } = {},
) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/stocks/:symbol/dcf" element={<DcfStub />} />
        <Route path="/stocks/:symbol" element={<StockPage />} />
      </Routes>
    </MemoryRouter>,
    { language: options.language ?? 'tr' },
  )
}

/** Interpolate an i18n template the way i18next would. */
export function fill(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{\{(\w+)\}\}/g, (_match, key: string) =>
    String(vars[key] ?? ''),
  )
}
