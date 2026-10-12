import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'

import { SectionMarkers } from '@/screens/stock-page/SectionMarkers'
import {
  renderStockPage,
  resetStockPageTest,
  RESTATED_FINANCIALS_FIXTURE,
  stockFinancialsHandler,
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
