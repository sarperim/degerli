import { useTranslation } from 'react-i18next'

import { SUPPORTED_LANGUAGES, type SupportedLanguage } from '@/i18n'
import { cn } from '@/lib/utils'

/**
 * Header language toggle (UXR-G-013/014).
 *
 * Switching calls `i18n.changeLanguage` only — no navigation and no scroll
 * mutation — so the current URL, view state and scroll position are preserved
 * and re-render is immediate (no page reload).
 */
export function LanguageToggle() {
  const { t, i18n } = useTranslation()
  const current: SupportedLanguage = i18n.resolvedLanguage === 'en' ? 'en' : 'tr'

  const labelFor: Record<SupportedLanguage, string> = {
    tr: t('header.turkish'),
    en: t('header.english'),
  }

  return (
    <div
      role="group"
      aria-label={t('header.languageToggle')}
      className="inline-flex h-[30px] shrink-0 items-center overflow-hidden rounded-[2px] border border-border bg-transparent font-mono text-[10px]"
    >
      {SUPPORTED_LANGUAGES.map((lng, index) => {
        const active = lng === current
        return (
          <button
            key={lng}
            type="button"
            aria-label={labelFor[lng]}
            aria-pressed={active}
            lang={lng}
            onClick={() => {
              if (!active) void i18n.changeLanguage(lng)
            }}
            className={cn(
              'h-full cursor-pointer px-2 font-mono text-[10px] tracking-wide transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset',
              index > 0 && 'border-l border-border',
              active
                ? 'bg-accent font-medium text-foreground'
                : 'text-muted-foreground hover:bg-accent/50 hover:text-foreground',
            )}
          >
            {lng.toUpperCase()}
          </button>
        )
      })}
    </div>
  )
}
