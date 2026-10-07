import type { ReactElement, ReactNode } from 'react'
import { render, type RenderOptions, type RenderResult } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { I18nextProvider } from 'react-i18next'
import type { i18n as I18n } from 'i18next'

import {
  DEFAULT_LANGUAGE,
  type SupportedLanguage,
} from '@/i18n'
import { createTestI18n } from '@/test/i18n'
import { createTestQueryClient } from '@/test/query/testQueryClient'

export interface RenderWithProvidersOptions
  extends Omit<RenderOptions, 'wrapper'> {
  /** Query client to use; a fresh retry-less client is created by default. */
  queryClient?: ReturnType<typeof createTestQueryClient>
  /** Language for the isolated i18n instance. TR is the default. */
  language?: SupportedLanguage
  /** Pre-built i18n instance, when a test needs to drive the language. */
  i18n?: I18n
}

export interface RenderWithProvidersResult extends RenderResult {
  queryClient: ReturnType<typeof createTestQueryClient>
  i18n: I18n
}

/**
 * TanStack Query + i18n test wrapper (test strategy §5.3).
 *
 * Every render gets its own query client and i18n instance, so tests are
 * order-independent and never share cached data or language state.
 */
export function renderWithProviders(
  ui: ReactElement,
  options: RenderWithProvidersOptions = {},
): RenderWithProvidersResult {
  const {
    queryClient = createTestQueryClient(),
    language = DEFAULT_LANGUAGE,
    i18n = createTestI18n(language),
    ...renderOptions
  } = options

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <I18nextProvider i18n={i18n}>
        <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
      </I18nextProvider>
    )
  }

  return {
    ...render(ui, { wrapper: Wrapper, ...renderOptions }),
    queryClient,
    i18n,
  }
}
