import { useTranslation } from 'react-i18next'
import { Info } from 'lucide-react'

import { cn } from '@/lib/utils'

/**
 * Informational-only disclaimer (design-system component, TKT-foundation-011).
 *
 * Rendered on every research surface (UXR-G-016; FR-MOV-020/SCR-016/RES-017/
 * VAL-008). The shell places the single global instance in the footer for
 * research routes; a screen may also render one inline for local context.
 *
 * `variant` keeps the two from colliding as duplicate `complementary` landmarks
 * (axe `landmark-is-unique`, UXR-G-027): the shell's global instance is the
 * landmark (`landmark`, default), an inline per-screen instance is a `note`.
 * It states plainly that the platform is informational only — never buy/sell
 * language, targets or scores (UXR-G-017).
 */
export function Disclaimer({
  className,
  variant = 'landmark',
}: {
  className?: string
  variant?: 'landmark' | 'note'
}) {
  const { t } = useTranslation()
  const isNote = variant === 'note'
  const Tag = isNote ? 'div' : 'aside'
  return (
    <Tag
      data-slot="disclaimer"
      {...(isNote ? { role: 'note' } : {})}
      aria-label={t('disclaimer.label')}
      className={cn(
        'flex items-start gap-2 rounded-md border bg-muted/40 px-3 py-2 text-xs text-muted-foreground',
        className,
      )}
    >
      <Info aria-hidden="true" className="mt-0.5 size-3.5 shrink-0" />
      <p>{t('disclaimer.text')}</p>
    </Tag>
  )
}
