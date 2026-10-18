import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'

import { apiGet } from '@/lib/api/client'
import { cn } from '@/lib/utils'

/**
 * Site-wide data-source provenance disclosure (TKT-foundation-029).
 *
 * Answers "where does the data come from?" per data class — distinct from, and
 * complementing, the informational-only `Disclaimer` (UXR-G-016, the "not
 * investment advice" posture). It is rendered in the shell footer (`AppShell.tsx`)
 * on **every** route — the origin of every data class is answered wherever data is
 * shown — not only on research routes.
 *
 * It is a plain footer text block, **not** another `complementary` landmark (the
 * `landmark-is-unique` rule the `Disclaimer` variants already respect), so the two
 * never collide. Copy is localized (TR/EN); class codes, source names and the honest
 * `state: live|unavailable` come from the server (`GET /api/v1/meta/data-sources`),
 * the words from the i18n catalogs (`03` §1.1, `01` §10.6).
 *
 * Honesty: a class with no live values renders the explicit unavailable marker and
 * never claims a live source; a dev/fixture-only world is never presented as real
 * (`01` §10.7). The class list is served by a small static server-side catalog so
 * liveness stays server-owned rather than drifting in a build-time web bundle.
 */

/** One disclosed data class as the API serves it (`03` §3). */
export interface DataSourceEntry {
  id: string
  class: string
  source: string
  state: 'live' | 'unavailable'
  nature: string
}

/** `GET /api/v1/meta/data-sources` response (`03` §3). */
export interface DataSourceCatalogResponse {
  sources: DataSourceEntry[]
}

/** Query-key factory so prefetch/invalidation stay consistent. */
const dataSourcesQueryKeys = {
  catalog: () => ['meta', 'data-sources'] as const,
}

function fetchDataSourceCatalog(
  signal?: AbortSignal,
): Promise<DataSourceCatalogResponse> {
  return apiGet<DataSourceCatalogResponse>('/meta/data-sources', signal)
}

/** Plain-language class labels, keyed by the stable server class code. */
const CLASS_LABEL_KEYS: Record<string, string> = {
  prices: 'dataSources.classes.prices',
  indexLevels: 'dataSources.classes.indexLevels',
  universe: 'dataSources.classes.universe',
  statements: 'dataSources.classes.statements',
  macro: 'dataSources.classes.macro',
}

/** Plain-language as-of natures, keyed by the stable server nature code. */
const NATURE_KEYS: Record<string, string> = {
  eod: 'dataSources.nature.eod',
}

export function DataSourceDisclaimer({ className }: { className?: string }) {
  const { t } = useTranslation()
  const { data } = useQuery({
    queryKey: dataSourcesQueryKeys.catalog(),
    queryFn: ({ signal }) => fetchDataSourceCatalog(signal),
    staleTime: Infinity,
  })

  if (!data) {
    return null
  }

  return (
    <div
      data-slot="data-source-disclaimer"
      data-testid="data-source-disclaimer"
      className={cn('flex flex-col gap-2', className)}
    >
      <p className="font-medium text-foreground">{t('dataSources.label')}</p>
      <ul className="flex flex-col gap-1">
        {data.sources.map((entry) => {
          const labelKey = CLASS_LABEL_KEYS[entry.class]
          const label = labelKey ? t(labelKey) : entry.class
          const natureKey = NATURE_KEYS[entry.nature]
          const nature = natureKey ? t(natureKey) : entry.nature
          const live = entry.state === 'live'
          return (
            <li
              key={entry.id}
              data-testid={`data-source-${entry.class}`}
              data-state={entry.state}
              className="flex flex-wrap items-baseline gap-x-2"
            >
              <span className="text-foreground">
                {t('dataSources.line', { class: label, source: entry.source })}
              </span>
              <span
                data-testid={`data-source-nature-${entry.class}`}
                className="text-muted-foreground"
              >
                {nature}
              </span>
              <span
                data-testid={`data-source-state-${entry.class}`}
                className="text-muted-foreground"
              >
                {live
                  ? t('dataSources.state.live')
                  : t('dataSources.state.unavailable')}
              </span>
            </li>
          )
        })}
      </ul>
      <p className="text-muted-foreground">{t('dataSources.freeSourceNote')}</p>
      <p
        data-testid="data-source-fixture-caveat"
        className="text-muted-foreground"
      >
        {t('dataSources.fixtureCaveat')}
      </p>
    </div>
  )
}
