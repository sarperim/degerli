import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'

import { server } from '@/test/msw/server'
import { catalogValue } from '@/test/i18n'
import { problemResponse, API_BASE } from '@/test/msw/handlers'
import type {
  AdminDescription,
  AdminSummaryResponse,
  DescriptionListResponse,
} from '@/lib/api/contracts'
import { resetAdminTest, renderAdmin } from '@/screens/admin/testSupport'

/**
 * TC-RES-034 (L3 translation of the L4 case) — SCR-012 §5 description review and
 * publication (`stock-research.md` TC-RES-034; UXR-MDF-018..022, FR-RES-020/021,
 * UC-RES-004 steps 2–4, M-2 coverage refresh).
 *
 * The FU §9b descriptions: ALFA published (with one unresolvable source ref),
 * REST draft with both texts, ZETA draft with an empty EN (the publish-gate
 * case). The suite drives queue filtering, the bilingual editor with KAP source
 * refs, save-preserves-on-failure, the disabled publish control, the
 * public-visibility confirmation and the coverage-count refresh (without a
 * manual refresh).
 */

const tr = {
  sectionDescriptions: catalogValue('tr', 'admin.sections.descriptions'),
  filterLabel: catalogValue('tr', 'admin.descriptions.filterLabel'),
  statusDraft: catalogValue('tr', 'admin.descriptions.statusDraft'),
  statusPublished: catalogValue('tr', 'admin.descriptions.statusPublished'),
  emptyTitle: catalogValue('tr', 'admin.descriptions.emptyTitle'),
  emptyBody: catalogValue('tr', 'admin.descriptions.emptyBody'),
  textTrLabel: catalogValue('tr', 'admin.descriptions.textTrLabel'),
  textEnLabel: catalogValue('tr', 'admin.descriptions.textEnLabel'),
  sourceRefsHeading: catalogValue('tr', 'admin.descriptions.sourceRefsHeading'),
  edit: catalogValue('tr', 'admin.descriptions.edit'),
  save: catalogValue('tr', 'admin.descriptions.save'),
  saved: catalogValue('tr', 'admin.descriptions.saved'),
  saveError: catalogValue('tr', 'admin.descriptions.saveError'),
  publish: catalogValue('tr', 'admin.descriptions.publish'),
  publishDisabledReason: catalogValue('tr', 'admin.descriptions.publishDisabledReason'),
  publishTitle: catalogValue('tr', 'admin.descriptions.publishTitle'),
  publishConfirmAction: catalogValue('tr', 'admin.descriptions.publishConfirmAction'),
  sourceRefStub: catalogValue('tr', 'admin.descriptions.sourceRefStub'),
  publishedNotice: catalogValue('tr', 'admin.descriptions.publishedNotice'),
  summary: catalogValue('tr', 'admin.sections.summary'),
  publishedCount: catalogValue('tr', 'admin.summary.descriptionsPublished'),
  draftCount: catalogValue('tr', 'admin.summary.descriptionsDraft'),
}

const REST_TEXT_TR = 'Rest Telekom, sabit ve mobil telekomünikasyon hizmetleri sunar.'
const REST_TEXT_EN = 'Rest Telekom provides fixed and mobile telecommunications services.'
const ZETA_TEXT_TR = 'Zeta İmalat, endüstriyel imalat yapar.'

const REST_REF_TITLE = 'Rest Telekom Finansal Rapor'

/** FU §9b descriptions: one published (with an unresolvable ref), two drafts. */
const DESCRIPTION_FIXTURE: AdminDescription[] = [
  {
    id: 412,
    symbol: 'ALFA',
    name: 'Alfa Teknoloji A.Ş.',
    version: 2,
    status: 'published',
    textTr: 'Alfa Teknoloji, haberleşme ve savunma elektronikleri geliştirir ve üretir.',
    textEn: 'Alfa Teknoloji develops and manufactures communications and defence electronics.',
    sourceRefs: [
      {
        disclosureId: 8841,
        disclosureType: 'annual_report',
        publishDate: '2026-03-14',
        title: '2025 Faaliyet Raporu',
        sourceUrl: 'https://www.kap.org.tr/tr/Bildirim/8841',
      },
      // An id that no longer resolves → a {disclosureId}-only stub, never dropped.
      { disclosureId: 9999 },
    ],
    lastReviewedAt: '2026-09-10T09:00:00Z',
    publishedAt: '2026-09-10T09:00:00Z',
  },
  {
    id: 413,
    symbol: 'REST',
    name: 'Rest Telekom A.Ş.',
    version: 1,
    status: 'draft',
    textTr: REST_TEXT_TR,
    textEn: REST_TEXT_EN,
    sourceRefs: [
      {
        disclosureId: 8842,
        disclosureType: 'financial_report',
        publishDate: '2025-12-10',
        title: REST_REF_TITLE,
        sourceUrl: 'https://www.kap.org.tr/tr/Bildirim/8842',
      },
    ],
    lastReviewedAt: null,
    publishedAt: null,
  },
  {
    id: 414,
    symbol: 'ZETA',
    name: 'Zeta İmalat A.Ş.',
    version: 1,
    status: 'draft',
    textTr: ZETA_TEXT_TR,
    textEn: '',
    sourceRefs: [
      {
        disclosureId: 8843,
        disclosureType: 'financial_report',
        publishDate: '2026-06-16',
        title: 'Zeta İmalat Finansal Rapor',
        sourceUrl: 'https://www.kap.org.tr/tr/Bildirim/8843',
      },
    ],
    lastReviewedAt: null,
    publishedAt: null,
  },
]

