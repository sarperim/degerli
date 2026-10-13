import { describe, expect, it, beforeEach } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'

import { Header } from '@/components/Header'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'
import { server } from '@/test/msw/server'
import {
  ANONYMOUS_SESSION_FIXTURE,
  API_BASE,
  BUILDER_SESSION_FIXTURE,
  VERIFIED_SESSION_FIXTURE,
  problemHandler,
  sessionHandler,
} from '@/test/msw/handlers'
import { resetAdminTest, renderAdmin } from '@/screens/admin/testSupport'

/**
 * TC-MDF-045 (L3 translation of the L4 case) — admin entry visibility and the
 * access-denied state.
 *
 * Traces UXR-MDF-001/002 (SCR-012 §4): the global navigation presents the admin
 * entry only to a builder account, and a direct `/admin` visit by a non-builder
 * (regular account or anonymous) presents the plain-language denied state with a
 * path back to platform content — never a blank page or a raw error.
 */

const tr = {
  navAdmin: catalogValue('tr', 'nav.admin'),
  primaryNav: catalogValue('tr', 'header.primaryNav'),
  deniedTitle: catalogValue('tr', 'admin.accessDenied.title'),
  deniedBody: catalogValue('tr', 'admin.accessDenied.body'),
  deniedBack: catalogValue('tr', 'admin.accessDenied.back'),
  sectionsNavLabel: catalogValue('tr', 'admin.sectionsNavLabel'),
  summary: catalogValue('tr', 'admin.sections.summary'),
  sessionErrorTitle: catalogValue('tr', 'screens.admin.sessionErrorTitle'),
  sessionErrorBody: catalogValue('tr', 'screens.admin.sessionErrorBody'),
  retry: catalogValue('tr', 'common.retry'),
}

function renderHeader() {
  return renderWithProviders(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route path="/" element={<Header />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('TC-MDF-045 — admin entry visibility and access-denied state', () => {
  beforeEach(() => {
    resetAdminTest()
  })

  it('presents the admin nav entry only to the builder role (UXR-MDF-001)', async () => {
    server.use(sessionHandler(BUILDER_SESSION_FIXTURE))
    renderHeader()
    const nav = await screen.findByRole('navigation', { name: tr.primaryNav })
    expect(
      await within(nav).findByRole('link', { name: tr.navAdmin }),
    ).toBeInTheDocument()
  })

  it('omits the admin nav entry for a regular account and for anonymous visitors (UXR-MDF-001)', async () => {
    server.use(sessionHandler(VERIFIED_SESSION_FIXTURE))
    const { unmount } = renderHeader()
    const nav = await screen.findByRole('navigation', { name: tr.primaryNav })
    await waitFor(() =>
      expect(within(nav).queryByRole('link', { name: tr.navAdmin })).toBeNull(),
    )
    unmount()

    server.use(sessionHandler(ANONYMOUS_SESSION_FIXTURE))
    renderHeader()
    const anonNav = await screen.findByRole('navigation', { name: tr.primaryNav })
    await waitFor(() =>
      expect(within(anonNav).queryByRole('link', { name: tr.navAdmin })).toBeNull(),
    )
  })

  it('renders the admin sections for a builder account (UXR-MDF-001)', async () => {
    renderAdmin()
    expect(
      await screen.findByRole('navigation', { name: tr.sectionsNavLabel }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: tr.summary })).toBeInTheDocument()
  })

  it('renders a plain-language denied state with a path back for a regular account (UXR-MDF-002)', async () => {
    server.use(sessionHandler(VERIFIED_SESSION_FIXTURE))
    renderAdmin()

    expect(
      await screen.findByRole('heading', { name: tr.deniedTitle }),
    ).toBeInTheDocument()
    expect(screen.getByText(tr.deniedBody)).toBeInTheDocument()
    expect(
      screen.queryByRole('navigation', { name: tr.sectionsNavLabel }),
    ).toBeNull()

    const back = screen.getByRole('link', { name: tr.deniedBack })
    expect(back).toHaveAttribute('href', '/')
    const user = userEvent.setup()
    await user.click(back)
    expect(await screen.findByTestId('home')).toBeInTheDocument()
  })

  it('renders the denied state for an anonymous visitor (UXR-MDF-002)', async () => {
    server.use(sessionHandler(ANONYMOUS_SESSION_FIXTURE))
    renderAdmin()
    expect(
      await screen.findByRole('heading', { name: tr.deniedTitle }),
    ).toBeInTheDocument()
  })

  it('shows a session error with retry, never access-denied, when the session fetch fails (UXR-G-003)', async () => {
    server.use(problemHandler(`${API_BASE}/auth/session`, 'INTERNAL'))
    renderAdmin()

    expect(await screen.findByText(tr.sessionErrorTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.sessionErrorBody)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: tr.retry })).toBeInTheDocument()
    // A failed session read must not masquerade as access-denied.
    expect(screen.queryByRole('heading', { name: tr.deniedTitle })).toBeNull()
  })

  it('shows the aggregate-only stats panel with no per-user surface (UXR-MDF-026)', async () => {
    const user = userEvent.setup()
    renderAdmin()
    await screen.findByRole('navigation', { name: tr.sectionsNavLabel })
    await user.click(
      screen.getByRole('button', { name: catalogValue('tr', 'admin.sections.stats') }),
    )

    expect(
      await screen.findByText(catalogValue('tr', 'admin.stats.registered')),
    ).toBeInTheDocument()
    expect(
      screen.getByText(catalogValue('tr', 'admin.stats.aggregateNote')),
    ).toBeInTheDocument()
    expect(
      screen.getByText(catalogValue('tr', 'admin.stats.dcfScenarios')),
    ).toBeInTheDocument()
  })
})
