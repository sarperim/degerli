import { Suspense } from 'react'
import { Outlet } from 'react-router'

import { Header } from '@/components/Header'
import { LoadingScreen } from '@/components/LoadingScreen'

/**
 * Application shell: the persistent global header surrounds the routed screen.
 * The header lives outside the route `Outlet`, so language/context changes do
 * not remount or navigate the active screen (UXR-G-013).
 */
export function AppShell() {
  return (
    <div className="flex min-h-screen flex-col">
      <Header />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6">
        <Suspense fallback={<LoadingScreen />}>
          <Outlet />
        </Suspense>
      </main>
    </div>
  )
}