/**
 * Stateful descriptions handlers: the list honours `?status=`; a PATCH rewrites
 * both texts (unless a forced failure is armed); publish runs the publication
 * gate (both languages required) and flips the status. The exposes counts feed
 * the summary handler so a publish is observable on the coverage counts (M-2).
 */
function descriptionsHandlers() {
  let descriptions = DESCRIPTION_FIXTURE.map((row) => ({ ...row }))
  let failNextPatch = false

  const list = http.get(`${API_BASE}/admin/descriptions`, ({ request }) => {
    const status = new URL(request.url).searchParams.get('status')
    const body: DescriptionListResponse = {
      descriptions: status ? descriptions.filter((row) => row.status === status) : descriptions,
    }
    return HttpResponse.json(body)
  })

  const patch = http.patch(
    `${API_BASE}/admin/descriptions/:id`,
    async ({ request, params }) => {
      if (failNextPatch) {
        failNextPatch = false
        return problemResponse('INTERNAL')
      }
      const body = (await request.json()) as { textTr?: string; textEn?: string }
      const id = Number(params.id)
      descriptions = descriptions.map((row) =>
        row.id === id
          ? {
              ...row,
              textTr: body.textTr ?? row.textTr,
              textEn: body.textEn ?? row.textEn,
            }
          : row,
      )
      return HttpResponse.json(descriptions.find((row) => row.id === id))
    },
  )

  const publish = http.post(
    `${API_BASE}/admin/descriptions/:id/publish`,
    ({ params }) => {
      const id = Number(params.id)
      const row = descriptions.find((candidate) => candidate.id === id)
      if (!row) return problemResponse('NOT_FOUND')
      if (!row.textTr?.trim() || !row.textEn?.trim()) {
        return problemResponse('VALIDATION_FAILED')
      }
      const published: AdminDescription = {
        ...row,
        status: 'published',
        lastReviewedAt: '2026-10-06T09:00:00Z',
        publishedAt: '2026-10-06T09:00:00Z',
      }
      descriptions = descriptions.map((candidate) => (candidate.id === id ? published : candidate))
      return HttpResponse.json(published)
    },
  )

  const counts = () => ({
    descriptionsPublished: descriptions.filter((row) => row.status === 'published').length,
    descriptionsDraft: descriptions.filter((row) => row.status === 'draft').length,
  })

  return {
    handlers: [list, patch, publish],
    failNextPatch: () => {
      failNextPatch = true
    },
    counts,
  }
}

let descriptions = descriptionsHandlers()
let summaryCalls = 0

function summaryHandler() {
  return http.get(`${API_BASE}/admin/summary`, () => {
    summaryCalls += 1
    const { descriptionsPublished, descriptionsDraft } = descriptions.counts()
    const body: AdminSummaryResponse = {
      asOf: '2026-10-06',
      jobs: [],
      freshness: [],
      contentCoverage: { descriptionsPublished, descriptionsDraft, universe: 100 },
      openQuarantineCount: 0,
    }
    return HttpResponse.json(body)
  })
}

function resetDescriptionsTest() {
  resetAdminTest()
  descriptions = descriptionsHandlers()
  summaryCalls = 0
  server.use(...descriptions.handlers, summaryHandler())
}

async function openDescriptions(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: tr.sectionDescriptions }))
  await screen.findByTestId('descriptions-table')
}

async function editRow(user: ReturnType<typeof userEvent.setup>, symbol: RegExp) {
  const table = await screen.findByTestId('descriptions-table')
  const row = await within(table).findByRole('row', { name: symbol })
  return user.click(within(row).getByRole('button', { name: tr.edit }))
}

