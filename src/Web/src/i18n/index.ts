import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'

import tr from './catalogs/tr.json'
import en from './catalogs/en.json'

/** Languages the platform ships; TR is the default (architecture §10.6). */
export const SUPPORTED_LANGUAGES = ['tr', 'en'] as const
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number]

export const DEFAULT_LANGUAGE: SupportedLanguage = 'tr'
export const LANGUAGE_STORAGE_KEY = 'degerli.language'

function isSupported(value: string | null): value is SupportedLanguage {
  return value !== null && (SUPPORTED_LANGUAGES as readonly string[]).includes(value)
}

/**
 * Language resolution for anonymous users: localStorage > TR default.
 * The account-preference leg (UXR-G-014) is applied by the auth/session layer
 * once accounts ship; this scaffold owns the device leg.
 */
function resolveInitialLanguage(): SupportedLanguage {
  if (typeof window === 'undefined') return DEFAULT_LANGUAGE
  try {
    const stored = window.localStorage.getItem(LANGUAGE_STORAGE_KEY)
    if (isSupported(stored)) return stored
  } catch {
    // Storage unavailable (private mode, blocked cookies) — fall through to default.
  }
  return DEFAULT_LANGUAGE
}

void i18n.use(initReactI18next).init({
  resources: {
    tr: { translation: tr },
    en: { translation: en },
  },
  lng: resolveInitialLanguage(),
  fallbackLng: DEFAULT_LANGUAGE,
  supportedLngs: SUPPORTED_LANGUAGES,
  interpolation: { escapeValue: false },
  initImmediate: false,
})

// Per-device persistence of an anonymous language choice (UXR-G-014).
i18n.on('languageChanged', (lng) => {
  if (typeof window === 'undefined') return
  try {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, lng)
  } catch {
    // Ignore storage failures — the in-memory switch has already happened.
  }
})

export default i18n
