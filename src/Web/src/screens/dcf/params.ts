import type {
  DcfParamKey,
  DcfParams,
  DcfSensitivity,
} from '@/lib/api/contracts'

/**
 * SCR-006 DCF parameter metadata and pure helpers (UX `valuation-dcf.md` §4.1;
 * FR-VAL-003/004; NFR-VAL-004).
 *
 * The eight-parameter set is the confirmed, fully user-editable list (OQ-UX-001):
 * every entry is visible and editable — no hidden, locked or platform-secret
 * assumption exists. Rates are shown in percentage points in the UI and held as
 * canonical fractions (0.12) on the wire (`03` §5), converted at this boundary.
 */

export type DcfParamKind = 'number' | 'rate' | 'integer'

export interface DcfParamDef {
  key: DcfParamKey
  kind: DcfParamKind
  /** i18n key for the parameter's plain-language label. */
  labelKey: string
  /** i18n key for the displayed unit, when the parameter carries one. */
  unitKey?: string
  /** True when the baseline default comes from a canonical statement fact (BR-VAL-008). */
  canonicalFact: boolean
}

/** Wire order of the eight parameters (`DcfComputeEndpoints.ParameterFields`). */
export const DCF_PARAM_ORDER: DcfParamKey[] = [
  'base_fcf',
  'growth_rate',
  'horizon_years',
  'terminal_growth',
  'discount_rate',
  'debt',
  'cash',
  'share_count',
]

export const DCF_PARAMS: DcfParamDef[] = [
  {
    key: 'base_fcf',
    kind: 'number',
    labelKey: 'dcf.param.base_fcf',
    unitKey: 'dcf.unit.currency',
    canonicalFact: true,
  },
  {
    key: 'growth_rate',
    kind: 'rate',
    labelKey: 'dcf.param.growth_rate',
    unitKey: 'dcf.unit.percent',
    canonicalFact: false,
  },
  {
    key: 'horizon_years',
    kind: 'integer',
    labelKey: 'dcf.param.horizon_years',
    unitKey: 'dcf.unit.years',
    canonicalFact: false,
  },
  {
    key: 'terminal_growth',
    kind: 'rate',
    labelKey: 'dcf.param.terminal_growth',
    unitKey: 'dcf.unit.percent',
    canonicalFact: false,
  },
  {
    key: 'discount_rate',
    kind: 'rate',
    labelKey: 'dcf.param.discount_rate',
    unitKey: 'dcf.unit.percent',
    canonicalFact: false,
  },
  {
    key: 'debt',
    kind: 'number',
    labelKey: 'dcf.param.debt',
    unitKey: 'dcf.unit.currency',
    canonicalFact: true,
  },
  {
    key: 'cash',
    kind: 'number',
    labelKey: 'dcf.param.cash',
    unitKey: 'dcf.unit.currency',
    canonicalFact: true,
  },
  {
    key: 'share_count',
    kind: 'integer',
    labelKey: 'dcf.param.share_count',
    unitKey: 'dcf.unit.shares',
    canonicalFact: true,
  },
]

export const DCF_PARAM_BY_KEY: Record<DcfParamKey, DcfParamDef> = DCF_PARAMS.reduce(
  (map, def) => {
    map[def.key] = def
    return map
  },
  {} as Record<DcfParamKey, DcfParamDef>,
)

/** Editable, string-backed field state (so invalid input can be held + validated). */
export type DcfFieldMap = Record<DcfParamKey, string>

/** i18n error keys surfaced inline; a present entry means the field is invalid. */
export type DcfFieldErrors = Partial<Record<DcfParamKey, string>>

const RATE_DECIMALS = 6

/** Trim floating-point noise from a percentage-points conversion (0.12 → 12). */
function cleanNumber(value: number): number {
  return Number(value.toFixed(RATE_DECIMALS))
}

/** Canonical value → editable field text (rates are displayed in percentage points). */
export function valueToField(def: DcfParamDef, value: number): string {
  if (def.kind === 'rate') return String(cleanNumber(value * 100))
  if (def.kind === 'integer') return String(Math.trunc(value))
  return String(value)
}

/** Canonical params → editable field map (order-preserving over the eight keys). */
export function paramsToFields(params: DcfParams): DcfFieldMap {
  const fields = {} as DcfFieldMap
  for (const def of DCF_PARAMS) {
    fields[def.key] = valueToField(def, params[def.key])
  }
  return fields
}

