import { describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'

import {
  AdjustedNotice,
  AsOfDate,
  NotMeaningful,
  NoDataState,
  RestatedMarker,
  RestatedNotice,
  StaleBadge,
} from '@/components/honest-data'
import { formatAsOfDate } from '@/lib/format'
import { runA11y } from '@/test/a11y'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'

const AS_OF = '2026-10-09'

/**
 * Honest-data marker vocabulary (TKT-foundation-011 acceptance).
 *
 * Copy is read from the checked-in catalogs (never hardcoded prose, test
 * strategy §8.3). Every marker must carry its meaning as text — not colour or
 * icon alone (UXR-G-009).
 */
describe('honest-data markers', () => {
  it('renders the as-of date in the active language (UXR-G-007)', () => {
    renderWithProviders(<AsOfDate asOf={AS_OF} />)

    const expected = catalogValue('tr', 'honestData.asOf').replace(
      '{{date}}',
      formatAsOfDate('tr', AS_OF),
    )
    expect(screen.getByText(expected)).toBeInTheDocument()
  })

  it('renders the as-of date in English when English is active', () => {
    renderWithProviders(<AsOfDate asOf={AS_OF} />, { language: 'en' })

    const expected = catalogValue('en', 'honestData.asOf').replace(
      '{{date}}',
      formatAsOfDate('en', AS_OF),
    )
    expect(screen.getByText(expected)).toBeInTheDocument()
  })

  it('marks stale data with a textual badge (UXR-G-008)', () => {
    renderWithProviders(<StaleBadge />)
    expect(
      screen.getByText(catalogValue('tr', 'honestData.stale')),
    ).toBeInTheDocument()
  })

  it('renders an explicit not-meaningful state, not a value (UXR-RES-025)', () => {
    renderWithProviders(<NotMeaningful />)
    const node = screen.getByText(catalogValue('tr', 'honestData.notMeaningful'))
    expect(node).toBeInTheDocument()
    expect(node).toHaveAttribute(
      'title',
      catalogValue('tr', 'honestData.notMeaningfulTitle'),
    )
  })

  it('carries the restatement asterisk as accessible text (UXR-G-009)', () => {
    renderWithProviders(
      <p>
        P/E <RestatedMarker /> 12.4
      </p>,
    )
    // The visible glyph is an asterisk…
    expect(screen.getByText('*')).toBeInTheDocument()
    // …and the meaning is exposed as text to assistive tech.
    expect(
      screen.getByText(`(${catalogValue('tr', 'honestData.restated')})`),
    ).toBeInTheDocument()
  })

  it('renders a restatement footnote and an adjusted-data disclaimer', () => {
    renderWithProviders(
      <>
        <RestatedNotice />
        <AdjustedNotice />
      </>,
    )
    expect(
      screen.getByText(catalogValue('tr', 'honestData.restatedNotice')),
    ).toBeInTheDocument()
    expect(
      screen.getByText(catalogValue('tr', 'honestData.adjustedNotice')),
    ).toBeInTheDocument()
  })

  it.each(['no-data', 'preparing', 'unavailable'] as const)(
    'renders the %s state with its own title',
    (state) => {
      const keyByState = {
        'no-data': 'honestData.noDataTitle',
        preparing: 'honestData.preparingTitle',
        unavailable: 'honestData.unavailableTitle',
      } as const
      renderWithProviders(<NoDataState state={state} />)
      expect(
        screen.getByText(catalogValue('tr', keyByState[state])),
      ).toBeInTheDocument()
    },
  )

  it('shows the coverage boundary for missing data (UXR-G-006)', () => {
    renderWithProviders(<NoDataState availableFrom="2019-01-02" />)
    const expected = catalogValue('tr', 'honestData.coverageBoundary').replace(
      '{{date}}',
      formatAsOfDate('tr', '2019-01-02'),
    )
    expect(screen.getByText(new RegExp(expected))).toBeInTheDocument()
  })

  it('has no detectable accessibility violations across the markers', async () => {
    const { container } = renderWithProviders(
      <>
        <AsOfDate asOf={AS_OF} />
        <StaleBadge />
        <NotMeaningful />
        <RestatedNotice />
        <AdjustedNotice />
        <NoDataState state="no-data" availableFrom="2019-01-02" />
      </>,
    )
    await expect(runA11y(container)).resolves.toEqual([])
  })
})
