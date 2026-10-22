import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'

import { formatAsOfDate } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import { server } from '@/test/msw/server'
import {
  CLOSE_OVERVIEW,
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
 * completed session's close dated by `sessionDate`, and the EOD fallback when the
 * payload carries no `priceState` (never a fabricated price, never a
 * stale-labelled-as-fresh value).
 *
 * Screen-alignment style (the 036/039 precedent): the ticket's acceptance is
 * explicit, so these cases carry the ticket tag and extend TC-RES-005 (the
 * SCR-005 header lane of FR-RES-005..018).
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

const fill = (template: string, vars: Record<string, string>) =>
  Object.entries(vars).reduce(
    (text, [name, value]) => text.replaceAll(`{{${name}}}`, value),
    template,
  )

/** The BIST session clock the doubles are written in (Istanbul is UTC+03). */
const time = (iso: string) =>
  new Intl.DateTimeFormat('tr', {
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZone: 'Europe/Istanbul',
  }).format(new Date(iso))

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
    expect(within(header).getByTestId('price-state-asof')).toHaveTextContent(
      fill(TR('priceState.intraday.label'), { time: '15:15' }),
    )
    expect(within(header).getByTestId('price-state-asof')).toHaveTextContent(
      time('2026-10-22T12:15:00Z'),
    )
    // "not final" is carried by text, never by colour alone (UXR-G-009).
    expect(within(header).getByTestId('price-state-asof')).toHaveTextContent(
      TR('priceState.intraday.label').split('—').pop()!.trim(),
    )
    expect(within(header).queryByTestId('price-state-session-date')).toBeNull()
  })

  it('never relabels the 18:00→EOD window as close (intraday with its last asOf)', async () => {
    server.use(stockOverviewHandler(INTRADAY_AFTER_CLOSE_OVERVIEW))
    renderStockPage()

    const header = await screen.findByTestId('price-state')
    expect(header).toHaveAttribute('data-price-state', 'intraday')
    expect(within(header).getByTestId('price-state-asof')).toHaveTextContent(
      fill(TR('priceState.intraday.label'), { time: '18:05' }),
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
    expect(within(header).getByTestId('price-state-session-date')).toHaveTextContent(
      fill(TR('priceState.close.label'), {
        date: formatAsOfDate('tr', '2026-10-22'),
      }),
    )
    expect(within(header).queryByTestId('price-state-asof')).toBeNull()
  })

  it('renders the labels in English when English is active', async () => {
    server.use(stockOverviewHandler(INTRADAY_OVERVIEW))
    renderStockPage(['/stocks/ALFA'], { language: 'en' })

    const header = await screen.findByTestId('price-state')
    expect(header).toHaveAttribute('aria-label', EN('priceState.label'))
    expect(within(header).getByTestId('price-state-asof')).toHaveTextContent(
      fill(EN('priceState.intraday.label'), { time: '15:15' }),
    )
    expect(within(header).getByTestId('price-state-change')).toHaveTextContent('+1.43%')
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
    expect(
      header.querySelector('[data-slot="as-of-date"]'),
    ).toHaveTextContent(formatAsOfDate('tr', '2026-10-06'))
    expect(within(header).queryByTestId('price-state-value')).toBeNull()
    expect(within(header).queryByTestId('price-state-change')).toBeNull()
    // No state label is invented for a payload that carries none.
    expect(header.textContent).not.toContain(TR('priceState.intraday.label').replace('{{time}}', ''))
    expect(header.textContent).not.toContain(TR('priceState.close.label').replace('{{date}}', ''))
  })
})
