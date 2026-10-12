/* eslint-disable react-refresh/only-export-components -- test harness: stub route
   components + fixtures/handlers/render helpers intentionally share one test-only module. */
import { Link, MemoryRouter, Route, Routes, useParams } from 'react-router'
import { http, HttpResponse } from 'msw'

import type {
  MissingDataResponse,
  StockBalanceSheetResponse,
  StockDividendsResponse,
  StockFinancialsResponse,
  StockGrowthResponse,
  StockOverviewResponse,
  StockProfitabilityResponse,
} from '@/lib/api/contracts'
import { StockPage } from '@/screens/stock-page/StockPage'
import { API_BASE } from '@/test/msw/handlers'
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

export const DIVIDENDS_FIXTURE: StockDividendsResponse = {
  asOf: '2026-10-06',
  stale: false,
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

export function stockDividendsHandler(body: StockDividendsResponse = DIVIDENDS_FIXTURE) {
  return http.get(`${API_BASE}/stocks/:symbol/dividends`, () =>
    HttpResponse.json(body),
  )
}

/** Register the default handlers for every stock-page section. */
export function registerStockPageHandlers() {
  server.use(
    stockOverviewHandler(),
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
export function renderStockPage(initialEntries: string[] = ['/stocks/ALFA']) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/stocks/:symbol/dcf" element={<DcfStub />} />
        <Route path="/stocks/:symbol" element={<StockPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

/** Interpolate an i18n template the way i18next would. */
export function fill(template: string, vars: Record<string, string | number>): string {
  return template.replace(/\{\{(\w+)\}\}/g, (_match, key: string) =>
    String(vars[key] ?? ''),
  )
}
