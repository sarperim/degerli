import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import { catalogValue } from '@/test/i18n'
import { API_BASE, problemHandler, saveScreenHandler } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { fill, renderScreener, resetScreenerTest } from '@/screens/screener/testSupport'

/**
 * TC-SCR-024 — Save-flow states (L3).
 *
 * Traces UXR-SCR-008/009/019, UXR-G-029/030. Decision table (MSW-driven):
 * anonymous → sign-in/register prompt (no error); unverified → verify-your-e-mail
 * message with path; duplicate → inline collision error naming the screen, nothing
 * lost; pending → trigger locked; server 500 → plain-language error + retry with
 * input preserved; success → confirmation names the screen.
 */

const TR = (key: string) => catalogValue('tr', key)

async function buildOneCriterion(
  user: ReturnType<typeof userEvent.setup>,
  name = 'Ucuz kaliteli',
) {
  await user.selectOptions(screen.getByTestId('metric-select'), 'pe')
  await user.click(screen.getByTestId('add-criterion'))
  const rows = screen.getAllByTestId('criterion-row')
  const row = rows[rows.length - 1]
  await user.selectOptions(within(row).getByTestId('criterion-bound'), 'max')
  await user.type(within(row).getByTestId('criterion-max'), '15')
  await user.type(screen.getByTestId('save-name'), name)
}

beforeEach(resetScreenerTest)

describe('TC-SCR-024 — save-flow states', () => {
  it('prompts anonymous users to sign in or register without showing an error', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/screens`, 'UNAUTHENTICATED', { method: 'post' }),
    )

    renderScreener()
    await screen.findByTestId('metric-select')
    await buildOneCriterion(user)
    await user.click(screen.getByTestId('save-button'))

    const prompt = await screen.findByTestId('save-anonymous')
    expect(prompt).toHaveTextContent(TR('screener.anonymousTitle'))
    expect(within(prompt).getByRole('link', { name: TR('screener.signIn') })).toHaveAttribute(
      'href',
      '/sign-in?returnUrl=%2Fscreener',
    )
    expect(within(prompt).getByRole('link', { name: TR('screener.register') })).toHaveAttribute(
      'href',
      '/register?returnUrl=%2Fscreener',
    )
    // A prompt, not an error; nothing lost.
    expect(screen.queryByTestId('save-error')).not.toBeInTheDocument()
    expect(screen.getByTestId('save-name')).toHaveValue('Ucuz kaliteli')
    expect(screen.getAllByTestId('criterion-row')).toHaveLength(1)
  })

  it('blocks an unverified account with a verify-your-e-mail message and path', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/screens`, 'EMAIL_NOT_VERIFIED', { method: 'post' }),
    )

    renderScreener()
    await screen.findByTestId('metric-select')
    await buildOneCriterion(user)
    await user.click(screen.getByTestId('save-button'))

    const gate = await screen.findByTestId('save-unverified')
    expect(gate).toHaveTextContent(TR('screener.unverifiedTitle'))
    expect(gate).toHaveTextContent(TR('screener.unverifiedBody'))
    expect(within(gate).getByRole('link', { name: TR('screener.unverifiedLink') })).toHaveAttribute(
      'href',
      '/settings',
    )
    // Criteria and name preserved.
    expect(screen.getByTestId('save-name')).toHaveValue('Ucuz kaliteli')
    expect(screen.getAllByTestId('criterion-row')).toHaveLength(1)
  })

  it('shows an inline duplicate-name error naming the screen, losing nothing', async () => {
    const user = userEvent.setup()
    server.use(
      problemHandler(`${API_BASE}/me/screens`, 'DUPLICATE_NAME', {
        method: 'post',
        params: { name: 'Ucuz kaliteli' },
      }),
    )

    renderScreener()
    await screen.findByTestId('metric-select')
    await buildOneCriterion(user)
    await user.click(screen.getByTestId('save-button'))

    const error = await screen.findByTestId('save-duplicate')
    expect(error).toHaveTextContent(
      fill(TR('screener.duplicateName'), { name: 'Ucuz kaliteli' }),
    )
    expect(screen.getByTestId('save-name')).toHaveValue('Ucuz kaliteli')
    expect(screen.getAllByTestId('criterion-row')).toHaveLength(1)
  })

  it('locks the save trigger while the mutation is pending', async () => {
    const user = userEvent.setup()
    let release!: () => void
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })
    server.use(
      http.post(`${API_BASE}/me/screens`, async () => {
        await gate
        return HttpResponse.json(
          { id: 1, name: 'Ucuz kaliteli', criteria: [], createdAt: '', updatedAt: '' },
          { status: 201 },
        )
      }),
    )

    renderScreener()
    await screen.findByTestId('metric-select')
    await buildOneCriterion(user)
    await user.click(screen.getByTestId('save-button'))

    const pending = screen.getByRole('button', { name: TR('screener.saving') })
    expect(pending).toBeDisabled()
    expect(screen.getByTestId('save-name')).toBeDisabled()

    release()
    await waitFor(() =>
      expect(
        screen.queryByRole('button', { name: TR('screener.saving') }),
      ).not.toBeInTheDocument(),
    )
  })

  it('renders a plain-language error with retry on a server failure, preserving input', async () => {
    const user = userEvent.setup()
    server.use(problemHandler(`${API_BASE}/me/screens`, 'INTERNAL', { method: 'post' }))

    renderScreener()
    await screen.findByTestId('metric-select')
    await buildOneCriterion(user)
    await user.click(screen.getByTestId('save-button'))

    const error = await screen.findByTestId('save-error')
    expect(error).toHaveTextContent(TR('screener.saveErrorTitle'))
    expect(error).toHaveTextContent(TR('screener.saveErrorBody'))
    expect(within(error).getByRole('button', { name: TR('common.retry') })).toBeInTheDocument()
    expect(screen.getByTestId('save-name')).toHaveValue('Ucuz kaliteli')
    expect(screen.getAllByTestId('criterion-row')).toHaveLength(1)
  })

  it('confirms success by naming the saved screen', async () => {
    const user = userEvent.setup()
    server.use(saveScreenHandler())

    renderScreener()
    await screen.findByTestId('metric-select')
    await buildOneCriterion(user)
    await user.click(screen.getByTestId('save-button'))

    const confirmation = await screen.findByTestId('save-confirmation')
    expect(confirmation).toHaveTextContent(
      fill(TR('screener.savedConfirmation'), { name: 'Ucuz kaliteli' }),
    )
  })
})
