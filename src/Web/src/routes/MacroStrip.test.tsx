import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { MacroStrip } from '@/routes/MacroStrip'
import { catalogValue } from '@/test/i18n'
import {
  API_BASE,
  MACRO_STRIP_DEGRADED_FIXTURE,
  MACRO_STRIP_FIXTURE,
  MACRO_STRIP_STALE_FIXTURE,
  marketMacroHandler,
  problemHandler,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'

/**
 * TC-MOV-016 (macro-strip half, L3 portion), TC-MOV-017 (L3 portion), TC-MOV-018
 * and TC-MOV-024 from `.pipeline/testing/market-overview.md` §2 Group D.
 *
 * TC-MOV-016 (macro half): the strip displays all six indicators (TÜİK CPI, the
 * independent measure, CBRT repo, USD/TRY, EUR/TRY, gold), each with its value,
 * unit, plain-language label, source attribution and as-of date (UXR-MOV-007).
 * TC-MOV-017: official and independent inflation measures are always rendered as
 * a pair, both visible.
 * TC-MOV-018: when the independent measure is unavailable the official value is
 * shown with an explicit note and the unavailable label is retained (no value,
 * never hidden, never blank-as-zero) (UXR-MOV-008/009).
 * TC-MOV-024: a stale series renders the stale badge together with its as-of
 * date; a fresh series renders the as-of date only (UXR-MOV-010/UXR-G-008).
 */

const tr = (key: string) => catalogValue('tr', key)

const SERIES_CODES = [
  'TUIK_CPI',
  'INDEP_CPI',
  'CBRT_REPO',
  'USD_TRY',
  'EUR_TRY',
  'GOLD',
] as const

function renderStrip() {
  return renderWithProviders(<MacroStrip />)
}

beforeEach(() => {
  server.use(marketMacroHandler())
})

describe('TC-MOV-016 macro strip renders six indicators with value, unit, source and as-of', () => {
  it('renders every indicator with its plain-language label, value, unit, source and as-of', async () => {
    server.use(marketMacroHandler(MACRO_STRIP_FIXTURE))
    renderStrip()

    await screen.findByTestId('market-block-macro')

    for (const code of SERIES_CODES) {
      await screen.findByTestId(`macro-${code}`)
      expect(screen.getByTestId(`macro-${code}`)).toHaveTextContent(
        tr(`marketOverview.macro.series.${code}`),
      )
      expect(screen.getByTestId(`macro-value-${code}`)).not.toBeEmptyDOMElement()
      expect(screen.getByTestId(`macro-unit-${code}`)).not.toBeEmptyDOMElement()
      expect(screen.getByTestId(`macro-source-${code}`)).not.toBeEmptyDOMElement()
      expect(
        within(screen.getByTestId(`macro-${code}`)).getByText(/2026/),
      ).toBeInTheDocument()
    }

    // Source attribution carries the plain-language label (UXR-MOV-007/NFR-MOV-004).
    expect(screen.getByTestId('macro-source-TUIK_CPI')).toHaveTextContent(
      tr('marketOverview.macro.source').replace('{{source}}', 'TÜİK'),
    )
  })
})

describe('TC-MOV-017 official and independent inflation measures are always paired', () => {
  it('renders the official and independent measures together inside the pair', async () => {
    server.use(marketMacroHandler(MACRO_STRIP_FIXTURE))
    renderStrip()

    const pair = await screen.findByTestId('macro-inflation-pair')
    expect(within(pair).getByTestId('macro-TUIK_CPI')).toBeInTheDocument()
    expect(within(pair).getByTestId('macro-INDEP_CPI')).toBeInTheDocument()
  })
})

describe('TC-MOV-018 degraded macro — unavailable independent measure', () => {
  it('keeps the official value, retains the unavailable label and notes the degradation', async () => {
    server.use(marketMacroHandler(MACRO_STRIP_DEGRADED_FIXTURE))
    renderStrip()

    const official = await screen.findByTestId('macro-TUIK_CPI')
    expect(within(official).getByTestId('macro-value-TUIK_CPI')).toBeInTheDocument()

    // The independent label is retained, its value is absent and its state explicit.
    const independent = screen.getByTestId('macro-INDEP_CPI')
    expect(independent).toHaveTextContent(tr('marketOverview.macro.series.INDEP_CPI'))
    expect(within(independent).queryByTestId('macro-value-INDEP_CPI')).toBeNull()
    expect(independent).toHaveTextContent(tr('honestData.unavailableTitle'))

    // The pair is never silently reduced to one unlabeled figure.
    expect(screen.getByTestId('macro-independent-unavailable')).toHaveTextContent(
      tr('marketOverview.macro.independentUnavailable'),
    )
  })
})

describe('TC-MOV-024 stale marker component', () => {
  it('renders a stale badge together with the as-of date for a stale series', async () => {
    server.use(marketMacroHandler(MACRO_STRIP_STALE_FIXTURE))
    renderStrip()

    const gold = await screen.findByTestId('macro-GOLD')
    expect(gold.querySelector('[data-slot="stale-badge"]')).not.toBeNull()
    expect(gold.querySelector('[data-slot="as-of-date"]')).not.toBeNull()
  })

  it('renders the as-of date only, with no badge, for a fresh series', async () => {
    server.use(marketMacroHandler(MACRO_STRIP_FIXTURE))
    renderStrip()

    const usd = await screen.findByTestId('macro-USD_TRY')
    expect(usd.querySelector('[data-slot="as-of-date"]')).not.toBeNull()
    expect(usd.querySelector('[data-slot="stale-badge"]')).toBeNull()
  })
})

describe('macro strip failure domain', () => {
  it('renders an independent plain-language error with retry, recovering when restored', async () => {
    const user = userEvent.setup()
    server.use(problemHandler(`${API_BASE}/market/macro`, 'INTERNAL'))

    renderStrip()

    const strip = await screen.findByTestId('market-block-macro')
    const alert = await within(strip).findByRole('alert')
    expect(alert).toHaveTextContent(tr('marketOverview.errorTitle'))

    server.use(marketMacroHandler(MACRO_STRIP_FIXTURE))
    await user.click(within(strip).getByRole('button', { name: tr('common.retry') }))

    await screen.findByTestId('macro-TUIK_CPI')
    expect(within(strip).queryByRole('alert')).toBeNull()
  })
})