describe('TC-RES-034 — description review, editing and publication', () => {
  beforeEach(() => {
    resetDescriptionsTest()
  })

  it('lists the queue filterable by status, each row showing stock and status (UXR-MDF-018)', async () => {
    const user = userEvent.setup()
    renderAdmin()
    await openDescriptions(user)

    const table = screen.getByTestId('descriptions-table')
    // Default filter is draft: the two drafts are listed with their status.
    expect(within(table).getByRole('row', { name: /REST/ })).toBeInTheDocument()
    expect(within(table).getByRole('row', { name: /ZETA/ })).toBeInTheDocument()
    expect(within(table).queryByRole('row', { name: /ALFA/ })).toBeNull()
    expect(within(table).getAllByText(tr.statusDraft).length).toBeGreaterThan(0)

    // Published filter surfaces the published row instead.
    await user.selectOptions(screen.getByLabelText(tr.filterLabel), 'published')
    expect(
      await within(screen.getByTestId('descriptions-table')).findByRole('row', {
        name: /ALFA/,
      }),
    ).toBeInTheDocument()
    expect(within(screen.getByTestId('descriptions-table')).getAllByText(tr.statusPublished).length).toBeGreaterThan(0)
  })

  it('names the empty state when the selected status has no descriptions (UXR-MDF-027)', async () => {
    const user = userEvent.setup()
    renderAdmin()
    await openDescriptions(user)

    await user.selectOptions(screen.getByLabelText(tr.filterLabel), 'reviewed')
    expect(await screen.findByText(tr.emptyTitle)).toBeInTheDocument()
    expect(screen.getByText(tr.emptyBody)).toBeInTheDocument()
  })

  it('opens the bilingual editor with the KAP source refs alongside, including an unresolved stub (UXR-MDF-019/020)', async () => {
    const user = userEvent.setup()
    renderAdmin()
    await openDescriptions(user)

    await editRow(user, /REST/)

    const editor = await screen.findByTestId('description-editor')
    expect(within(editor).getByLabelText(tr.textTrLabel)).toHaveValue(REST_TEXT_TR)
    expect(within(editor).getByLabelText(tr.textEnLabel)).toHaveValue(REST_TEXT_EN)

    const refs = within(editor).getByTestId('description-source-refs')
    expect(within(refs).getByText(REST_REF_TITLE)).toBeInTheDocument()
    expect(within(refs).getByText(/https:\/\/www\.kap\.org\.tr/)).toBeInTheDocument()

    // ALFA's unresolvable id is served as a stub, never silently dropped.
    await user.selectOptions(screen.getByLabelText(tr.filterLabel), 'published')
    await editRow(user, /ALFA/)
    const alfaEditor = await screen.findByTestId('description-editor')
    const alfaRefs = within(alfaEditor).getByTestId('description-source-refs')
    expect(
      within(alfaRefs).getByText(tr.sourceRefStub.replace('{{id}}', '9999')),
    ).toBeInTheDocument()
  })

  it('saves both languages and preserves both texts exactly when the save fails (UXR-MDF-019, UXR-G-004)', async () => {
    const user = userEvent.setup()
    renderAdmin()
    await openDescriptions(user)
    await editRow(user, /REST/)

    const editor = await screen.findByTestId('description-editor')
    const trField = within(editor).getByLabelText(tr.textTrLabel)
    const enField = within(editor).getByLabelText(tr.textEnLabel)
    await user.clear(trField)
    await user.type(trField, 'Yeni Türkçe metin')
    await user.clear(enField)
    await user.type(enField, 'New English text')

    descriptions.failNextPatch()
    await user.click(within(editor).getByRole('button', { name: tr.save }))

    // Plain-language error, and both texts preserved exactly as entered.
    expect(await within(editor).findByText(tr.saveError)).toBeInTheDocument()
    expect(trField).toHaveValue('Yeni Türkçe metin')
    expect(enField).toHaveValue('New English text')

    // A retry succeeds and the editor keeps showing the saved texts.
    await user.click(within(editor).getByRole('button', { name: tr.save }))
    expect(await within(editor).findByText(tr.saved)).toBeInTheDocument()
    expect(trField).toHaveValue('Yeni Türkçe metin')
    expect(enField).toHaveValue('New English text')
  })

  it('disables publish while a language is empty, with the explanation (UXR-MDF-021)', async () => {
    const user = userEvent.setup()
    renderAdmin()
    await openDescriptions(user)
    await editRow(user, /ZETA/)

    const editor = await screen.findByTestId('description-editor')
    expect(within(editor).getByRole('button', { name: tr.publish })).toBeDisabled()
    expect(within(editor).getByText(tr.publishDisabledReason)).toBeInTheDocument()
  })

  it('publishes only after a confirmation stating public visibility, and the coverage counts update without a manual refresh (UXR-MDF-022, M-2)', async () => {
    const user = userEvent.setup()
    renderAdmin()
    await openDescriptions(user)
    await editRow(user, /REST/)

    const editor = await screen.findByTestId('description-editor')
    const publishButton = within(editor).getByRole('button', { name: tr.publish })
    expect(publishButton).toBeEnabled()
    await user.click(publishButton)

    // The confirmation states the consequence (public visibility).
    const dialog = await screen.findByRole('dialog', { name: tr.publishTitle })
    expect(within(dialog).getByText(new RegExp('REST'))).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: tr.publishConfirmAction }))

    // The editor reflects the published status.
    expect(await within(editor).findByText(tr.publishedNotice)).toBeInTheDocument()

    // The coverage counts update without a manual refresh: the summary view is
    // re-read fresh when opened (M-2 / UXR-MDF-022).
    const callsBefore = summaryCalls
    await user.click(screen.getByRole('button', { name: tr.summary }))
    const publishedCount = await screen.findByTestId('coverage-count-published')
    expect(publishedCount).toHaveTextContent('2')
    expect(screen.getByTestId('coverage-count-draft')).toHaveTextContent('1')
    expect(summaryCalls).toBeGreaterThan(callsBefore)
  })
})
