import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'

import { SectionMarkers } from '@/screens/stock-page/SectionMarkers'
import {
  renderStockPage,
  resetStockPageTest,
  RESTATED_FINANCIALS_FIXTURE,
  stockDividendsHandler,
  stockFinancialsHandler,
  ZERO_DIVIDENDS_FIXTURE,
} from '@/screens/stock-page/testSupport'
import { server } from '@/test/msw/server'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'
import { formatAsOfDate } from '@/lib/format'
import { fill } from '@/screens/stock-page/testSupport'

/**
 * TC-RES-024 — Honest-data markers (L3, decision table — flag × marker rendering).
 *
 * The un-fragmented expected result from `.pipeline/testing/stock-research.md`:
 * `restated: true` → asterisk on affected figures + bottom-of-display footnote +
 * warning (all textual, reachable); `adjusted: true` → adjusted-data disclaimer;
 * `stale: true` → badge + asOf; missing data → no-data state with coverage
 * boundary; none set → clean render with asOf only.
 */

const TR = (key: string) => catalogValue('tr', key)
const AS_OF = '2026-10-06'

beforeEach(resetStockPageTest)

describe('TC-RES-024 honest-data markers', () => {
  it('renders cleanly with the as-of date when no flag is set', () => {
    renderWithProviders(<SectionMarkers asOf={AS_OF} />)

    expect(
      screen.getByText(
        fill(TR('honestData.asOf'), { date: formatAsOfDate('tr', AS_OF) }),
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText(TR('honestData.stale'))).not.toBeInTheDocument()
    expect(
      screen.queryByText(TR('honestData.restatedNotice')),
    ).not.toBeInTheDocument()
    expect(
      screen.queryByText(TR('honestData.adjustedNotice')),
    ).not.toBeInTheDocument()
  })

  it('marks stale data with a badge alongside its as-of date', () => {
    renderWithProviders(<SectionMarkers asOf={AS_OF} stale />)

    expect(screen.getByText(TR('honestData.stale'))).toBeInTheDocument()
    expect(
      screen.getByText(
        fill(TR('honestData.asOf'), { date: formatAsOfDate('tr', AS_OF) }),
      ),
    ).toBeInTheDocument()
  })

  it('shows the restatement footnote and warning when a section has restated figures', () => {
    const { container } = renderWithProviders(
      <SectionMarkers asOf={AS_OF} restated />,
    )

    expect(
      screen.getByText(TR('honestData.restatedNotice')),
    ).toBeInTheDocument()
    // The footnote carries the warning indicator and is textual (UXR-G-009).
    expect(
      container.querySelector('[data-slot="restated-notice"]'),
    ).toBeInTheDocument()
  })

  it('shows the adjusted-data disclaimer when a series is corporate-action-adjusted', () => {
    renderWithProviders(<SectionMarkers asOf={AS_OF} adjusted />)

    expect(
      screen.getByText(TR('honestData.adjustedNotice')),
    ).toBeInTheDocument()
  })

  it('shows the explicit no-data state with the coverage boundary', () => {
    const { container } = renderWithProviders(
      <SectionMarkers
        asOf={AS_OF}
        state="no-data"
        coverage={{ availableFrom: '2019-01-02', boundaryNote: null }}
      />,
    )

    expect(screen.getByText(TR('honestData.noDataTitle'))).toBeInTheDocument()
    expect(
      screen.getByText(
        new RegExp(
          fill(TR('honestData.coverageBoundary'), {
            date: formatAsOfDate('tr', '2019-01-02'),
          }),
        ),
      ),
    ).toBeInTheDocument()
    expect(
      container.querySelector('[data-slot="as-of-date"]'),
    ).not.toBeInTheDocument()
  })

  /**
   * TKT-foundation-073 (TC-RES-024): a stock-page section marker never renders a
   * bare date or a bare "Bayat" — every marker explains itself in the active
   * language. The as-of marker keeps to what the section can prove: the neutral
   * "this is the point in time the data describes" sentence. `SectionMarkers` does
   * not know whether a section is the latest publication, so it makes no recency
   * claim (no claim beats a false one — 039 DR-2 doctrine, extended here).
   */
  describe('TC-RES-024 section markers explain themselves', () => {
    const asOfSlot = (text: string) =>
      screen.getByText(text).closest('[data-slot="as-of-date"]')

    for (const language of ['tr', 'en'] as const) {
      it(`explains a fresh section's as-of date in ${language}`, () => {
        const asOf = catalogValue(language, 'honestData.asOf').replace(
          '{{date}}',
          formatAsOfDate(language, AS_OF),
        )
        renderWithProviders(<SectionMarkers asOf={AS_OF} />, { language })

        const marker = asOfSlot(asOf)
        expect(marker).toHaveAttribute(
          'title',
          catalogValue(language, 'honestData.asOfExplanation'),
        )
        expect(marker).not.toHaveTextContent(
          catalogValue(language, 'honestData.asOfRecencyExplanation'),
        )
      })

      it(`keeps the explanation and the stale meaning in ${language}`, () => {
        const asOf = catalogValue(language, 'honestData.asOf').replace(
          '{{date}}',
          formatAsOfDate(language, AS_OF),
        )
        const { container } = renderWithProviders(
          <SectionMarkers asOf={AS_OF} stale />,
          { language },
        )

        const badge = container.querySelector('[data-slot="stale-badge"]')
        expect(badge).not.toBeNull()
        expect(badge).toHaveAttribute(
          'title',
          catalogValue(language, 'honestData.staleExplanation'),
        )
        expect(badge).toHaveAccessibleName(
          `${catalogValue(language, 'honestData.stale')} — ${catalogValue(language, 'honestData.staleExplanation')}`,
        )

        // The as-of marker still explains itself.
        const marker = asOfSlot(asOf)
        expect(marker).toHaveAttribute(
          'title',
          catalogValue(language, 'honestData.asOfExplanation'),
        )
        // No recency assertion belongs here yet: `SectionMarkers` does not thread
        // `stale` into `AsOfDate` (it never passes `fresh`), so a recency check
        // would pass identically fresh or stale — vacuous, not a suppression test
        // (review round 1, DR-5). Threading freshness through is a recorded
        // follow-up, outside TKT-foundation-073's pinned behaviour.
      })
    }

    it('renders no as-of marker on the stock page without its explanation', async () => {
      renderStockPage()

      const financials = await screen.findByRole('region', {
        name: TR('stockPage.sections.financials'),
      })
      await within(financials).findByText(
        TR('stockPage.financials.columns.revenue'),
      )

      const markers = financials.querySelectorAll('[data-slot="as-of-date"]')
      expect(markers.length).toBeGreaterThan(0)
      for (const marker of markers) {
        expect(marker).toHaveAttribute(
          'title',
          TR('honestData.asOfExplanation'),
        )
      }
    })

    /**
     * The dividends "never paid" state is an absence claim, so it states the period
     * it is made over — the coverage boundary alone ("available from …") does not
     * say that an older payment would not have been seen.
     */
    it('bounds the dividends never-paid claim with its coverage period', async () => {
      server.use(stockDividendsHandler(ZERO_DIVIDENDS_FIXTURE))
      renderStockPage(['/stocks/NEWP'])

      const dividends = await screen.findByRole('region', {
        name: TR('stockPage.sections.dividends'),
      })
      await within(dividends).findByText(
        TR('stockPage.dividends.neverPaidTitle'),
      )

      const state = dividends.querySelector('[data-testid="dividends-never-paid"]')
      expect(state).not.toBeNull()
      expect(state).toHaveTextContent(TR('stockPage.dividends.neverPaidBody'))
      expect(state).toHaveTextContent(TR('stockPage.dividends.neverPaidCoverage'))
      expect(state).toHaveTextContent(
        fill(TR('honestData.coverageBoundary'), {
          date: formatAsOfDate('tr', '2016-04-12'),
        }),
      )
    })
  })

  it('places the restatement asterisk on the affected figures of a section', async () => {
    server.use(stockFinancialsHandler(RESTATED_FINANCIALS_FIXTURE))
    renderStockPage()

    const financials = await screen.findByRole('region', {
      name: TR('stockPage.sections.financials'),
    })
    await within(financials).findByText(TR('stockPage.financials.columns.revenue'))

    // The affected figure carries the asterisk…
    expect(
      financials.querySelector('[data-slot="restated-marker"]'),
    ).toBeInTheDocument()
    // …and the bottom-of-display footnote explains it.
    expect(
      within(financials).getByText(TR('honestData.restatedNotice')),
    ).toBeInTheDocument()
  })
})
