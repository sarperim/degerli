import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Link, MemoryRouter, Route, Routes, useParams } from 'react-router'

import { StockListPage } from '@/routes/StockListPage'
import { catalogValue } from '@/test/i18n'
import { stockListHandler } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'
import { useDraftStore } from '@/store/draftStore'

/**
 * TC-RES-022 — Stock list component (L3).
 *
 * The un-fragmented expected result from `.pipeline/testing/stock-research.md`:
 * full list renders; sector filter + search apply together and show as active;
 * no-match → explicit no-matching-stock state with a clear path; a dashboard
 * sector link prefills the filter (UXR-RES-006); filter/search/active state is
 * preserved across navigation (UXR-G-021); rows activate the stock page.
 */

const TR = (key: string) => catalogValue('tr', key)

function StockPageStub() {
  const { symbol } = useParams()
  return (
    <div data-testid="stock-page">
      <span data-testid="stock-page-symbol">{symbol}</span>
      <Link to="/stocks">back-to-list</Link>
    </div>
  )
}

function renderStockList(initialEntries: string[] = ['/stocks']) {
  return renderWithProviders(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/stocks" element={<StockListPage />} />
        <Route path="/stocks/:symbol" element={<StockPageStub />} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  sessionStorage.clear()
  useDraftStore.getState().resetDrafts()
  server.use(stockListHandler())
})

describe('TC-RES-022 stock list component', () => {
  it('renders the full universe with name, code and sector', async () => {
    renderStockList()

    const alfa = await screen.findByTestId('stock-row-ALFA')
    expect(within(alfa).getByText('Alfa Teknoloji A.Ş.')).toBeInTheDocument()
    expect(within(alfa).getByText('ALFA')).toBeInTheDocument()
    expect(within(alfa).getByText('A')).toBeInTheDocument()

    // All 16 covered stocks, including the explicitly-unclassified UNSEC.
    for (const symbol of [
      'BETA', 'GAMA', 'DELTA', 'REST', 'EPSL', 'ZETA', 'ETA', 'THETA',
      'PART', 'NEWP', 'IOTA', 'KAPPA', 'LAMDA', 'NU', 'UNSEC',
    ]) {
      expect(screen.getByTestId(`stock-row-${symbol}`)).toBeInTheDocument()
    }

    const unsec = screen.getByTestId('stock-row-UNSEC')
    expect(within(unsec).getByText(TR('stockList.unclassified'))).toBeInTheDocument()
  })

  it('applies sector filter and search together and shows both as active', async () => {
    const user = userEvent.setup()
    renderStockList()
    await screen.findByTestId('stock-row-ALFA')

    // Sector A alone narrows to the five SEC-A stocks.
    await user.selectOptions(
      screen.getByLabelText(TR('stockList.sectorLabel')),
      'A',
    )
    await waitFor(() =>
      expect(screen.queryByTestId('stock-row-ZETA')).not.toBeInTheDocument(),
    )
    expect(screen.getByTestId('stock-row-BETA')).toBeInTheDocument()

    // Adding a search term applies conjunctively within the sector.
    await user.type(
      screen.getByLabelText(TR('stockList.searchLabel')),
      'beta',
    )
    await waitFor(() =>
      expect(screen.queryByTestId('stock-row-GAMA')).not.toBeInTheDocument(),
    )
    expect(screen.getByTestId('stock-row-BETA')).toBeInTheDocument()

    const active = screen.getByTestId('active-filters')
    expect(within(active).getByTestId('active-sector')).toHaveTextContent('A')
    expect(within(active).getByTestId('active-search')).toHaveTextContent('beta')
  })

  it('combines sector and search conjunctively (sector restricts the search)', async () => {
    const user = userEvent.setup()
    renderStockList()
    await screen.findByTestId('stock-row-ALFA')

    // "alfa" alone matches ALFA (sector A); combined with sector B it matches nothing.
    await user.selectOptions(
      screen.getByLabelText(TR('stockList.sectorLabel')),
      'B',
    )
    await user.type(
      screen.getByLabelText(TR('stockList.searchLabel')),
      'alfa',
    )

    expect(await screen.findByTestId('no-match-state')).toBeInTheDocument()
  })

  it('shows an explicit no-match state with a path to clear the query', async () => {
    const user = userEvent.setup()
    renderStockList()
    await screen.findByTestId('stock-row-ALFA')

    await user.type(screen.getByLabelText(TR('stockList.searchLabel')), 'zzz')

    const empty = await screen.findByTestId('no-match-state')
    expect(within(empty).getByText(TR('stockList.emptyTitle'))).toBeInTheDocument()

    await user.click(
      within(empty).getByRole('button', { name: TR('stockList.clearFilters') }),
    )

    expect(await screen.findByTestId('stock-row-ALFA')).toBeInTheDocument()
    expect(screen.getByLabelText(TR('stockList.searchLabel'))).toHaveValue('')
  })

  it('prefills the sector filter from a dashboard sector link via URL params', async () => {
    renderStockList(['/stocks?sector=A'])

    await screen.findByTestId('stock-row-ALFA')

    expect(screen.getByLabelText(TR('stockList.sectorLabel'))).toHaveValue('A')
    expect(screen.getByTestId('active-sector')).toHaveTextContent('A')
    // SEC-B is not in the filtered SEC-A result.
    expect(screen.queryByTestId('stock-row-ZETA')).not.toBeInTheDocument()
  })

  it('a sector link does not inherit a stale stored search term', async () => {
    // The store already holds a search from a previous interaction on the list.
    useDraftStore.getState().setStockListFilters({ sector: 'A', query: 'beta' })

    renderStockList(['/stocks?sector=A'])

    // GAMA only appears once the stale "beta" search has been cleared by the link.
    expect(await screen.findByTestId('stock-row-GAMA')).toBeInTheDocument()
    expect(screen.getByLabelText(TR('stockList.searchLabel'))).toHaveValue('')
    expect(screen.queryByTestId('active-search')).not.toBeInTheDocument()
  })

  it('preserves filter and search across navigation to a stock page and back', async () => {
    const user = userEvent.setup()
    renderStockList()
    await screen.findByTestId('stock-row-ALFA')

    await user.selectOptions(
      screen.getByLabelText(TR('stockList.sectorLabel')),
      'A',
    )
    await user.type(
      screen.getByLabelText(TR('stockList.searchLabel')),
      'beta',
    )

    const betaLink = await screen.findByRole('link', {
      name: 'Beta Yazılım A.Ş. (BETA)',
    })
    await user.click(betaLink)

    expect(await screen.findByTestId('stock-page-symbol')).toHaveTextContent(
      'BETA',
    )

    await user.click(screen.getByRole('link', { name: 'back-to-list' }))

    await screen.findByTestId('stock-row-BETA')
    expect(screen.getByLabelText(TR('stockList.searchLabel'))).toHaveValue('beta')
    expect(screen.getByLabelText(TR('stockList.sectorLabel'))).toHaveValue('A')
    expect(screen.getByTestId('active-sector')).toHaveTextContent('A')
  })

  it('activates the stock page when a row is selected', async () => {
    const user = userEvent.setup()
    renderStockList()
    await screen.findByTestId('stock-row-ALFA')

    await user.click(
      screen.getByRole('link', { name: 'Alfa Teknoloji A.Ş. (ALFA)' }),
    )

    expect(await screen.findByTestId('stock-page-symbol')).toHaveTextContent(
      'ALFA',
    )
  })
})
