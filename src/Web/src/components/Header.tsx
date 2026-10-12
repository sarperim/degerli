import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router'

import { AuthState } from '@/components/AuthState'
import { LanguageToggle } from '@/components/LanguageToggle'
import { StockSearch } from '@/components/StockSearch'
import { NAVIGATION_ROUTES } from '@/routes/routeConfig'
import { useSession } from '@/lib/auth/session'
import { cn } from '@/lib/utils'

/**
 * Global header shell (UX `00-screen-inventory.md` §3): brand, language toggle,
 * auth-state area and stock-search placeholder — present on every screen.
 *
 * Primary navigation is signed-in-only where the route demands it: links whose
 * route contract is `requiresAuth` (My Saved Screens, My DCF Scenarios, Account
 * Settings) render only to an authenticated session, so anonymous visitors see
 * just the public research surfaces and SCR-010 gains its signed-in entry point
 * (UXR-ACC-020, `routes/routeConfig.ts`).
 */
export function Header() {
  const { t } = useTranslation()
  const session = useSession()
  const authenticated = session.data?.authenticated === true

  return (
    <header
      aria-label={t('header.ariaLabel')}
      className="border-b bg-background"
    >
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-6 gap-y-3 px-4 py-3">
        <NavLink to="/" className="flex flex-col leading-tight">
          <span className="text-lg font-semibold">{t('app.name')}</span>
          <span className="text-xs text-muted-foreground">{t('app.tagline')}</span>
        </NavLink>

        <nav
          aria-label={t('header.primaryNav')}
          className="order-last flex w-full flex-wrap items-center gap-x-4 gap-y-1 sm:order-none sm:w-auto"
        >
          {NAVIGATION_ROUTES.filter(
            (route) => !route.requiresAuth || authenticated,
          ).map((route) => (
            <NavLink
              key={route.id}
              to={route.path}
              end={route.path === '/'}
              className={({ isActive }) =>
                cn(
                  'text-sm transition-colors hover:text-foreground',
                  isActive
                    ? 'font-medium text-foreground'
                    : 'text-muted-foreground',
                )
              }
            >
              {t(route.navLabelKey)}
            </NavLink>
          ))}
        </nav>

        <div className="ms-auto flex w-full flex-wrap items-center gap-3 sm:w-auto">
          <StockSearch />
          <LanguageToggle />
          <AuthState />
        </div>
      </div>
    </header>
  )
}
