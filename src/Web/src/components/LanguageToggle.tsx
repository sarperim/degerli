import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'
import { SUPPORTED_LANGUAGES, type SupportedLanguage } from '@/i18n'

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
      className="inline-flex items-center rounded-md border p-0.5"
    >
      {SUPPORTED_LANGUAGES.map((lng) => {
        const active = lng === current
        return (
          <Button
            key={lng}
            type="button"
            size="sm"
            variant={active ? 'default' : 'ghost'}
            aria-pressed={active}
            lang={lng}
            onClick={() => {
              if (!active) void i18n.changeLanguage(lng)
            }}
          >
            {labelFor[lng]}
          </Button>
        )
      })}
    </div>
  )
}
