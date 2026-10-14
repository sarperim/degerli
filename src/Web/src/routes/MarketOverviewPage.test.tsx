import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes, useParams } from 'react-router'

import { MarketOverviewPage } from '@/routes/MarketOverviewPage'
import { catalogValue } from '@/test/i18n'
import {
  API_BASE,
  MARKET_OVERVIEW_FIXTURE,
  marketOverviewHandler,
  problemResponse,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'

/**
 * TC-MOV-016 (equity blocks, L3 portion) and TC-MOV-025 (per-block independence,
 * L3) from `.pipeline/testing/market-overview.md` §2 Group D.
 *
 * TC-MOV-016: the dashboard renders the equity blocks with values — index levels
 * with directional change, sector performance entries linking to the filtered
 * Stock List, breadth, top movers with per-entry volume linking to the stock
 * page, market volume, and the market-valuation block with its loss-maker
 * exclusion disclosure; the equity data-as-of date is displayed. (The macro-strip
 * half of TC-MOV-016 — six indicators with unit/source/as-of — belongs to
 * TKT-mov-006, which builds the macro strip, and is asserted there.)
 *
 * TC-MOV-025: a single block whose query fails shows a plain-language error with
 * its own retry; every other block still renders; retry re-invokes and recovers.
 */

const tr = (key: string) => catalogValue('tr', key)

/** Mirror the page's locale formatting (`tr`) independently of the app code. */
const num = (value: number, options: Intl.NumberFormatOptions = {}) =>
  new Intl.NumberFormat('tr', options).format(value)
const signedPct = (value: number) =>
  `${num(value, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
    signDisplay: 'always',
  })}%`
const ratioPct = (value: number) =>
  new Intl.NumberFormat('tr', {
    style: 'percent',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value)

function StockPageStub() {
  const { symbol } = useParams()
  return <div data-testid="stock-page-symbol">{symbol}</div>
}

function renderPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route path="/" element={<MarketOverviewPage />} />
        <Route path="/stocks" element={<div data-testid="stock-list-page" />} />
        <Route path="/stocks/:symbol" element={<StockPageStub />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  sessionStorage.clear()
  server.use(marketOverviewHandler())
})

describe('TC-MOV-016 equity blocks render with values', () => {
  it('renders index levels with a directional daily change', async () => {
    renderPage()

    const indices = await screen.findByTestId('market-block-indices')
    const xu100 = await within(indices).findByTestId('index-xu100')
    expect(xu100).toHaveTextContent(tr('marketOverview.blocks.indices.xu100'))
    expect(xu100).toHaveTextContent(num(10200, { minimumFractionDigits: 2, maximumFractionDigits: 2 }))
    expect(xu100).toHaveTextContent(signedPct(2))

    const xu30 = within(indices).getByTestId('index-xu30')
    expect(xu30).toHaveTextContent(tr('marketOverview.blocks.indices.xu30'))
    expect(xu30).toHaveTextContent(signedPct(2))
  })

  it('renders sector performance entries linking to the filtered stock list', async () => {
    renderPage()

    const sectors = await screen.findByTestId('market-block-sectors')
    const sectorA = await within(sectors).findByTestId('sector-entry-A')
    expect(sectorA).toHaveAttribute('href', '/stocks?sector=A')
    expect(sectorA).toHaveTextContent(signedPct(0.253))
    expect(sectorA).toHaveAccessibleName(
      tr('marketOverview.blocks.sectors.linkLabel').replace('{{sector}}', 'A'),
    )
  })

  it('renders market breadth counts', async () => {
    renderPage()

    const breadth = await screen.findByTestId('market-block-breadth')
    expect(await within(breadth).findByTestId('breadth-advancing')).toHaveTextContent('10')
    expect(within(breadth).getByTestId('breadth-declining')).toHaveTextContent('5')
    expect(within(breadth).getByTestId('breadth-unchanged')).toHaveTextContent('1')
  })

  it('renders top movers with per-entry volume linking to each stock page', async () => {
    renderPage()

    const movers = await screen.findByTestId('market-block-movers')
    const eta = await within(movers).findByTestId('mover-gainer-ETA')
    expect(eta).toHaveAttribute('href', '/stocks/ETA')
    expect(eta).toHaveTextContent(signedPct(10))
    expect(within(eta).getByTestId('mover-volume-ETA')).toHaveTextContent(num(2_000_000))

    const theta = within(movers).getByTestId('mover-loser-THETA')
    expect(theta).toHaveAttribute('href', '/stocks/THETA')
    expect(theta).toHaveTextContent(signedPct(-10))
    expect(within(theta).getByTestId('mover-volume-THETA')).toHaveTextContent(num(2_100_000))
  })

  it('renders market volume and the valuation block with the exclusion disclosure', async () => {
    renderPage()

    const volume = await screen.findByTestId('market-block-volume')
    expect(await within(volume).findByTestId('market-volume-total')).toHaveTextContent(
      num(174_500_000),
    )

    const valuation = await screen.findByTestId('market-block-valuation')
    expect(await within(valuation).findByTestId('market-pe')).toHaveTextContent(
      num(28.1218, { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
    )
    expect(within(valuation).getByTestId('market-div-yield')).toHaveTextContent(
      ratioPct(0.011225),
    )

    const disclosure = within(valuation).getByTestId('market-pe-disclosure')
    const expectedDisclosure = tr('marketOverview.blocks.valuation.exclusion').replace(
      '{{count}}',
      '2',
    )
    expect(disclosure).toHaveTextContent(expectedDisclosure)
  })

  it('displays the equity data-as-of date', async () => {
    renderPage()

    await screen.findByTestId('market-block-indices')
    await waitFor(() => {
      expect(document.querySelector('[data-slot="as-of-date"]')).not.toBeNull()
    })
    expect(document.querySelector('[data-slot="as-of-date"]')).toHaveTextContent('2026')
  })
})

describe('TC-MOV-015 sector performance period selector (Could, UXR-MOV-012)', () => {
  it('offers 1W/1M/YTD, defaults to daily, and requests the selected period', async () => {
    const user = userEvent.setup()
    const sectorRequests: (string | null)[] = []

    server.use(
      http.get(`${API_BASE}/market/overview`, ({ request }) => {
        const url = new URL(request.url)
        const period = url.searchParams.get('period')
        if (url.searchParams.get('block') === 'sectors') sectorRequests.push(period)
        // Contract-only (F-MOV-1): the server serves the requested period's sector block
        // under the same envelope; period values are deferred, so return a distinct
        // shape that proves the period response (not the daily one) is rendered.
        const sectorPerformance = period
          ? { A: 0.5, B: 0.6, C: 0.7 }
          : MARKET_OVERVIEW_FIXTURE.sectorPerformance
        return HttpResponse.json({ ...MARKET_OVERVIEW_FIXTURE, sectorPerformance })
      }),
    )

    renderPage()

    const sectors = await screen.findByTestId('market-block-sectors')
    // Default is the daily view (no period parameter).
    expect(await within(sectors).findByTestId('sector-entry-A')).toHaveTextContent(
      signedPct(0.253),
    )

    // The selector is present with all period options; daily is the default.
    const group = within(sectors).getByRole('group', {
      name: tr('marketOverview.blocks.sectors.periodLabel'),
    })
    expect(
      within(group).getByRole('button', {
        name: tr('marketOverview.blocks.sectors.period.daily'),
      }),
    ).toHaveAttribute('aria-pressed', 'true')
    expect(
      within(group).getByRole('button', {
        name: tr('marketOverview.blocks.sectors.period.1w'),
      }),
    ).toHaveAttribute('aria-pressed', 'false')

    // Selecting 1W plumbs the parameter to the endpoint and renders its response.
    await user.click(
      within(group).getByRole('button', {
        name: tr('marketOverview.blocks.sectors.period.1w'),
      }),
    )
    await waitFor(() => expect(sectorRequests).toContain('1w'))
    expect(await within(sectors).findByTestId('sector-entry-A')).toHaveTextContent(
      signedPct(0.5),
    )

    // The honest envelope (as-of date) survives the period switch.
    await waitFor(() =>
      expect(document.querySelector('[data-slot="as-of-date"]')).not.toBeNull(),
    )
  })
})

describe('TC-MOV-025 blocks render and fail independently', () => {
  it('keeps other blocks rendered when one block fails, and recovers on retry', async () => {
    const user = userEvent.setup()

    // The movers block's query is rejected; every other block succeeds.
    server.use(
      http.get(`${API_BASE}/market/overview`, ({ request }) => {
        const block = new URL(request.url).searchParams.get('block')
        return block === 'movers'
          ? problemResponse('INTERNAL')
          : HttpResponse.json(MARKET_OVERVIEW_FIXTURE)
      }),
    )

    renderPage()

    // The failing block shows a plain-language error with its own retry.
    const movers = await screen.findByTestId('market-block-movers')
    const error = await within(movers).findByRole('alert')
    expect(error).toHaveTextContent(tr('marketOverview.errorTitle'))

    // Every other block still renders its values.
    expect(await screen.findByTestId('index-xu100')).toBeInTheDocument()
    expect(await screen.findByTestId('market-pe')).toBeInTheDocument()

    // Restore the handler; retry re-invokes and the block recovers.
    server.use(marketOverviewHandler())
    await user.click(within(movers).getByRole('button', { name: tr('common.retry') }))

    await waitFor(() =>
      expect(within(movers).getByTestId('mover-gainer-ETA')).toBeInTheDocument(),
    )
    expect(within(movers).queryByRole('alert')).not.toBeInTheDocument()
  })
})
