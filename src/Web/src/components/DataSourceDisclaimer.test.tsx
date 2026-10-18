import { beforeEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { createMemoryRouter, RouterProvider } from 'react-router'

import { AppShell } from '@/components/AppShell'
import {
  DataSourceDisclaimer,
  type DataSourceCatalogResponse,
} from '@/components/DataSourceDisclaimer'
import { runA11y } from '@/test/a11y'
import { catalogValue } from '@/test/i18n'
import { renderWithProviders } from '@/test/render'
import { server } from '@/test/msw/server'

/**
 * TKT-foundation-029 — the site-wide data-source provenance disclosure
 * ("where the data comes from"). Explicit acceptance criteria (no TC covers a
 * provenance disclosure; the informational-only TC-XC-006 sweep covers only the
 * `Disclaimer` component):
 *
 * (i)  the block renders in the shell footer, TR by default and EN by toggle;
 *      each class line shows its source + "as of" nature;
 * (ii) a class served `state: unavailable` renders the honest unavailable marker
 *      and never claims a live source — a dev/fixture-only world is never the
 *      displayed state of a real class;
 * (iii) the existing informational-only `Disclaimer` is unaffected: this is a
 *      separate data-slot, a single global instance, and NOT a second
 *      `complementary` landmark (landmark-is-unique);
 * (iv) i18n parity is green with the new tr/en key block (`i18nParity.test.ts`).
 */

const API = '/api/v1'

/** The catalog as `GET /api/v1/meta/data-sources` serves it (mirrors the server). */
const CATALOG_FIXTURE: DataSourceCatalogResponse = {
  sources: [
    { id: 'isyatirim-prices', class: 'prices', source: 'İş Yatırım', state: 'live', nature: 'eod' },
    { id: 'isyatirim-index-levels', class: 'indexLevels', source: 'İş Yatırım', state: 'live', nature: 'eod' },
    { id: 'isyatirim-universe', class: 'universe', source: 'İş Yatırım', state: 'live', nature: 'eod' },
    { id: 'kap-statements', class: 'statements', source: 'MKK KAP', state: 'unavailable', nature: 'eod' },
    { id: 'evds-macro', class: 'macro', source: 'TCMB EVDS', state: 'unavailable', nature: 'eod' },
  ],
}

const LIVE_CLASSES = ['prices', 'indexLevels', 'universe'] as const
const UNAVAILABLE_CLASSES = ['statements', 'macro'] as const

function dataSourcesHandler(body: DataSourceCatalogResponse = CATALOG_FIXTURE) {
  return http.get(`${API}/meta/data-sources`, () => HttpResponse.json(body))
}

const tr = (key: string) => catalogValue('tr', key)
const en = (key: string) => catalogValue('en', key)

beforeEach(() => {
  server.use(dataSourcesHandler())
})

describe('DataSourceDisclaimer renders the per-class provenance catalog (i)', () => {
  it('renders the block with every class line showing its source and as-of nature (TR default)', async () => {
    renderWithProviders(<DataSourceDisclaimer />)

    const block = await screen.findByTestId('data-source-disclaimer')
    expect(block).toHaveTextContent(tr('dataSources.label'))

    for (const entry of CATALOG_FIXTURE.sources) {
      const row = screen.getByTestId(`data-source-${entry.class}`)
      expect(row).toHaveTextContent(tr(`dataSources.classes.${entry.class}`))
      expect(row).toHaveTextContent(entry.source)
      expect(screen.getByTestId(`data-source-nature-${entry.class}`)).toHaveTextContent(
        tr('dataSources.nature.eod'),
      )
    }
  })

  it('renders the English copy when English is active', async () => {
    renderWithProviders(<DataSourceDisclaimer />, { language: 'en' })

    const block = await screen.findByTestId('data-source-disclaimer')
    expect(block).toHaveTextContent(en('dataSources.label'))
    expect(screen.getByTestId('data-source-prices')).toHaveTextContent(
      en('dataSources.classes.prices'),
    )
  })
})

describe('DataSourceDisclaimer honest state markers (ii)', () => {
  it('marks an unavailable class honestly and never claims a live source', async () => {
    renderWithProviders(<DataSourceDisclaimer />)
    await screen.findByTestId('data-source-disclaimer')

    for (const cls of UNAVAILABLE_CLASSES) {
      const state = screen.getByTestId(`data-source-state-${cls}`)
      expect(state).toHaveTextContent(tr('dataSources.state.unavailable'))
      expect(state).not.toHaveTextContent(tr('dataSources.state.live'))
    }
  })

  it('never presents the dev/fixture-only caveat as the displayed state of a live class', async () => {
    renderWithProviders(<DataSourceDisclaimer />)
    await screen.findByTestId('data-source-disclaimer')

    // The caveat is a standalone disclosure (a sibling of the class list), so it
    // can never be a real class's displayed state; assert that directly rather
    // than checking whether the class <li> "contains" a sibling <p>.
    const caveat = screen.getByTestId('data-source-fixture-caveat')
    expect(caveat).toHaveTextContent(tr('dataSources.fixtureCaveat'))

    for (const cls of LIVE_CLASSES) {
      expect(screen.getByTestId(`data-source-state-${cls}`)).toHaveTextContent(
        tr('dataSources.state.live'),
      )
      expect(screen.getByTestId(`data-source-state-${cls}`)).not.toHaveTextContent(
        tr('dataSources.state.unavailable'),
      )
      expect(screen.getByTestId(`data-source-state-${cls}`)).not.toBe(caveat)
    }
  })
})

describe('DataSourceDisclaimer shell placement and a11y (iii)', () => {
  function renderShell(path: string) {
    const router = createMemoryRouter(
      [
        {
          path: '/',
          element: <AppShell />,
          children: [
            { index: true, element: <div>home</div>, handle: { research: false } },
            { path: 'stocks', element: <div>stocks</div>, handle: { research: true } },
          ],
        },
      ],
      { initialEntries: [path] },
    )
    return renderWithProviders(<RouterProvider router={router} />)
  }

  it('renders site-wide in the shell footer, on a non-research route too', async () => {
    renderShell('/')

    await screen.findByTestId('data-source-disclaimer')
    // The informational-only Disclaimer stays research-only; the provenance
    // disclosure is global (a separate data-slot, not a `complementary` landmark).
    expect(screen.queryByRole('complementary')).toBeNull()
  })

  it('coexists with the informational Disclaimer without a duplicate landmark', async () => {
    renderShell('/stocks')

    await screen.findByTestId('data-source-disclaimer')
    expect(await screen.findByRole('complementary')).toBeInTheDocument()
    expect(screen.getAllByRole('complementary')).toHaveLength(1)
  })

  it('has no detectable accessibility violations', async () => {
    const { container } = renderWithProviders(<DataSourceDisclaimer />)
    await screen.findByTestId('data-source-disclaimer')

    await expect(runA11y(container)).resolves.toEqual([])
  })
})