/**
 * Canonical params → editable field map where an absent parameter is seeded as
 * an empty field rather than a fabricated value (UXR-VAL-009, TC-VAL-026). Used
 * for a stock whose statement inputs are missing so the calculator never
 * silently zero-fills a missing canonical fact.
 */
export function paramsToFieldsAllowingMissing(
  params: Partial<DcfParams> | undefined,
): DcfFieldMap {
  const fields = {} as DcfFieldMap
  for (const def of DCF_PARAMS) {
    const value = params?.[def.key]
    fields[def.key] = value === undefined ? '' : valueToField(def, value)
  }
  return fields
}

/**
 * Parse one editable field into its canonical value, or `null` when it is not a
 * usable number. Rates are divided by 100 (percentage points → fraction); a
 * comma decimal separator is accepted (TR keyboards).
 */
export function parseField(def: DcfParamDef, raw: string): number | null {
  const trimmed = raw.trim()
  if (trimmed === '') return null
  const normalized = trimmed.replace(',', '.')
  const parsed = Number(normalized)
  if (!Number.isFinite(parsed)) return null
  return def.kind === 'rate' ? parsed / 100 : parsed
}

export interface DcfValidation {
  /** All eight parsed params, or `null` when any field is invalid. */
  params: DcfParams | null
  errors: DcfFieldErrors
}

/**
 * Validate the editable fields and derive the canonical parameter set.
 *
 * Invalid input (non-numeric, non-integer horizon, unusable share count, or a
 * model the DCF constraint rejects — horizon outside 1..10, discount rate not
 * above terminal growth) yields inline errors and `params: null`; the screen
 * then suppresses recompute and keeps the last valid result visible
 * (UXR-VAL-008, M-6; I-VAL-2 keeps the constants in sync with `/src/Core`).
 */
export function validateFields(fields: DcfFieldMap): DcfValidation {
  const errors: DcfFieldErrors = {}
  const values = {} as DcfParams

  for (const def of DCF_PARAMS) {
    const value = parseField(def, fields[def.key])
    if (value === null) {
      errors[def.key] = 'dcf.error.notNumber'
      continue
    }
    if (def.kind === 'integer' && !Number.isInteger(value)) {
      errors[def.key] = 'dcf.error.notInteger'
      continue
    }
    values[def.key] = value
  }

  if (!errors.share_count && values.share_count <= 0) {
    errors.share_count = 'dcf.error.outOfRange'
  }
  if (!errors.horizon_years && (values.horizon_years < 1 || values.horizon_years > 10)) {
    errors.horizon_years = 'dcf.error.outOfRange'
  }
  if (
    !errors.discount_rate &&
    !errors.terminal_growth &&
    values.discount_rate <= values.terminal_growth
  ) {
    errors.discount_rate = 'dcf.error.notComputable'
  }

  return Object.keys(errors).length > 0 ? { params: null, errors } : { params: values, errors }
}

/** True when the editable fields differ from the served baseline params. */
export function isModifiedVsBaseline(
  fields: DcfFieldMap,
  baselineParams: DcfParams,
): boolean {
  return DCF_PARAMS.some(
    (def) => fields[def.key] !== valueToField(def, baselineParams[def.key]),
  )
}

/** The i18n key for the plain-language verdict direction from the margin of safety. */
export function verdictKey(marginOfSafety: number): string {
  if (Math.abs(marginOfSafety) < 1e-9) return 'dcf.verdict.equal'
  // MOS > 0 → price below the user's fair value; MOS < 0 → price above it.
  return marginOfSafety > 0 ? 'dcf.verdict.below' : 'dcf.verdict.above'
}

export interface DcfCellIndex {
  row: number
  col: number
}

/**
 * Locate the grid cell matching the user's own discount rate × terminal growth
 * (the center cell of the 5×5 grid), so the single-point result is identifiable
 * within the sensitivity table (UXR-VAL-006, TC-VAL-020).
 */
export function findCurrentCellIndex(
  sensitivity: DcfSensitivity,
  discountRate: number,
  terminalGrowth: number,
): DcfCellIndex | null {
  const row = sensitivity.discountRates.findIndex(
    (rate) => Math.abs(rate - discountRate) < 1e-9,
  )
  const col = sensitivity.terminalGrowths.findIndex(
    (growth) => Math.abs(growth - terminalGrowth) < 1e-9,
  )
  return row >= 0 && col >= 0 ? { row, col } : null
}
