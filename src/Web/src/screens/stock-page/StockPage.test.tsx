import { beforeEach, describe, expect, it } from 'vitest'
import { act, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'

import { formatAsOfDate } from '@/lib/format'
import { catalogValue } from '@/test/i18n'
import { problemHandler } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import {
  fill,
  GROWTH_FIXTURE,
  PREPARING_OVERVIEW,
  PUBLISHED_OVERVIEW,
  renderStockPage,
  resetStockPageTest,
  stockFinancialsHandler,
  stockOverviewHandler,
} from '@/screens/stock-page/testSupport'

/**
 * SCR-005 Stock Page component tests (TKT-res-008).
 *
 * TC-RES-023 — Section independence and per-section retry (L3).
 * TC-RES-026 — Description rendering and preparing state (L3).
 * TC-RES-027 — Active-section preservation (L3).
 *
 * Expected results are the un-fragmented cases from
 * `.pipeline/testing/stock-research.md`; copy is read from the catalogs (§8.3).
 */

const TR = (key: string) => catalogValue('tr', key)

function region(name: string): HTMLElement {
  return screen.getByRole('region', { name })
}

beforeEach(resetStockPageTest)

describe('TC-RES-023 — section independence and per-section retry', () => {
  it('shows one failing section’s own error while the other six render', async () => {
    server.use(problemHandler('/api/v1/stocks/:symbol/financials', 'INTERNAL'))
    renderStockPage()

    // The screen mounted and the overview rendered.
    expect(await screen.findByText(PUBLISHED_OVERVIEW.textTr!)).toBeInTheDocument()

    // The failing section owns its plain-language error and retry…
    const financials = region(TR('stockPage.sections.financials'))
    expect(await within(financials).findByRole('alert')).toBeInTheDocument()
    expect(
      within(financials).getByText(TR('stockPage.sectionErrorTitle')),
    ).toBeInTheDocument()
    expect(
      within(financials).getByText(TR('stockPage.sectionErrorBody')),
    ).toBeInTheDocument()
    expect(
      within(financials).getByRole('button', { name: TR('common.retry') }),
    ).toBeInTheDocument()

    // …while the other six sections render normally.
    expect(
      within(region(TR('stockPage.sections.overview'))).getByText(
        PUBLISHED_OVERVIEW.textTr!,
      ),
    ).toBeInTheDocument()
    expect(
      within(region(TR('stockPage.sections.valuation'))).getByText(
        TR('stockPage.valuationPlaceholder'),
      ),
    ).toBeInTheDocument()
    expect(
      within(region(TR('stockPage.sections.profitability'))).getByText(
        TR('stockPage.profitability.metrics.roic'),
      ),
    ).toBeInTheDocument()
    expect(
      within(region(TR('stockPage.sections.growth'))).getByText(
        TR('stockPage.growth.families.rev'),
      ),
    ).toBeInTheDocument()
    expect(
      within(region(TR('stockPage.sections.balanceSheet'))).getByText(
        TR('stockPage.balanceSheet.bookValue'),
      ),
    ).toBeInTheDocument()
    expect(
      within(region(TR('stockPage.sections.dividends'))).getByText(
        TR('stockPage.dividends.columns.exDate'),
      ),
    ).toBeInTheDocument()
  })

  it('recovers a failed section on retry once the handler is restored', async () => {
    const user = userEvent.setup()
    server.use(problemHandler('/api/v1/stocks/:symbol/financials', 'INTERNAL'))
    renderStockPage()

    const financials = region(TR('stockPage.sections.financials'))
    await within(financials).findByRole('alert')

    server.use(stockFinancialsHandler())
    await user.click(
      within(financials).getByRole('button', { name: TR('common.retry') }),
    )

    expect(
      await within(financials).findByText(
        TR('stockPage.financials.columns.revenue'),
      ),
    ).toBeInTheDocument()
    expect(within(financials).queryByRole('alert')).not.toBeInTheDocument()
  })

  it('loads sections with independent loading states', async () => {
    // Growth is slow; every other section resolves immediately.
    server.use(
      http.get('/api/v1/stocks/:symbol/growth', async () => {
        await delay(120)
        return HttpResponse.json(GROWTH_FIXTURE)
      }),
    )
    renderStockPage()

    await screen.findByText(PUBLISHED_OVERVIEW.textTr!)

    const growth = region(TR('stockPage.sections.growth'))
    expect(within(growth).getByRole('status')).toBeInTheDocument()

    const balanceSheet = region(TR('stockPage.sections.balanceSheet'))
    expect(within(balanceSheet).queryByRole('status')).not.toBeInTheDocument()
    expect(
      within(balanceSheet).getByText(TR('stockPage.balanceSheet.bookValue')),
    ).toBeInTheDocument()

    expect(
      await within(growth).findByText(TR('stockPage.growth.families.rev')),
    ).toBeInTheDocument()
  })
})

describe('TC-RES-026 — description rendering and preparing state', () => {
  it('renders the published description in the selected language with its last-reviewed date', async () => {
    let overviewCalls = 0
    server.use(
      http.get('/api/v1/stocks/:symbol/overview', () => {
        overviewCalls += 1
        return HttpResponse.json(PUBLISHED_OVERVIEW)
      }),
    )

    const { i18n } = renderStockPage()

    expect(await screen.findByText(PUBLISHED_OVERVIEW.textTr!)).toBeInTheDocument()
    expect(
      screen.getByText(
        fill(TR('stockPage.descriptionLastReviewed'), {
          date: formatAsOfDate('tr', PUBLISHED_OVERVIEW.lastReviewedAt!),
        }),
      ),
    ).toBeInTheDocument()

    // Switching language re-renders both pre-served texts without a refetch.
    await act(async () => {
      await i18n.changeLanguage('en')
    })
    expect(await screen.findByText(PUBLISHED_OVERVIEW.textEn!)).toBeInTheDocument()
    expect(screen.queryByText(PUBLISHED_OVERVIEW.textTr!)).not.toBeInTheDocument()
    expect(overviewCalls).toBe(1)
  })

  it('shows the preparing state while the numbers sections stay fully populated', async () => {
    server.use(stockOverviewHandler(PREPARING_OVERVIEW))
    renderStockPage()

    const overview = await screen.findByRole('region', {
      name: TR('stockPage.sections.overview'),
    })
    expect(
      await within(overview).findByText(TR('honestData.preparingTitle')),
    ).toBeInTheDocument()

    expect(
      await within(region(TR('stockPage.sections.financials'))).findByText(
        TR('stockPage.financials.columns.revenue'),
      ),
    ).toBeInTheDocument()
    expect(
      within(region(TR('stockPage.sections.profitability'))).getByText(
        TR('stockPage.profitability.metrics.roic'),
      ),
    ).toBeInTheDocument()
    expect(
      within(region(TR('stockPage.sections.balanceSheet'))).getByText(
        TR('stockPage.balanceSheet.bookValue'),
      ),
    ).toBeInTheDocument()
  })
})

describe('TC-RES-027 — active-section preservation', () => {
  it('preserves the active section across navigation away and back', async () => {
    const user = userEvent.setup()
    renderStockPage()
    await screen.findByText(PUBLISHED_OVERVIEW.textTr!)

    await user.click(
      screen.getByRole('button', {
        name: TR('stockPage.sections.balanceSheet'),
      }),
    )
    expect(
      screen.getByRole('button', {
        name: TR('stockPage.sections.balanceSheet'),
      }),
    ).toHaveAttribute('aria-current', 'true')

    // Navigate away via the DCF hand-off, then return.
    await user.click(
      screen.getByRole('link', { name: TR('stockPage.dcfHandoff') }),
    )
    expect(await screen.findByTestId('dcf-stub')).toBeInTheDocument()
    await user.click(screen.getByRole('link', { name: 'back-to-stock' }))

    await screen.findByText(PUBLISHED_OVERVIEW.textTr!)
    expect(
      screen.getByRole('button', {
        name: TR('stockPage.sections.balanceSheet'),
      }),
    ).toHaveAttribute('aria-current', 'true')
  })
})
