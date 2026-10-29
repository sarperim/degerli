import type { KeyMetricEntry } from '@/lib/api/contracts'
import { formatNumber, formatPercent } from '@/lib/format'

/**
 * The key value metrics & moat panel's row model (TKT-foundation-097).
 *
 * The rows are the user's v0 spec row set (recorded verbatim on TKT-foundation-095)
 * mapped onto the canonical codes TKT-foundation-096's `GET /stocks/{symbol}/key-metrics`
 * serves. **Nothing here computes a value** (BR-RES-004): a row names the served
 * code and the way that served figure is formatted; the component renders the
 * served `metrics[code]` entry.
 *
 * **Basis honesty (DR-3 routing from the 096 review).** The backend serves
 * **group/consolidated**-basis `roe` and `eps_cagr_*`, while the user's spec names
 * "ROE (Parent)" and "EPS Parent". Rendering a parent label over a group number
 * would be a mislabel, so those rows are labelled with the basis the payload
 * actually carries (group/consolidated). The parent-basis pair is a routed
 * planner follow-up; this view never fabricates it.
 */

/** How a served metric is formatted: a fraction rendered as `%`, or a plain ratio. */
export type KeyMetricFormat = 'percent' | 'ratio'

/** Format one served metric in its row's unit (a fraction as `%`, else a ratio). */
export function formatKeyMetric(
  locale: string,
  format: KeyMetricFormat,
  value: number,
): string {
  return format === 'percent'
    ? formatPercent(locale, value, 2)
    : formatNumber(locale, value, { maximumFractionDigits: 2 })
}

export interface KeyMetricRow {
  /** The canonical metric code the row binds to. */
  code: string
  /** The catalog key for the spec row name (TR/EN). */
  labelKey: string
  format: KeyMetricFormat
}

export interface KeyMetricGroup {
  key: 'moat' | 'valuation' | 'health' | 'growth'
  labelKey: string
  rows: KeyMetricRow[]
}

const row = (
  code: string,
  labelKey: string,
  format: KeyMetricFormat = 'ratio',
): KeyMetricRow => ({ code, labelKey, format })

/**
 * The four display groups (moat / valuation / health / growth). Order follows the
 * user's spec; the labels are scaffolding until the ux-designer folds the spec into
 * SCR-005 (the 095 flow-back routing item).
 *
 * `roe` and `eps_cagr_*` carry the **group/consolidated** basis in their labels —
 * the served basis — never the spec's Parent wording (see the module note).
 */
export const KEY_METRIC_GROUPS: readonly KeyMetricGroup[] = [
  {
    key: 'moat',
    labelKey: 'stockPage.keyMetrics.groups.moat',
    rows: [
      row('roic', 'stockPage.keyMetrics.rows.roic', 'percent'),
      row('croic', 'stockPage.keyMetrics.rows.croic', 'percent'),
      row('roe', 'stockPage.keyMetrics.rows.roe', 'percent'),
      row('roa', 'stockPage.keyMetrics.rows.roa', 'percent'),
      row('fcf_ni', 'stockPage.keyMetrics.rows.fcf_ni'),
      row('fcf_ni_3y', 'stockPage.keyMetrics.rows.fcf_ni_3y'),
      row('cfo_ebit', 'stockPage.keyMetrics.rows.cfo_ebit'),
      row('monetary_gain_pretax', 'stockPage.keyMetrics.rows.monetary_gain_pretax', 'percent'),
    ],
  },
  {
    key: 'valuation',
    labelKey: 'stockPage.keyMetrics.groups.valuation',
    rows: [
      row('ev_ebit', 'stockPage.keyMetrics.rows.ev_ebit'),
      row('ev_ebitda', 'stockPage.keyMetrics.rows.ev_ebitda'),
      row('pe', 'stockPage.keyMetrics.rows.pe'),
      row('p_fcf', 'stockPage.keyMetrics.rows.p_fcf'),
      row('fcf_yield', 'stockPage.keyMetrics.rows.fcf_yield', 'percent'),
      row('pb', 'stockPage.keyMetrics.rows.pb'),
      row('p_tbv', 'stockPage.keyMetrics.rows.p_tbv'),
      row('div_yield', 'stockPage.keyMetrics.rows.div_yield', 'percent'),
    ],
  },
  {
    key: 'health',
    labelKey: 'stockPage.keyMetrics.groups.health',
    rows: [
      row('net_debt_ebitda', 'stockPage.keyMetrics.rows.net_debt_ebitda'),
      row('net_debt_fcf', 'stockPage.keyMetrics.rows.net_debt_fcf'),
      row('interest_coverage', 'stockPage.keyMetrics.rows.interest_coverage'),
      row('cfo_interest', 'stockPage.keyMetrics.rows.cfo_interest'),
      row('current_ratio', 'stockPage.keyMetrics.rows.current_ratio'),
    ],
  },
  {
    key: 'growth',
    labelKey: 'stockPage.keyMetrics.groups.growth',
    rows: [
      row('rev_cagr_3y', 'stockPage.keyMetrics.rows.rev_cagr_3y', 'percent'),
      row('rev_cagr_5y', 'stockPage.keyMetrics.rows.rev_cagr_5y', 'percent'),
      row('ebit_cagr_3y', 'stockPage.keyMetrics.rows.ebit_cagr_3y', 'percent'),
      row('ebit_cagr_5y', 'stockPage.keyMetrics.rows.ebit_cagr_5y', 'percent'),
      row('eps_cagr_3y', 'stockPage.keyMetrics.rows.eps_cagr_3y', 'percent'),
      row('eps_cagr_5y', 'stockPage.keyMetrics.rows.eps_cagr_5y', 'percent'),
      row('fcf_ps_cagr_3y', 'stockPage.keyMetrics.rows.fcf_ps_cagr_3y', 'percent'),
      row('fcf_ps_cagr_5y', 'stockPage.keyMetrics.rows.fcf_ps_cagr_5y', 'percent'),
      row('bvps_cagr_3y', 'stockPage.keyMetrics.rows.bvps_cagr_3y', 'percent'),
      row('bvps_cagr_5y', 'stockPage.keyMetrics.rows.bvps_cagr_5y', 'percent'),
    ],
  },
]

