import { Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { Outlet, useMatches } from 'react-router'

import { DataSourceDisclaimer } from '@/components/DataSourceDisclaimer'
import { Disclaimer } from '@/components/Disclaimer'
import { Header } from '@/components/Header'
import { LoadingScreen } from '@/components/LoadingScreen'

/**
 * Application shell: the persistent global header surrounds the routed screen
 * (UXR-G-013); the footer carries the informational-only disclaimer on research
 * surfaces (UXR-G-016) plus the site-wide data-source provenance disclosure
 * (TKT-foundation-029) on every route. The header lives outside the route
 * `Outlet`, so language changes do not remount or navigate the active screen.
 */
export function AppShell() {
  const { t } = useTranslation()
  const matches = useMatches()
  const isResearch = matches.some(
    (match) =>
      (match.handle as { research?: boolean } | undefined)?.research === true,
  )

  return (
    <div className="flex min-h-screen flex-col">
      <Header />
      <main className="mx-auto w-full max-w-content flex-1 px-4 py-section sm:px-8">
        <Suspense fallback={<LoadingScreen />}>
          <Outlet />
        </Suspense>
      </main>
      <footer className="border-t border-border bg-background/80">
        <div className="mx-auto flex w-full max-w-content flex-col gap-4 px-4 py-8 text-xs text-muted-foreground sm:px-8">
          {isResearch ? <Disclaimer /> : null}
          <div className="grid gap-6 md:grid-cols-[1.4fr_0.6fr] md:items-start">
            <DataSourceDisclaimer />
            <p className="font-mono text-[11px] leading-relaxed text-muted-foreground md:text-right">
              {t('app.name')} — {t('app.tagline')}
            </p>
          </div>
        </div>
      </footer>
    </div>
  )
}
