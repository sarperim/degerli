import { useTranslation } from 'react-i18next'

/** Route-level loading state (UXR-G-001); shell owns the shared indicator. */
export function LoadingScreen() {
  const { t } = useTranslation()
  return (
    <div
      role="status"
      aria-live="polite"
      className="flex min-h-[40vh] items-center justify-center text-muted-foreground"
    >
      {t('common.loading')}
    </div>
  )
}
