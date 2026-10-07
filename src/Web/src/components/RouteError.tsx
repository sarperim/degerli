import { useTranslation } from 'react-i18next'
import { useRouteError } from 'react-router'

import { Button } from '@/components/ui/button'

/**
 * Route error boundary (UXR-G-003): plain-language message + retry, never a raw
 * technical error. The full retry/invalidation story arrives with the data layer.
 */
export function RouteError() {
  const { t } = useTranslation()
  const error = useRouteError()
  if (import.meta.env.DEV) {
    // Technical detail is logged, never rendered to users (UXR-G-003).
    console.error(error)
  }

  return (
    <div className="mx-auto flex min-h-[40vh] max-w-md flex-col items-center justify-center gap-4 text-center">
      <p className="text-lg font-medium">{t('common.error')}</p>
      <Button onClick={() => window.location.reload()}>{t('common.retry')}</Button>
    </div>
  )
}
