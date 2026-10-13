import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'

/**
 * Per-section error surface for the admin screen (UXR-G-003; SCR-012 §5 states).
 *
 * Each admin section renders and fails independently — one section's error never
 * blanks the screen. The copy is plain-language; the retry re-fetches only this
 * section.
 */
export function SectionError({
  title,
  body,
  onRetry,
}: {
  title: string
  body: string
  onRetry: () => void
}) {
  const { t } = useTranslation()
  return (
    <div
      role="alert"
      data-slot="section-error"
      className="flex flex-col items-start gap-2 rounded-lg border border-destructive/30 bg-destructive/5 p-4"
    >
      <p className="font-medium text-foreground">{title}</p>
      <p className="text-sm text-muted-foreground">{body}</p>
      <Button type="button" variant="outline" size="sm" onClick={onRetry}>
        {t('common.retry')}
      </Button>
    </div>
  )
}
