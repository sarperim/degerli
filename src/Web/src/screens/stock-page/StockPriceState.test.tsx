import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'

import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import { PRICE_STATE_SERVER_CHANGE } from '@/test/msw/handlers'
import { fillTemplate, sessionClockTime } from '@/test/priceState'
import {
  CLOSE_OVERVIEW,
  COLD_START_OVERVIEW,
  INTRADAY_AFTER_CLOSE_OVERVIEW,
  INTRADAY_OVERVIEW,
  PUBLISHED_OVERVIEW,
  renderStockPage,
  resetStockPageTest,
  stockOverviewHandler,
} from '@/screens/stock-page/testSupport'

/**
 * TKT-foundation-049 — the SCR-005 price header renders the frozen `priceState`
 * contract (`01` §10.9.x; TKT-foundation-047 item 4): the intraday refresh with
 * its not-final last-update time, the 18:00→EOD window staying intraday, the
 * completed session's close dated by `sessionDate`, the cold-start payload with
 * no previous close (no fabricated change), and the EOD fallback when the payload
 * carries no `priceState` (never a fabricated price, never a
 * stale-labelled-as-fresh value).
 *
 * The instrument endpoint is where the producer serves the full numeric block
 * (`GetInstrumentStateAsync`), so the value/change assertions live here; the
 * market overview's numerics are `null` and are asserted on SCR-001.
 *
 * Screen-alignment style (the 036/039 precedent): the ticket's acceptance is
 * explicit, so these cases carry the ticket tag and extend TC-RES-005 (the
 * SCR-005 header lane of FR-RES-005..018).
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

const currency = (value: number) =>
  new Intl.NumberFormat('tr', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value)

beforeEach(resetStockPageTest)

describe('TKT-foundation-049 — SCR-005 price header (TC-RES-005 extension)', () => {
  it('renders the intraday state: symbol, last price, served change and the not-final update time', async () => {
    server.use(stockOverviewHandler(INTRADAY_OVERVIEW))
    renderStockPage()

    const header = await screen.findByTestId('price-state')
    expect(header).toHaveAttribute('data-price-state', 'intraday')
    expect(within(header).getByTestId('price-state-symbol')).toHaveTextContent('ALFA')
    expect(within(header).getByTestId('price-state-value')).toHaveTextContent(
      currency(10345.67),
    )
    // The server's fraction (0.014281) renders as +1,43% (TKT-foundation-036).
    expect(within(header).getByTestId('price-state-change')).toHaveTextContent('+1,43%')
    const asOf = within(header).getByTestId('price-state-asof')
    expect(asOf).toHaveTextContent(
      fillTemplate(TR('priceState.intraday.label'), { time: '15:15' }),
    )
    expect(asOf).toHaveTextContent(sessionClockTime('2026-10-22T12:15:00Z'))
    // "not final" is carried by text, never by colour alone (UXR-G-009). The
    // literal token is asserted directly so a catalog edit that drops the wording
    // fails here instead of passing self-referentially (review round 1, DR-1).
    expect(asOf).toHaveTextContent('kesinleşmedi')
    expect(within(header).queryByTestId('price-state-session-date')).toBeNull()
  })

  it('never relabels the 18:00→EOD window as close (intraday with its last asOf)', async () => {
    server.use(stockOverviewHandler(INTRADAY_AFTER_CLOSE_OVERVIEW))
    renderStockPage()

    const header = await screen.findByTestId('price-state')
    expect(header).toHaveAttribute('data-price-state', 'intraday')
    expect(within(header).getByTestId('price-state-asof')).toHaveTextContent(
      fillTemplate(TR('priceState.intraday.label'), { time: '18:05' }),
    )
    expect(within(header).getByTestId('price-state-asof')).toHaveTextContent(
      sessionClockTime('2026-10-22T15:05:00Z'),
    )
    expect(within(header).queryByTestId('price-state-session-date')).toBeNull()
  })

  it('renders the completed session close, dated by sessionDate', async () => {
    server.use(stockOverviewHandler(CLOSE_OVERVIEW))
    renderStockPage()

    const header = await screen.findByTestId('price-state')
    expect(header).toHaveAttribute('data-price-state', 'close')
    expect(within(header).getByTestId('price-state-value')).toHaveTextContent(
      currency(10412.5),
    )
    expect(within(header).getByTestId('price-state-change')).toHaveTextContent('+2,08%')
    // A date-only `sessionDate` keeps its own civil date in every browser zone
    // (the close-date expectation is pinned literally, not derived from the
    // formatter, so a west-of-UTC day shift fails here — review round 1, DR-2).
    expect(within(header).getByTestId('price-state-session-date')).toHaveTextContent(
      fillTemplate(TR('priceState.close.label'), { date: '22 Eki 2026' }),
    )
    expect(within(header).queryByTestId('price-state-asof')).toBeNull()
  })

  /**
   * Acceptance (ii) — never fabricated, at a **cold start**: the instrument has
   * no previous stored close yet, so `prevClose`/`changePct` are `null` while the
   * served `lastPrice` is real. The change and the direction arrow must be absent,
   * never a `+0,00%` (TKT-foundation-047: "never a fabricated 0").
   */
  it('omits the change entirely when changePct is null (cold start)', async () => {
    server.use(stockOverviewHandler(COLD_START_OVERVIEW))
    renderStockPage()

    const header = await screen.findByTestId('price-state')
    expect(header).toHaveAttribute('data-price-state', 'intraday')
    expect(within(header).getByTestId('price-state-value')).toHaveTextContent(
      currency(10345.67),
    )
    expect(within(header).queryByTestId('price-state-change')).toBeNull()
    expect(header.textContent).not.toContain('%')
    expect(header.textContent).not.toContain('0,00%')
  })

  /**
   * Acceptance (ii) — never fabricated: the change is the **served** `changePct`.
   * This double is internally inconsistent (100 vs a 200 previous close implies
   * −50%) while the server says +50%; rendering +50,00% proves the UI computes
   * nothing of its own and a client-side formula fails the assertion.
   */
  it('renders the server-provided changePct, never a client-side computation', async () => {
    server.use(
      stockOverviewHandler({
        ...INTRADAY_OVERVIEW,
        priceState: PRICE_STATE_SERVER_CHANGE,
      }),
    )
    renderStockPage()

    const header = await screen.findByTestId('price-state')
    expect(within(header).getByTestId('price-state-change')).toHaveTextContent('+50,00%')
    expect(within(header).getByTestId('price-state-change')).not.toHaveTextContent('-50,00%')
  })

  it('renders the labels in English when English is active', async () => {
    server.use(stockOverviewHandler(INTRADAY_OVERVIEW))
    renderStockPage(['/stocks/ALFA'], { language: 'en' })

    const header = await screen.findByTestId('price-state')
    expect(header).toHaveAttribute('aria-label', EN('priceState.label'))
    const asOf = within(header).getByTestId('price-state-asof')
    expect(asOf).toHaveTextContent(
      fillTemplate(EN('priceState.intraday.label'), { time: '15:15' }),
    )
    expect(asOf).toHaveTextContent('not final')
    expect(within(header).getByTestId('price-state-change')).toHaveTextContent('+1.43%')
  })

  it('renders the close label in English when English is active', async () => {
    server.use(stockOverviewHandler(CLOSE_OVERVIEW))
    renderStockPage(['/stocks/ALFA'], { language: 'en' })

    const header = await screen.findByTestId('price-state')
    expect(within(header).getByTestId('price-state-session-date')).toHaveTextContent(
      fillTemplate(EN('priceState.close.label'), { date: 'Oct 22, 2026' }),
    )
  })

  /**
   * Acceptance (ii): with no `priceState` the header shows the existing EOD
   * envelope only — its as-of date and stale flag, no price value. The server
   * never sends a fresher price, so the UI must not imply one.
   */
  it('degrades to the EOD as-of display when priceState is missing', async () => {
    server.use(stockOverviewHandler(PUBLISHED_OVERVIEW))
    renderStockPage()

    const header = await screen.findByTestId('price-state')
    expect(header).toHaveAttribute('data-price-state', 'eod')
    expect(header.querySelector('[data-slot="as-of-date"]')).toHaveTextContent(
      '6 Eki 2026',
    )
    expect(within(header).queryByTestId('price-state-value')).toBeNull()
    expect(within(header).queryByTestId('price-state-change')).toBeNull()
    // No state label is invented for a payload that carries none.
    expect(header.textContent).not.toContain(TR('priceState.intraday.label').replace('{{time}}', ''))
    expect(header.textContent).not.toContain(TR('priceState.close.label').replace('{{date}}', ''))
  })
})
