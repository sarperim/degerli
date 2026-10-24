import type { TFunction } from 'i18next'

/**
 * Display-side unit semantics (TKT-foundation-072; `03` §1 principle 1).
 *
 * **Codes and numbers from the server; words from the client.** A served `unit`
 * (`%`, `% YoY`, `x`, `TRY`, `USD/oz`, `TRY/g` — the seeded `macro_series` and
 * `screener_metrics` vocabulary) is a **code**, not display prose: rendering it
 * verbatim shows a server identifier to the user and hides the translation
 * (NFR-MOV-004, FR-MOV-009). Every surface that renders a unit resolves it through
 * this one map, the same discipline the DCF screen already applies to its params
 * (`screens/dcf/params.ts` → `dcf.unit.*`).
 *
 * **Unknown codes degrade honestly.** The catalog is server-driven and its `unit`
 * is an open string (`03` §4), so this client can meet a code it does not know. It
 * then says so ("unit not specified" / "Birim belirtilmemiş") rather than showing
 * the raw code or guessing a unit: a unit label is a claim about the data
 * (FR-RES-017), and a guess would be a false one.
 *
 * **Fixed-point units and money.** `%` and `x` are language-neutral symbols — their
 * display form is the glyph itself. Everything with words around it (`% YoY`,
 * `USD/oz`, `TRY/g`, `TRY`) is localized per catalog.
 */

/**
 * Served unit code → i18n key. Covers the seeded macro/screener vocabulary
 * (`%`, `% YoY`, `x`, `TRY`, `USD/oz`) plus `TRY/g`, which appears only in the
 * MSW test fixtures.
 *
 * The literals live here so the i18n parity gate (`scripts/i18n-parity.mjs`) sees
 * every key referenced in source.
 */
export const UNIT_LABEL_KEYS: Record<string, string> = {
  '%': 'units.percent',
  '% YoY': 'units.percentYoY',
  x: 'units.ratio',
  TRY: 'units.currencyTry',
  'USD/oz': 'units.usdPerOunce',
  'TRY/g': 'units.tryPerGram',
}

/** The honest label for a code this client does not recognize (or an empty one). */
export const UNKNOWN_UNIT_LABEL_KEY = 'units.unknown'

/**
 * The unit of the paid-in-capital figure (TKT-foundation-065 / USER DECISION D4,
 * Option A). The value the `shares` lane serves is the sum of `/memberSecurities`
 * `currentCapital` — money in Turkish lira, proven by the 64/556 rows that carry
 * sub-unit precision — so it is never rendered as a count of shares.
 */
export const PAID_IN_CAPITAL_UNIT_CODE = 'TRY'

/** Units that attach to the number with no separating space (`15%`, `10x`). */
const TIGHT_UNIT_CODES = new Set(['%', 'x'])

/** True for the `%` unit, whose canonical served values are decimal fractions. */
export function isPercentUnit(unit: string | null | undefined): boolean {
  return unit === '%'
}

/** Resolve a served unit code to its i18n key; unknown/empty → the honest fallback. */
export function unitLabelKey(unit: string | null | undefined): string {
  if (!unit) return UNKNOWN_UNIT_LABEL_KEY
  // The served code is an open, server-supplied string: resolve only own entries,
  // never inherited members (`constructor`, `toString`, `__proto__`, …).
  return Object.hasOwn(UNIT_LABEL_KEYS, unit)
    ? UNIT_LABEL_KEYS[unit]
    : UNKNOWN_UNIT_LABEL_KEY
}

/** Localized display label for a served unit code. */
export function unitLabel(
  unit: string | null | undefined,
  t: TFunction,
): string {
  return t(unitLabelKey(unit))
}

/**
 * The label as a suffix for a formatted number: `15%`, `10x`, `47,10 ₺`,
 * `5.200,00 USD/ons`. Unknown/empty codes still produce the honest fallback.
 */
export function unitSuffix(
  unit: string | null | undefined,
  t: TFunction,
): string {
  const label = unitLabel(unit, t)
  return unit && TIGHT_UNIT_CODES.has(unit) ? label : ` ${label}`
}
