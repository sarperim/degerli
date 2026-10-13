import { describe, expect, it, beforeEach } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

import { catalogValue } from '@/test/i18n'
import { resetAdminTest, renderAdmin } from '@/screens/admin/testSupport'

/**
 * TC-MDF-051 + TC-FDF-012 (L3 translations of the L4 cases) — the coverage
 * report and scope switch.
 *
 * Instrument scope shows per-instrument data types with from-when dates and the
 * listing date, with coverage gaps shown explicitly (PART's statements gap).
 * Switching to fund scope lists the funds with their data types and the
 * TEF0002 holdings gap; the scope choice is preserved across in-session
 * navigation (UXR-MDF-011/012/013, UXR-G-021).
 */

const tr = {
  sectionCoverage: catalogValue('tr', 'admin.sections.coverage'),
  scopeLabel: catalogValue('tr', 'admin.coverage.scopeLabel'),
  scopeInstrument: catalogValue('tr', 'admin.coverage.scopeInstrument'),
  scopeFund: catalogValue('tr', 'admin.coverage.scopeFund'),
  listingDate: catalogValue('tr', 'admin.coverage.listingDate'),
  present: catalogValue('tr', 'admin.coverage.present'),
  gap: catalogValue('tr', 'admin.coverage.gap'),
  statements: catalogValue('tr', 'admin.dataTypes.statements'),
  prices: catalogValue('tr', 'admin.dataTypes.prices'),
  holdings: catalogValue('tr', 'admin.dataTypes.holdings'),
  nav: catalogValue('tr', 'admin.dataTypes.nav'),
}

async function openCoverage(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: tr.sectionCoverage }))
}

describe('TC-MDF-051 / TC-FDF-012 — coverage report and scope switch', () => {
  beforeEach(() => {
    resetAdminTest()
  })

  it('shows instrument data types with from-when dates, listing dates and explicit gaps (UXR-MDF-011)', async () => {
    const user = userEvent.setup()
    renderAdmin()
    await openCoverage(user)

    const table = await screen.findByTestId('coverage-instrument-table')
    expect(within(table).getByRole('columnheader', { name: tr.listingDate })).toBeInTheDocument()

    const partRow = within(table).getByRole('row', { name: /PART/ })
    expect(within(partRow).getByText(tr.statements)).toBeInTheDocument()
    // The statements gap is shown explicitly, never omitted.
    expect(within(partRow).getByText(tr.gap)).toBeInTheDocument()
    // Present types carry their availability.
    expect(within(partRow).getByText(tr.present)).toBeInTheDocument()
  })

  it('switches to fund scope and renders the funds with their data types and gaps (TC-FDF-012)', async () => {
    const user = userEvent.setup()
    renderAdmin()
    await openCoverage(user)

    await user.click(screen.getByRole('radio', { name: tr.scopeFund }))
    const table = await screen.findByTestId('coverage-fund-table')

    for (const code of ['TEF0001', 'TEF0002', 'TEF0003']) {
      expect(within(table).getByRole('row', { name: new RegExp(code) })).toBeInTheDocument()
    }

    const tef0002 = within(table).getByRole('row', { name: /TEF0002/ })
    expect(within(tef0002).getByText(tr.holdings)).toBeInTheDocument()
    expect(within(tef0002).getByText(tr.gap)).toBeInTheDocument()

    // Fund data types use the fund vocabulary (NAV/performance/holdings).
    const tef0001 = within(table).getByRole('row', { name: /TEF0001/ })
    expect(within(tef0001).getByText(tr.nav)).toBeInTheDocument()
  })

  it('preserves the scope choice across in-session navigation (UXR-G-021)', async () => {
    const user = userEvent.setup()
    const first = renderAdmin()
    await openCoverage(user)
    await user.click(screen.getByRole('radio', { name: tr.scopeFund }))
    await screen.findByTestId('coverage-fund-table')
    first.unmount()

    renderAdmin()
    const fundRadio = await screen.findByRole('radio', { name: tr.scopeFund })
    expect(fundRadio).toBeChecked()
    expect(await screen.findByTestId('coverage-fund-table')).toBeInTheDocument()
  })
})
