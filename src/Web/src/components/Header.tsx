import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router'

import { AuthState } from '@/components/AuthState'
import { LanguageToggle } from '@/components/LanguageToggle'
import { StockSearch } from '@/components/StockSearch'
import { ThemeToggle } from '@/components/ThemeToggle'
import { useIsBuilder, useSession } from '@/lib/auth/session'
import { NAVIGATION_ROUTES } from '@/routes/routeConfig'
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
 *
 * The admin entry is additionally presented only to a builder-role account
 * (UXR-MDF-001, architecture AD-13). Both gates apply: a route renders when it
 * is not auth-gated or the session is authenticated, AND it is not builder-only
 * or the session is a builder.
 */
export function Header() {
  const { t } = useTranslation()
  const session = useSession()
  const authenticated = session.data?.authenticated === true
  const isBuilder = useIsBuilder()
  const routes = NAVIGATION_ROUTES.filter(
    (route) =>
      (!route.requiresAuth || authenticated) &&
      (route.builderOnly !== true || isBuilder),
  )

  return (
    <header
      aria-label={t('header.ariaLabel')}
      className="sticky top-0 z-40 border-b border-border bg-background/90 backdrop-blur-md"
    >
      <div
        className={cn(
          'mx-auto flex w-full flex-wrap items-center gap-x-4 gap-y-2 px-4 py-2',
          authenticated
            ? 'max-w-[1600px] sm:px-6 xl:min-h-[54px] xl:flex-nowrap xl:px-8 xl:py-0'
            : 'max-w-content sm:px-8 lg:min-h-[54px] lg:flex-nowrap lg:py-0',
        )}
      >
        <NavLink to="/" data-magnetic="" className="flex shrink-0 items-baseline gap-2">
          <span
            lang="tr"
            className="text-sm font-bold tracking-[0.12em] uppercase text-foreground"
          >
            {t('app.name')}
          </span>
          <span
            className={cn(
              'hidden font-mono text-[10px] tracking-[0.06em] text-muted-foreground',
              authenticated ? '2xl:inline' : 'sm:inline',
            )}
          >
            BIST EOD
          </span>
        </NavLink>

        <nav
          aria-label={t('header.primaryNav')}
          className={cn(
            'order-last flex w-full flex-wrap items-center border-t border-border/60 pt-2',
            authenticated
              ? 'gap-x-3.5 gap-y-1.5 xl:order-none xl:w-auto xl:min-w-0 xl:flex-nowrap xl:border-t-0 xl:pt-0'
              : 'gap-x-4 gap-y-1 lg:order-none lg:w-auto lg:min-w-0 lg:flex-nowrap lg:border-t-0 lg:pt-0',
          )}
        >
          {routes.map((route) => (
            <NavLink
              key={route.id}
              to={route.path}
              end={route.path === '/'}
              data-magnetic=""
              className={({ isActive }) =>
                cn(
                  'shrink-0 whitespace-nowrap border-b-[1.5px] py-1 text-[13px] transition-colors hover:text-foreground',
                  isActive
                    ? 'border-[var(--brand-accent)] font-medium text-foreground'
                    : 'border-transparent text-muted-foreground',
                )
              }
            >
              {t(route.navLabelKey)}
            </NavLink>
          ))}
        </nav>

        <div
          className={cn(
            'ms-auto flex w-full flex-wrap items-center gap-2 sm:w-auto',
            authenticated
              ? 'xl:shrink-0 xl:flex-nowrap'
              : 'lg:shrink-0 lg:flex-nowrap',
          )}
        >
          <StockSearch />
          <ThemeToggle />
          <LanguageToggle />
          <AuthState />
        </div>
      </div>
    </header>
  )
}
