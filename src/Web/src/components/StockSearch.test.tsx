import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  MemoryRouter,
  Route,
  Routes,
  useLocation,
  useParams,
} from 'react-router'

import { StockSearch } from '@/components/StockSearch'
import { catalogValue } from '@/test/i18n'
import { stockSearchHandler } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'
import { useDraftStore } from '@/store/draftStore'

/**
 * Header stock search wiring (FR-RES-004; UXR-RES-007; TC-RES-035 classes at the
 * component level): selecting a match opens that stock's page directly; viewing
 * all matches opens the list with the query applied; a query matching nothing
 * shows an explicit no-match state. The widget itself is the shared-shell
 * component; this ticket wires its results/landing behaviour.
 */

const TR = (key: string) => catalogValue('tr', key)

function LocationProbe() {
  const location = useLocation()
  return (
    <span data-testid="location">
      {location.pathname}
      {location.search}
    </span>
  )
}

function StockPageStub() {
  const { symbol } = useParams()
  return <span data-testid="stock-page-symbol">{symbol}</span>
}

function renderSearch(initialEntries: string[] = ['/']) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <StockSearch />
      <LocationProbe />
      <Routes>
        <Route path="/stocks/:symbol" element={<StockPageStub />} />
        <Route path="/stocks" element={<span data-testid="stock-list">list</span>} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  sessionStorage.clear()
  useDraftStore.getState().resetDrafts()
  server.use(stockSearchHandler())
})

describe('header stock search (FR-RES-004)', () => {
  it('selecting a match opens that stock page directly', async () => {
    const user = userEvent.setup()
    renderSearch()

    const input = screen.getByLabelText(TR('header.searchLabel'))
    await user.type(input, 'alfa')

    const option = await screen.findByRole('option', {
      name: 'Alfa Teknoloji A.Ş. (ALFA)',
    })
    await user.click(option)

    expect(await screen.findByTestId('stock-page-symbol')).toHaveTextContent('ALFA')
  })

  it('viewing all matches opens the list with the query applied', async () => {
    const user = userEvent.setup()
    renderSearch()

    await user.type(screen.getByLabelText(TR('header.searchLabel')), 'alfa')

    const viewAll = await screen.findByRole('button', {
      name: TR('headerSearch.viewAll'),
    })
    await user.click(viewAll)

    await waitFor(() =>
      expect(screen.getByTestId('location')).toHaveTextContent('/stocks?q=alfa'),
    )
  })

  it('shows an explicit no-match state when nothing matches', async () => {
    const user = userEvent.setup()
    renderSearch()

    await user.type(screen.getByLabelText(TR('header.searchLabel')), 'zzz')

    expect(
      await screen.findByText(TR('headerSearch.noMatches')),
    ).toBeInTheDocument()
  })

  it('preserves the search term across navigation via the draft store', async () => {
    const user = userEvent.setup()
    renderSearch()

    await user.type(screen.getByLabelText(TR('header.searchLabel')), 'beta')
    await screen.findByRole('option', { name: /Beta Yazılım/ })

    expect(useDraftStore.getState().headerSearch).toBe('beta')
    // The value is store-backed, so a remount would restore it (asserted at L4).
  })
})
