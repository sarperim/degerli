import { useTranslation } from 'react-i18next'
import { Info } from 'lucide-react'

import { cn } from '@/lib/utils'

/**
 * Informational-only disclaimer (design-system component, TKT-foundation-011).
 *
 * Rendered on every research surface (UXR-G-016; FR-MOV-020/SCR-016/RES-017/
 * VAL-008). The shell places it in the footer for research routes so it is
 * present regardless of how an individual screen is assembled; screens may also
 * render it inline. It states plainly that the platform is informational only —
 * never buy/sell language, targets or scores (UXR-G-017).
 */
export function Disclaimer({ className }: { className?: string }) {
  const { t } = useTranslation()
  return (
    <aside
      data-slot="disclaimer"
      aria-label={t('disclaimer.label')}
      className={cn(
        'flex items-start gap-2 rounded-md border bg-muted/40 px-3 py-2 text-xs text-muted-foreground',
        className,
      )}
    >
      <Info aria-hidden="true" className="mt-0.5 size-3.5 shrink-0" />
      <p>{t('disclaimer.text')}</p>
    </aside>
  )
}
