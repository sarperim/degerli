import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes, useParams } from 'react-router'

import { MarketOverviewPage } from '@/routes/MarketOverviewPage'
import type { MarketOverviewResponse } from '@/lib/api/contracts'
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

/**
 * The overview envelope flagged stale (TKT-foundation-039 review round 1):
 * 037's stale means a newer stored session exists that this row lacks, so the
 * badge beside the as-of date is the honest statement and the "most recently
 * completed session" recency claim is suppressed. Local to this suite so the
 * shared MSW fixtures stay untouched.
 */
const STALE_OVERVIEW: MarketOverviewResponse = {
  ...MARKET_OVERVIEW_FIXTURE,
  stale: true,
}

/**
 * Mirror the page's locale formatting independently of the app code. Percent
 * payloads are **fractions** (`0.02` = +2%) — the helper scales, the page's
 * formatter scales, and the literal assertions below pin the result.
 */
const num = (value: number, options: Intl.NumberFormatOptions = {}) =>
  new Intl.NumberFormat('tr', options).format(value)
const signedPct = (fraction: number) =>
  `${num(fraction * 100, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
    signDisplay: 'always',
  })}%`
const ratioPct = (fraction: number) =>
  new Intl.NumberFormat('tr', {
    style: 'percent',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(fraction)

function StockPageStub() {
  const { symbol } = useParams()
  return <div data-testid="stock-page-symbol">{symbol}</div>
}

/**
 * The overview envelope's stale badge — the one beside the page header's as-of
 * date. Scoped through the `<header>` that carries the page `h1` (level 1, so
 * language-independent) instead of a document-wide query, so a badge rendered by
 * a child block could never satisfy the assertion. The header sits inside
 * `<section data-screen="SCR-001">`, which makes it generic markup rather than a
 * `banner` landmark, hence `closest` and not a role query.
 */
function headerStaleBadge(): Element | null {
  return (
    screen
      .getByRole('heading', { level: 1 })
      .closest('header')
      ?.querySelector('[data-slot="stale-badge"]') ?? null
  )
}

function renderPage(language: 'tr' | 'en' = 'tr') {
  return renderWithProviders(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route path="/" element={<MarketOverviewPage />} />
        <Route path="/stocks" element={<div data-testid="stock-list-page" />} />
        <Route path="/stocks/:symbol" element={<StockPageStub />} />
      </Routes>
    </MemoryRouter>,
    { language },
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
    // The payload's `changePct` is a fraction (0.02) — the row must show +2,00%,
    // not +0,02% (TKT-foundation-036; TC-MOV-031).
    expect(xu100).toHaveTextContent('+2,00%')
    expect(xu100).toHaveTextContent(signedPct(0.02))

    const xu30 = within(indices).getByTestId('index-xu30')
    expect(xu30).toHaveTextContent(tr('marketOverview.blocks.indices.xu30'))
    expect(xu30).toHaveTextContent('+2,00%')
    expect(xu30).toHaveTextContent(signedPct(0.02))
  })

  it('renders sector performance entries linking to the filtered stock list', async () => {
    renderPage()

    const sectors = await screen.findByTestId('market-block-sectors')
    const sectorA = await within(sectors).findByTestId('sector-entry-A')
    expect(sectorA).toHaveAttribute('href', '/stocks?sector=A')
    // Cap-weighted sector daily performance is a fraction (0.00253 = +0,25%).
    expect(sectorA).toHaveTextContent('+0,25%')
    expect(sectorA).toHaveTextContent(signedPct(0.00253))
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
    expect(eta).toHaveTextContent('+10,00%')
    expect(eta).toHaveTextContent(signedPct(0.1))
    expect(within(eta).getByTestId('mover-volume-ETA')).toHaveTextContent(num(2_000_000))

    const theta = within(movers).getByTestId('mover-loser-THETA')
    expect(theta).toHaveAttribute('href', '/stocks/THETA')
    expect(theta).toHaveTextContent('-10,00%')
    expect(theta).toHaveTextContent(signedPct(-0.1))
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
    // The dividend yield is already a fraction and is formatted by the
    // fraction-aware `formatRatioPercent` — the 036 fix must not double-scale it
    // (0.011225 → 1,12%, never 112,25%).
    const divYield = within(valuation).getByTestId('market-div-yield')
    expect(divYield).toHaveTextContent(ratioPct(0.011225))
    expect(divYield).toHaveTextContent('1,12')
    expect(divYield).not.toHaveTextContent(ratioPct(1.1225))

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

  /**
   * TKT-foundation-039 (TC-MOV-016, finding B3): the as-of line states what the
   * date means — the latest completed session — so a fresh-but-not-today row is
   * never read as a data error.
   */
  it('explains that the as-of date is the latest completed session', async () => {
    renderPage()

    const explanation = await screen.findByTestId('market-asof-explanation')
    expect(explanation).toHaveTextContent(
      tr('screens.marketOverview.asOfExplanation'),
    )
  })

  it('explains the as-of date in English when English is active', async () => {
    renderPage('en')

    const explanation = await screen.findByTestId('market-asof-explanation')
    expect(explanation).toHaveTextContent(
      catalogValue('en', 'screens.marketOverview.asOfExplanation'),
    )
  })

  /**
   * TKT-foundation-039 review round 1 (DR-1): when 037 marks the envelope stale a
   * newer stored session exists that this row lacks, so the "most recently
   * completed session" claim is false. The badge beside the as-of date states the
   * honest meaning; the recency explanation must not contradict it.
   */
  it('does not claim the latest completed session when the envelope is stale', async () => {
    server.use(marketOverviewHandler(STALE_OVERVIEW))
    renderPage()

    await screen.findByTestId('market-block-indices')
    await waitFor(() => {
      expect(headerStaleBadge()).not.toBeNull()
    })
    expect(screen.queryByTestId('market-asof-explanation')).toBeNull()
    // The contradiction is impossible, not just reworded: the stale explanation
    // travels with the badge in the same language.
    expect(headerStaleBadge()).toHaveAccessibleName(
      `${tr('honestData.stale')} — ${tr('honestData.staleExplanation')}`,
    )
  })

  it('does not claim the latest completed session when stale, in English', async () => {
    server.use(marketOverviewHandler(STALE_OVERVIEW))
    renderPage('en')

    await screen.findByTestId('market-block-indices')
    await waitFor(() => {
      expect(headerStaleBadge()).not.toBeNull()
    })
    expect(screen.queryByTestId('market-asof-explanation')).toBeNull()
    expect(headerStaleBadge()).toHaveAccessibleName(
      `${catalogValue('en', 'honestData.stale')} — ${catalogValue('en', 'honestData.staleExplanation')}`,
    )
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
          ? { A: 0.005, B: 0.006, C: 0.007 }
          : MARKET_OVERVIEW_FIXTURE.sectorPerformance
        return HttpResponse.json({ ...MARKET_OVERVIEW_FIXTURE, sectorPerformance })
      }),
    )

    renderPage()

    const sectors = await screen.findByTestId('market-block-sectors')
    // Default is the daily view (no period parameter).
    expect(await within(sectors).findByTestId('sector-entry-A')).toHaveTextContent(
      signedPct(0.00253),
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
      signedPct(0.005),
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

describe('TC-MOV-031 percentage surfaces render the API fraction ×100', () => {
  /**
   * The real served payload shape (user-verified live `GET /market/overview`,
   * 2026-10-20): `changePct` is a decimal fraction — AGHOL `0.014735` = +1,47%,
   * XU100/TCKRC `-0.024875`/`-0.1` = −2,49% / −10,00%. Rendering these through a
   * non-scaling formatter shows "+0,01%" (the shipped home-screen defect); these
   * assertions are red on that code and green only when every percentage surface
   * scales the fraction by 100 (TKT-foundation-036).
   */
  const FRACTION_PAYLOAD: MarketOverviewResponse = {
    ...MARKET_OVERVIEW_FIXTURE,
    indices: {
      xu100: { level: 10234.56, changePct: 0.014735 },
      xu30: { level: 30123.45, changePct: -0.1 },
    },
    gainers: [{ symbol: 'AGHOL', changePct: 0.014735, volume: 12_345_678 }],
    losers: [{ symbol: 'TCKRC', changePct: -0.1, volume: 2_100_000 }],
    sectorPerformance: { B: 0.022409 },
  }

  beforeEach(() => {
    server.use(marketOverviewHandler(FRACTION_PAYLOAD))
  })

  it('renders a fractional index change as +1,47% (never +0,01%)', async () => {
    renderPage()

    const xu100 = await screen.findByTestId('index-xu100')
    expect(xu100).toHaveTextContent('+1,47%')
    expect(xu100).not.toHaveTextContent('+0,01%')

    // −0.1 is −10,00%, not −0,10%.
    const xu30 = screen.getByTestId('index-xu30')
    expect(xu30).toHaveTextContent('-10,00%')
    expect(xu30).not.toHaveTextContent('-0,10%')
  })

  it('renders a fractional mover change (visible text and aria-label)', async () => {
    renderPage()

    const movers = await screen.findByTestId('market-block-movers')
    const aghol = await within(movers).findByTestId('mover-gainer-AGHOL')
    expect(aghol).toHaveTextContent('+1,47%')
    expect(aghol).not.toHaveTextContent('+0,01%')
    // The link's accessible name carries the same corrected value.
    expect(aghol).toHaveAccessibleName(/AGHOL \+1,47%/)
    expect(aghol).not.toHaveAccessibleName(/\+0,01%/)

    const tckrc = within(movers).getByTestId('mover-loser-TCKRC')
    expect(tckrc).toHaveTextContent('-10,00%')
    expect(tckrc).toHaveAccessibleName(/TCKRC -10,00%/)
  })

  it('renders a fractional sector entry as +2,24% (never +0,02%)', async () => {
    renderPage()

    const sectors = await screen.findByTestId('market-block-sectors')
    const sectorB = await within(sectors).findByTestId('sector-entry-B')
    expect(sectorB).toHaveTextContent('+2,24%')
    expect(sectorB).not.toHaveTextContent('+0,02%')
  })

  it('renders the same fraction in EN locale formatting (+1.47%)', async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<MarketOverviewPage />} />
          <Route path="/stocks" element={<div data-testid="stock-list-page" />} />
          <Route path="/stocks/:symbol" element={<StockPageStub />} />
        </Routes>
      </MemoryRouter>,
      { language: 'en' },
    )

    expect(await screen.findByTestId('index-xu100')).toHaveTextContent('+1.47%')
    expect(screen.getByTestId('index-xu30')).toHaveTextContent('-10.00%')
  })
})
