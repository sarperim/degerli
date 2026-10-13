import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'

/**
 * SCR-012 access-denied state (UXR-MDF-002).
 *
 * A non-builder (regular account or anonymous) reaching the admin route sees a
 * plain-language denial with a path back to platform content — never a blank page
 * or a raw technical error.
 */
export function AdminAccessDenied() {
  const { t } = useTranslation()
  return (
    <div
      data-testid="admin-access-denied"
      className="mx-auto flex max-w-prose flex-col items-start gap-4 rounded-lg border p-8"
    >
      <h1 className="text-h2 font-semibold">{t('admin.accessDenied.title')}</h1>
      <p className="text-sm text-muted-foreground">{t('admin.accessDenied.body')}</p>
      <Link to="/" className="text-sm font-medium text-primary underline-offset-4 hover:underline">
        {t('admin.accessDenied.back')}
      </Link>
    </div>
  )
}
