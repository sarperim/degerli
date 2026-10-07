import i18next, { type i18n as I18n } from 'i18next'
import { initReactI18next } from 'react-i18next'

import {
  DEFAULT_LANGUAGE,
  SUPPORTED_LANGUAGES,
  type SupportedLanguage,
} from '@/i18n'
import en from '@/i18n/catalogs/en.json'
import tr from '@/i18n/catalogs/tr.json'

/**
 * i18n loading for tests (test strategy §8.3): the catalogs are imported from
 * the repository at test time, so assertions read the same copy the app ships —
 * never hardcoded prose.
 */
export const CATALOGS = { tr, en } as const

/** Resolve a dotted catalog key to its string, throwing on a missing key. */
export function catalogValue(language: SupportedLanguage, key: string): string {
  let node: unknown = CATALOGS[language]

  for (const segment of key.split('.')) {
    if (typeof node !== 'object' || node === null || !(segment in node)) {
      throw new Error(`Missing i18n key "${key}" in ${language}.json`)
    }
    node = (node as Record<string, unknown>)[segment]
  }

  if (typeof node !== 'string') {
    throw new Error(`i18n key "${key}" in ${language}.json does not resolve to a string`)
  }
  return node
}

/** Build an isolated i18n instance for a test render (no shared global state). */
export function createTestI18n(
  language: SupportedLanguage = DEFAULT_LANGUAGE,
): I18n {
  const instance = i18next.createInstance()

  void instance.use(initReactI18next).init({
    resources: {
      tr: { translation: tr },
      en: { translation: en },
    },
    lng: language,
    fallbackLng: DEFAULT_LANGUAGE,
    supportedLngs: [...SUPPORTED_LANGUAGES],
    interpolation: { escapeValue: false },
    initImmediate: false,
  })

  return instance
}
