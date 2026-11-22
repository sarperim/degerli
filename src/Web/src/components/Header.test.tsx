import { describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'

import { Header } from '@/components/Header'
import { runA11y } from '@/test/a11y'
import { catalogValue } from '@/test/i18n'
import {
  BUILDER_SESSION_FIXTURE,
  sessionHandler,
  VERIFIED_SESSION_FIXTURE,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { renderWithProviders } from '@/test/render'

/**
 * Global header responsive contract (FR-ACC-002/003; UXR-ACC-010).
 *
 * The anonymous header keeps the narrow `max-w-content` single-row layout. The
 * signed-in header (6 links, or 7 for a builder) widens to `max-w-[1600px]` and
 * only forces its single desktop row at 1400px, so all navigation links plus the
 * utility bar — including the always-visible account e-mail from `sm` upwards —
 * fit without being squeezed onto a cramped second line.
 */

const TR = (key: string) => catalogValue('tr', key)

function renderHeader() {
  return renderWithProviders(
    <MemoryRouter initialEntries={['/']}>
      <Header />
    </MemoryRouter>,
  )
}

function getHeaderRow(container: HTMLElement): HTMLElement {
  const row = container.querySelector('header > div')
  if (!(row instanceof HTMLElement)) throw new Error('Header inner row not found')
  return row
}

describe('Header — anonymous layout', () => {
  it('renders the three public links in the narrow single-row container', () => {
    const { container } = renderHeader()

    const row = getHeaderRow(container)
    expect(row).toHaveClass('max-w-content')
    expect(row).toHaveClass('lg:flex-nowrap')

    const nav = screen.getByRole('navigation', { name: TR('header.primaryNav') })
    expect(nav).toHaveClass('lg:flex-nowrap')
    // The anonymous nav keeps its pre-PR layout: no hairline separator row.
    expect(nav).not.toHaveClass('border-t')

    const links = within(nav).getAllByRole('link')
    expect(links).toHaveLength(3)
    expect(links.map((link) => link.textContent)).toEqual([
      TR('nav.marketOverview'),
      TR('nav.stocks'),
      TR('nav.screener'),
    ])
    expect(screen.queryByTestId('auth-email')).not.toBeInTheDocument()
  })

  it('has no detectable accessibility violations', async () => {
    const { container } = renderHeader()

    await expect(runA11y(container)).resolves.toEqual([])
  })
})

describe('Header — signed-in layout', () => {
  it('fits the six nav links in the widened single-row container', async () => {
    server.use(sessionHandler(VERIFIED_SESSION_FIXTURE))
    const { container } = renderHeader()

    await screen.findByTestId('auth-email')

    const row = getHeaderRow(container)
    expect(row).toHaveClass('max-w-[1600px]')
    expect(row).toHaveClass('min-[1400px]:flex-nowrap')
    expect(row).not.toHaveClass('max-w-content')

    const nav = screen.getByRole('navigation', { name: TR('header.primaryNav') })
    expect(nav).toHaveClass('min-[1400px]:flex-nowrap')
    // Below 1400px the nav sits on its own full-width, hairline-separated row.
    expect(nav).toHaveClass('order-last')
    expect(nav).toHaveClass('w-full')
    expect(nav).toHaveClass('border-t')
    expect(nav).toHaveClass('min-[1400px]:border-t-0')

    const links = within(nav).getAllByRole('link')
    expect(links).toHaveLength(6)
    expect(links.map((link) => link.textContent)).toEqual([
      TR('nav.marketOverview'),
      TR('nav.stocks'),
      TR('nav.screener'),
      TR('nav.savedScreens'),
      TR('nav.dcfScenarios'),
      TR('nav.settings'),
    ])

    const email = screen.getByTestId('auth-email')
    expect(email).toHaveAttribute('title', VERIFIED_SESSION_FIXTURE.email)
    expect(email).toHaveTextContent(VERIFIED_SESSION_FIXTURE.email)
    // The identity stays visible from sm upwards (no B2 regression), bounded.
    expect(email).toHaveClass('sm:inline')
    expect(email).toHaveClass('max-w-[9rem]')
    expect(email).toHaveClass('2xl:max-w-[11.5rem]')
  })

  it('has no detectable accessibility violations', async () => {
    server.use(sessionHandler(VERIFIED_SESSION_FIXTURE))
    const { container } = renderHeader()

    await screen.findByTestId('auth-email')

    await expect(runA11y(container)).resolves.toEqual([])
  })
})

describe('Header — builder layout', () => {
  it('fits all seven nav links including Admin in the single-row nav', async () => {
    server.use(sessionHandler(BUILDER_SESSION_FIXTURE))
    renderHeader()

    await screen.findByTestId('auth-email')

    const nav = screen.getByRole('navigation', { name: TR('header.primaryNav') })
    expect(nav).toHaveClass('min-[1400px]:flex-nowrap')

    const links = within(nav).getAllByRole('link')
    expect(links).toHaveLength(7)
    expect(links.at(-1)).toHaveTextContent(TR('nav.admin'))
  })
})
