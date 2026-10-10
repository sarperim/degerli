import { Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { Outlet, useMatches } from 'react-router'

import { Disclaimer } from '@/components/Disclaimer'
import { Header } from '@/components/Header'
import { LoadingScreen } from '@/components/LoadingScreen'

/**
 * Application shell: the persistent global header surrounds the routed screen
 * (UXR-G-013); the footer carries the informational-only disclaimer on research
 * surfaces (UXR-G-016). The header lives outside the route `Outlet`, so language
 * changes do not remount or navigate the active screen.
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
      <main className="mx-auto w-full max-w-content flex-1 px-gutter py-section">
        <Suspense fallback={<LoadingScreen />}>
          <Outlet />
        </Suspense>
      </main>
      <footer className="border-t bg-background">
        <div className="mx-auto flex w-full max-w-content flex-col gap-3 px-gutter py-6 text-xs text-muted-foreground">
          {isResearch ? <Disclaimer /> : null}
          <p>
            {t('app.name')} — {t('app.tagline')}
          </p>
        </div>
      </footer>
    </div>
  )
}
