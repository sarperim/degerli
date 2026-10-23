import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'

import { catalogValue } from '@/test/i18n'
import { PRICE_STATE_SERVER_CHANGE } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
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
 * TKT-foundation-055(b) — the SCR-005 daily-movement block.
 *
 * The user finding (2026-10-20, dev stack): the day's price and change were not
 * readable as part of the page's identity — the stock page said which stock it
 * was (TKT-foundation-057's `SYMBOL — Name` anchor) but the movement sat apart
 * from it. The planner-pinned minimal shape: the movement block — `lastPrice` +
 * `changePct` + the state's honest label — renders **inside the identity anchor**,
 * one glanceable unit ahead of the description.
 *
 * It is a renderer over the landed `priceState` contract (TKT-foundation-047/049):
 * no client-side computation, and every state degrades honestly (no fabricated
 * change, no inferred price, no relabelled intraday refresh).
 *
 * Acceptance (i) of the ticket; extends the TC-RES-005 header lane.
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

const currency = (value: number) =>
  new Intl.NumberFormat('tr', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value)

beforeEach(resetStockPageTest)

describe('TKT-foundation-055 (TC-RES-005 extension) — SCR-005 daily-movement block', () => {
  it('TC-RES-005 — renders the served last price and change beside the identity anchor', async () => {
    server.use(stockOverviewHandler(INTRADAY_OVERVIEW))
    renderStockPage()

    const anchor = await screen.findByTestId('stock-page-identity-anchor')

    // The identity anchor keeps 057's shape and is part of the same unit.
    expect(await within(anchor).findByTestId('stock-page-identity')).toHaveTextContent(
      fillTemplate(TR('stockPage.identity.withName'), {
        symbol: 'ALFA',
        name: 'Alfa Teknoloji A.Ş.',
      }),
    )

    const movement = await within(anchor).findByTestId('price-state')
    expect(movement).toHaveAttribute('data-price-state', 'intraday')
    expect(within(movement).getByTestId('price-state-value')).toHaveTextContent(
      currency(10345.67),
    )
    expect(within(movement).getByTestId('price-state-change')).toHaveTextContent(
      '+1,43%',
    )
    const asOf = within(movement).getByTestId('price-state-asof')
    expect(asOf).toHaveTextContent(
      fillTemplate(TR('priceState.intraday.label'), {
        time: sessionClockTime('2026-10-22T12:15:00Z'),
      }),
    )
    // "not final" is carried by text, never colour alone.
    expect(asOf).toHaveTextContent('kesinleşmedi')
  })

  it('TC-RES-005 — keeps the identity and the movement one unit, ahead of the description', async () => {
    server.use(stockOverviewHandler(INTRADAY_OVERVIEW))
    renderStockPage()

    const anchor = await screen.findByTestId('stock-page-identity-anchor')
    expect(anchor).toContainElement(await within(anchor).findByTestId('stock-page-identity'))
    expect(anchor).toContainElement(await within(anchor).findByTestId('price-state'))

    // The movement reads before the page description, i.e. inside the header
    // unit rather than below it.
    const description = screen.getByText(TR('screens.stockPage.description'))
    expect(
      anchor.compareDocumentPosition(description) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
  })

  it('TC-RES-005 — never relabels the 18:00→EOD window as a close', async () => {
    server.use(stockOverviewHandler(INTRADAY_AFTER_CLOSE_OVERVIEW))
    renderStockPage()

    const anchor = await screen.findByTestId('stock-page-identity-anchor')
    const movement = await within(anchor).findByTestId('price-state')
    expect(movement).toHaveAttribute('data-price-state', 'intraday')
    expect(within(movement).getByTestId('price-state-asof')).toHaveTextContent(
      fillTemplate(TR('priceState.intraday.label'), {
        time: sessionClockTime('2026-10-22T15:05:00Z'),
      }),
    )
    expect(
      within(movement).queryByTestId('price-state-session-date'),
    ).toBeNull()
  })

  it('TC-RES-005 — renders the completed session close, dated by sessionDate', async () => {
    server.use(stockOverviewHandler(CLOSE_OVERVIEW))
    renderStockPage()

    const anchor = await screen.findByTestId('stock-page-identity-anchor')
    const movement = await within(anchor).findByTestId('price-state')
    expect(movement).toHaveAttribute('data-price-state', 'close')
    expect(within(movement).getByTestId('price-state-value')).toHaveTextContent(
      currency(10412.5),
    )
    expect(within(movement).getByTestId('price-state-change')).toHaveTextContent(
      '+2,08%',
    )
    expect(
      within(movement).getByTestId('price-state-session-date'),
    ).toHaveTextContent(
      fillTemplate(TR('priceState.close.label'), { date: '22 Eki 2026' }),
    )
  })

  it('TC-RES-005 — omits the change entirely on a cold start (never a fabricated 0)', async () => {
    server.use(stockOverviewHandler(COLD_START_OVERVIEW))
    renderStockPage()

    const anchor = await screen.findByTestId('stock-page-identity-anchor')
    const movement = await within(anchor).findByTestId('price-state')
    expect(within(movement).getByTestId('price-state-value')).toHaveTextContent(
      currency(10345.67),
    )
    expect(within(movement).queryByTestId('price-state-change')).toBeNull()
    expect(movement.textContent).not.toContain('%')
  })

  it('TC-RES-005 — renders the served changePct, never a client-side computation', async () => {
    server.use(
      stockOverviewHandler({
        ...INTRADAY_OVERVIEW,
        priceState: PRICE_STATE_SERVER_CHANGE,
      }),
    )
    renderStockPage()

    const anchor = await screen.findByTestId('stock-page-identity-anchor')
    const movement = await within(anchor).findByTestId('price-state')
    expect(within(movement).getByTestId('price-state-change')).toHaveTextContent(
      '+50,00%',
    )
    expect(within(movement).getByTestId('price-state-change')).not.toHaveTextContent(
      '-50,00%',
    )
  })

  it('TC-RES-005 — degrades to the EOD envelope when the payload carries no priceState', async () => {
    server.use(stockOverviewHandler(PUBLISHED_OVERVIEW))
    renderStockPage()

    const anchor = await screen.findByTestId('stock-page-identity-anchor')
    const movement = await within(anchor).findByTestId('price-state')
    expect(movement).toHaveAttribute('data-price-state', 'eod')
    expect(within(movement).queryByTestId('price-state-value')).toBeNull()
    expect(within(movement).queryByTestId('price-state-change')).toBeNull()
  })

  it('TC-RES-005 — renders the movement labels in English when English is active', async () => {
    server.use(stockOverviewHandler(INTRADAY_OVERVIEW))
    renderStockPage(['/stocks/ALFA'], { language: 'en' })

    const anchor = await screen.findByTestId('stock-page-identity-anchor')
    const movement = await within(anchor).findByTestId('price-state')
    expect(movement).toHaveAttribute('aria-label', EN('priceState.label'))
    expect(within(movement).getByTestId('price-state-asof')).toHaveTextContent(
      fillTemplate(EN('priceState.intraday.label'), {
        time: sessionClockTime('2026-10-22T12:15:00Z'),
      }),
    )
    expect(within(movement).getByTestId('price-state-asof')).toHaveTextContent(
      'not final',
    )
    expect(within(movement).getByTestId('price-state-change')).toHaveTextContent(
      '+1.43%',
    )
  })
})
