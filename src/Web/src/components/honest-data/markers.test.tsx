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

  /**
   * TKT-foundation-039 (TC-MOV-024, finding B3): the badge must explain its own
   * semantics — a truthful stale flag that reads as "this number is wrong" is a
   * comprehension defect. The explanation is catalog copy and is programmatically
   * determinable (title + accessible name), never colour- or hover-only.
   */
  it('explains what stale means instead of leaving the flag unexplained', () => {
    renderWithProviders(<StaleBadge />)

    const badge = screen.getByText(catalogValue('tr', 'honestData.stale'))
    const explanation = catalogValue('tr', 'honestData.staleExplanation')

    expect(badge).toHaveAttribute('title', explanation)
    expect(badge).toHaveAccessibleName(
      `${catalogValue('tr', 'honestData.stale')} — ${explanation}`,
    )
    // A bare label is not an explanation: the two must not collapse into one string.
    expect(explanation).not.toBe(catalogValue('tr', 'honestData.stale'))
  })

  it('explains the stale badge in English when English is active', () => {
    renderWithProviders(<StaleBadge />, { language: 'en' })

    const badge = screen.getByText(catalogValue('en', 'honestData.stale'))
    expect(badge).toHaveAttribute(
      'title',
      catalogValue('en', 'honestData.staleExplanation'),
    )
  })

  /**
   * TKT-foundation-073 (TC-RES-024): the as-of date is itself a claim, so it
   * explains what it means on every surface — the same doctrine 039 applied to
   * the stale badge. The neutral sentence is true in every state ("the point in
   * time the data describes"); the recency sentence is only rendered when the
   * caller has established that this row IS the latest publication, because on a
   * stale row that sentence would be false (039 review round 1, DR-1).
   */
  describe('TC-RES-024 as-of marker explains itself', () => {
    const asOfSlot = (text: string) =>
      screen.getByText(text).closest('[data-slot="as-of-date"]')

    function asOfText(language: 'tr' | 'en') {
      return catalogValue(language, 'honestData.asOf').replace(
        '{{date}}',
        formatAsOfDate(language, AS_OF),
      )
    }

    it('carries the catalog explanation in Turkish and English', () => {
      for (const language of ['tr', 'en'] as const) {
        const { unmount } = renderWithProviders(<AsOfDate asOf={AS_OF} />, {
          language,
        })
        try {
          const marker = asOfSlot(asOfText(language))
          expect(marker).not.toBeNull()
          expect(marker).toHaveAttribute(
            'title',
            catalogValue(language, 'honestData.asOfExplanation'),
          )
          // Accessible as text, not only as a hover tooltip (UXR-G-009 spirit).
          expect(marker).toHaveTextContent(
            catalogValue(language, 'honestData.asOfExplanation'),
          )
        } finally {
          unmount()
        }
      }
    })

    it('adds the recency sentence only when the caller establishes freshness', () => {
      renderWithProviders(<AsOfDate asOf={AS_OF} fresh />)

      const marker = asOfSlot(asOfText('tr'))
      const neutral = catalogValue('tr', 'honestData.asOfExplanation')
      const recency = catalogValue('tr', 'honestData.asOfRecencyExplanation')

      expect(marker).toHaveAttribute('title', `${neutral} ${recency}`)
      // A bare recency claim is not an explanation: the two are distinct strings.
      expect(recency).not.toBe(neutral)
    })

    it('makes no recency claim when freshness is not established', () => {
      renderWithProviders(<AsOfDate asOf={AS_OF} />)

      const marker = asOfSlot(asOfText('tr'))
      expect(marker).not.toBeNull()
      expect(marker).not.toHaveTextContent(
        catalogValue('tr', 'honestData.asOfRecencyExplanation'),
      )
      // Unknown is not fresh: the neutral explanation still stands.
      expect(marker).toHaveTextContent(
        catalogValue('tr', 'honestData.asOfExplanation'),
      )
    })

    it('makes no recency claim in English either when freshness is not established', () => {
      renderWithProviders(<AsOfDate asOf={AS_OF} />, { language: 'en' })

      const marker = asOfSlot(asOfText('en'))
      expect(marker).not.toHaveTextContent(
        catalogValue('en', 'honestData.asOfRecencyExplanation'),
      )
      expect(marker).toHaveTextContent(
        catalogValue('en', 'honestData.asOfExplanation'),
      )
    })
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
