import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'

import { formatNumber } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'
import { server } from '@/test/msw/server'
import {
  BETA_VALUATION_FIXTURE,
  fill,
  NO_OWN_VALUE_VALUATION_FIXTURE,
  NO_PEERS_VALUATION_FIXTURE,
  renderStockPage,
  resetStockPageTest,
  stockValuationHandler,
  VALUATION_FIXTURE,
  ZERO_PEER_COUNT_VALUATION_FIXTURE,
} from '@/screens/stock-page/testSupport'
import { VsSectorStrip } from '@/screens/stock-page/ValuationSection'

/**
 * TKT-foundation-055(c) — the vs.-sector strip's honest-empty family.
 *
 * The user finding (2026-10-20, dev stack): the sector comparison showed a wall
 * of "Anlamsız" / "0 emsal". In the zero-shares world no share-count-derived
 * multiple and no sector peer median can be computed, so those rows were not
 * verdicts about the stock — they presented missing data as a claim, and a zero
 * peer count as a median basis.
 *
 * The planner-pinned minimal shape: when no peer has a computable metric the
 * block renders the honest "veri yok" family once (TR "Bu sektör için yeterli
 * veri yok" / EN "Not enough data for this sector"); per-row "Anlamsız" verdicts
 * survive only where a computable-but-insignificant comparison genuinely exists,
 * and a zero peer count never renders as a median basis.
 *
 * Acceptance (ii) of the ticket; extends TC-RES-025.
 */

const TR = (key: string) => catalogValue('tr', key)
const EN = (key: string) => catalogValue('en', key)

function strip(): HTMLElement {
  const node = document.querySelector('[data-slot="vs-sector-strip"]')
  if (!(node instanceof HTMLElement)) throw new Error('vs.-sector strip not found')
  return node
}

function metric(container: HTMLElement, code: string): HTMLElement {
  const node = container.querySelector(`[data-metric="${code}"]`)
  if (!(node instanceof HTMLElement)) throw new Error(`strip metric "${code}" not found`)
  return node
}

beforeEach(resetStockPageTest)

describe('TKT-foundation-055 (TC-RES-025 extension) — vs.-sector honest empties', () => {
  it('TC-RES-025 — renders one honest no-data state when no peer has a computable metric', () => {
    renderWithProviders(<VsSectorStrip strip={NO_PEERS_VALUATION_FIXTURE.strip} />)

    const block = strip()
    const empty = within(block).getByTestId('vs-sector-no-data')
    expect(empty).toHaveTextContent(TR('stockPage.valuation.strip.noSectorDataTitle'))
    expect(empty).toHaveTextContent('Bu sektör için yeterli veri yok')

    // No per-row verdicts and no zero-peer median basis at all.
    expect(block.querySelectorAll('[data-metric]')).toHaveLength(0)
    expect(block.textContent).not.toContain(TR('honestData.notMeaningful'))
    expect(block.textContent).not.toContain('emsal')
  })

  it('TC-RES-025 — renders the honest no-data state in English too', () => {
    renderWithProviders(<VsSectorStrip strip={NO_PEERS_VALUATION_FIXTURE.strip} />, {
      language: 'en',
    })

    const block = strip()
    expect(within(block).getByTestId('vs-sector-no-data')).toHaveTextContent(
      EN('stockPage.valuation.strip.noSectorDataTitle'),
    )
    expect(within(block).getByTestId('vs-sector-no-data')).toHaveTextContent(
      'Not enough data for this sector',
    )
    expect(block.textContent).not.toContain(EN('honestData.notMeaningful'))
    expect(block.textContent).not.toContain('peers')
  })

  it('TC-RES-025 — keeps a per-row not-meaningful verdict where a comparison is computable', () => {
    renderWithProviders(<VsSectorStrip strip={BETA_VALUATION_FIXTURE.strip} />)

    const block = strip()
    expect(within(block).queryByTestId('vs-sector-no-data')).toBeNull()

    const pe = metric(block, 'pe')
    expect(pe.querySelector('[data-slot="stock-value"]')).toHaveTextContent(
      TR('honestData.notMeaningful'),
    )
    // The served comparison basis still renders honestly (UXR-RES-024/025).
    expect(pe.querySelector('[data-slot="sector-median"]')).toHaveTextContent(
      formatNumber('tr', 12.5),
    )
    expect(pe.querySelector('[data-slot="peer-count"]')).toHaveTextContent(
      fill(TR('stockPage.valuation.strip.peerCount'), { count: 4 }),
    )
  })

  it('TC-RES-025 — shows the no-data dash, never a not-meaningful verdict, for an uncomputed own value', () => {
    renderWithProviders(
      <VsSectorStrip strip={NO_OWN_VALUE_VALUATION_FIXTURE.strip} />,
    )

    const pe = metric(strip(), 'pe')
    const ownValue = pe.querySelector('[data-slot="stock-value"]')
    expect(ownValue).toHaveTextContent('—')
    expect(ownValue).not.toHaveTextContent(TR('honestData.notMeaningful'))
    expect(pe.querySelector('[data-slot="no-value"]')).toHaveAttribute(
      'title',
      TR('honestData.noDataTitle'),
    )
    expect(pe.querySelector('[data-slot="sector-median"]')).toHaveTextContent(
      formatNumber('tr', 10),
    )
  })

  it('TC-RES-025 — never renders a zero peer count as a median basis', () => {
    renderWithProviders(
      <VsSectorStrip strip={ZERO_PEER_COUNT_VALUATION_FIXTURE.strip} />,
    )

    const block = strip()
    // Other metrics are computable, so the block stays populated…
    expect(within(block).queryByTestId('vs-sector-no-data')).toBeNull()

    const pe = metric(block, 'pe')
    expect(pe.querySelector('[data-slot="stock-value"]')).toHaveTextContent('10')
    expect(pe.querySelector('[data-slot="sector-median"]')).toHaveTextContent(
      TR('stockPage.valuation.strip.noMedian'),
    )
    expect(pe.querySelector('[data-slot="peer-count"]')).toBeNull()
    expect(block.textContent).not.toContain('0 emsal')
  })

  it('TC-RES-025 — the section renders the honest empty for a no-peers payload', async () => {
    server.use(stockValuationHandler(NO_PEERS_VALUATION_FIXTURE))
    renderStockPage()

    const valuation = await screen.findByRole('region', {
      name: TR('stockPage.sections.valuation'),
    })
    const block = await within(valuation).findByText(
      TR('stockPage.valuation.strip.title'),
    )
    const section = block.closest('[data-slot="vs-sector-strip"]')
    if (!(section instanceof HTMLElement)) throw new Error('vs.-sector strip not found')

    expect(within(section).getByTestId('vs-sector-no-data')).toHaveTextContent(
      TR('stockPage.valuation.strip.noSectorDataTitle'),
    )
    expect(section.textContent).not.toContain(TR('honestData.notMeaningful'))
    expect(section.textContent).not.toContain('emsal')
  })
})

/**
 * The populated path stays untouched: the ALFA golden renders all five metrics
 * with their medians, peer counts and the P/E exclusion disclosure.
 */
describe('TKT-foundation-055 (TC-RES-025 extension) — populated strip unchanged', () => {
  it('TC-RES-025 — still renders all five metrics with their served medians', () => {
    renderWithProviders(<VsSectorStrip strip={VALUATION_FIXTURE.strip} />)

    const block = strip()
    expect(within(block).queryByTestId('vs-sector-no-data')).toBeNull()
    expect(block.querySelectorAll('[data-metric]')).toHaveLength(5)
    expect(metric(block, 'ev_ebitda')).toHaveTextContent('6,2')
  })
})
