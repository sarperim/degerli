import { useTranslation } from 'react-i18next'

import type { ScreenRoute } from '@/routes/routeConfig'

/**
 * Structural placeholder for every SCR route (TKT-foundation-002 scope).
 *
 * Each screen ticket replaces the placeholder with its routed implementation
 * while keeping the route contract (`ScreenRoute`) declared here.
 */
export function RoutePlaceholder({ route }: { route: ScreenRoute }) {
  const { t } = useTranslation()

  return (
    <section
      data-screen={route.screen}
      data-route-id={route.id}
      className="flex flex-col gap-3"
    >
      <div className="flex items-baseline gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">
          {t(route.titleKey)}
        </h1>
        <span className="rounded-full border px-2 py-0.5 text-xs text-muted-foreground">
          {route.screen}
        </span>
        <span className="rounded-full border px-2 py-0.5 text-xs text-muted-foreground">
          {t('placeholder.badge')}
        </span>
      </div>
      <p className="max-w-2xl text-muted-foreground">{t(route.descriptionKey)}</p>
      <p className="text-sm text-muted-foreground">{t('placeholder.note')}</p>
    </section>
  )
}
