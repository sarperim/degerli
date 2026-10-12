import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'
import { SkeletonText } from '@/components/ui/skeleton'

export interface SectionBodyProps {
  /** Localized section label, used to make the loading/error states self-describing. */
  label: string
  isPending: boolean
  isError: boolean
  onRetry: () => void
  children: ReactNode
}

/**
 * Per-section load/fail/retry frame (SCR-005 §5; architecture M-5).
 *
 * Each stock-page section owns its query, so it renders, loads, fails and retries
 * independently: one failing section shows its own plain-language error and retry
 * while the others stay fully usable (UXR-G-003). The retry trigger is locked
 * while a refetch is in flight (UXR-G-020).
 */
export function SectionBody({
  label,
  isPending,
  isError,
  onRetry,
  children,
}: SectionBodyProps) {
  const { t } = useTranslation()

  if (isPending) {
    return (
      <div
        role="status"
        data-section-state="loading"
        aria-label={`${label} — ${t('common.loading')}`}
      >
        <SkeletonText lines={3} />
      </div>
    )
  }

  if (isError) {
    return (
      <div
        role="alert"
        data-section-state="error"
        className="flex flex-col items-start gap-3 rounded-lg border border-dashed px-6 py-8 text-center"
      >
        <p className="text-h3 font-medium text-foreground">
          {t('stockPage.sectionErrorTitle')}
        </p>
        <p className="max-w-prose text-sm text-muted-foreground">
          {t('stockPage.sectionErrorBody')}
        </p>
        <Button variant="outline" onClick={onRetry}>
          {t('common.retry')}
        </Button>
      </div>
    )
  }

  return <>{children}</>
}