/** Every row code in display order — the panel's label-parity fixture. */
export const KEY_METRIC_CODES: readonly string[] = KEY_METRIC_GROUPS.flatMap(
  (group) => group.rows.map((entry) => entry.code),
)

/**
 * The rows a profile should see. A financial-sector instrument is served every
 * metric with the industrial-shaped ones honestly `profileGated`; the panel then
 * shows only the profile-appropriate rows (the served gate, never a client list):
 * a bank's `pe`/`roe`/`roa` and its share-based CAGRs stay, the industrial-shaped
 * codes are omitted rather than rendered as a wall of not-meaningful rows.
 */
export function visibleKeyMetricGroups(
  metrics: Record<string, KeyMetricEntry>,
  financialProfile: boolean,
): KeyMetricGroup[] {
  if (!financialProfile) return KEY_METRIC_GROUPS.map((group) => ({ ...group }))
  return KEY_METRIC_GROUPS.map((group) => ({
    ...group,
    rows: group.rows.filter((entry) => metrics[entry.code]?.profileGated !== true),
  })).filter((group) => group.rows.length > 0)
}

/** True when any rendered metric carries the served TMS 29 regime-break marker. */
export function hasRegimeBreak(metrics: Record<string, KeyMetricEntry>): boolean {
  return Object.values(metrics).some((entry) => entry.regimeBreak === true)
}

/** True when any rendered metric is served on the corporate-action-adjusted basis. */
export function hasAdjustedMetric(metrics: Record<string, KeyMetricEntry>): boolean {
  return Object.values(metrics).some((entry) => entry.isAdjusted === true)
}

/**
 * The TMS 29 distortion alert's subject: the served `monetary_gain_pretax` entry.
 * The alert is rendered from the served fact (the ratio exists and is displayed),
 * never from a client-side threshold — the spec's "high" reading stays a served
 * figure the user can judge beside its label, not a client verdict.
 */
export function monetaryGainAlert(
  metrics: Record<string, KeyMetricEntry>,
): KeyMetricEntry | null {
  const entry = metrics.monetary_gain_pretax
  if (entry === undefined || entry.value === null) return null
  return entry
}

/** The catalog key for a served corporate-action kind; unknown kinds stay unlabelled. */
export function corporateActionKindKey(kind: string): string | null {
  switch (kind) {
    case 'bedelli':
      return 'stockPage.capitalTracker.actionKinds.bedelli'
    case 'bedelsiz':
      return 'stockPage.capitalTracker.actionKinds.bedelsiz'
    case 'split':
      return 'stockPage.capitalTracker.actionKinds.split'
    default:
      return null
  }
}
