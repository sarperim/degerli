import { readFileSync } from 'node:fs'
import path from 'node:path'

import { WEB_CATALOG_DIR } from '../config'

/**
 * Loads the checked-in i18n catalogs at test time (test strategy §5.4/§8.3):
 * assertions on copy read `tr.json`/`en.json` from the repo instead of hardcoding
 * prose that could silently drift from the product.
 */

export type Catalog = Record<string, unknown>

export type CatalogLanguage = 'tr' | 'en'

export function loadCatalog(language: CatalogLanguage): Catalog {
  return JSON.parse(
    readFileSync(path.join(WEB_CATALOG_DIR, `${language}.json`), 'utf8'),
  ) as Catalog
}

/** Resolves a dotted i18n key (e.g. `screens.marketOverview.title`) to its value. */
export function catalogValue(catalog: Catalog, key: string): string {
  const value = key.split('.').reduce<unknown>((node, part) => {
    if (node && typeof node === 'object') {
      return (node as Record<string, unknown>)[part]
    }
    return undefined
  }, catalog)

  if (typeof value !== 'string') {
    throw new Error(`i18n catalog key "${key}" is missing or not a string`)
  }
  return value
}
