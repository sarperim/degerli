import { Fragment, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

import { RestatedMarker } from '@/components/honest-data'
import { Badge } from '@/components/ui/badge'
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import type {
  EvFact,
  FinancialPeriod,
  LayoutNotice,
  StatementFigure,
  StockFinancialsResponse,
} from '@/lib/api/contracts'
import { formatAsOfDate, formatNumber, formatPercent } from '@/lib/format'
import { useStatementBasis } from '@/screens/stock-page/statementsPeriod'
import {
  STATEMENT_SECTIONS,
  evHeaderRows,
  filingBadges,
  periodLabel,
  periodsForBasis,
  rowFigure,
  type StatementBasis,
  type StatementRow,
  type StatementSection,
  type StatementValueFormat,
} from '@/screens/stock-page/statementsView'

/**
 * SCR-005 §3 — the statements view (TKT-foundation-095): the global controls &
 * filing metadata bar, the market & enterprise value header, and the full
 * income-statement / balance-sheet / cash-flow tables, all bound to the served
 * `/financials` payload of TKT-foundation-094.
 *
 * **The page computes nothing** (BR-RES-004, SCR-005 §6): `[R]` rows read a served
 * `lines` entry, `[C]` rows read a served `derived` value, and a row with no served
 * figure renders the honest no-data dash — never a zero, never a client-side margin,
 * ratio, sum or EV reading (UXR-RES-022, UXR-G-006). The period toggle is UI state
 * only; the periods themselves are served.
 */

/** One served figure in a table cell: the formatted value + restatement mark, or a dash. */
function StatementValue({
  figure,
  format,
  locale,
}: {
  figure: StatementFigure | null
  format: StatementValueFormat
  locale: string
}) {
  const { t } = useTranslation()
  if (figure === null || figure.value === null) {
    return (
      <span data-slot="no-value" title={t('honestData.noDataTitle')}>
        —
      </span>
    )
  }

  const value = figure.value
  return (
    <span className="tabular-nums">
      {format === 'percent'
        ? formatPercent(locale, value, 2)
        : formatNumber(locale, value, {
            maximumFractionDigits: format === 'perShare' ? 2 : 0,
          })}
      {figure.restated ? (
        <>
          {' '}
          <RestatedMarker />
        </>
      ) : null}
    </span>
  )
}

/** One served EV operand with its own as-of (mismatched bases stay visible). */
function EvValue({ fact, locale }: { fact: EvFact | null; locale: string }) {
  const { t } = useTranslation()
  if (fact === null || fact.value === null) {
    return (
      <span data-slot="no-value" title={t('honestData.noDataTitle')}>
        —
      </span>
    )
  }
  return (
    <span className="tabular-nums">
      {formatNumber(locale, fact.value, { maximumFractionDigits: 2 })}
      {fact.asOf ? (
        <span data-slot="ev-as-of" className="text-xs text-muted-foreground">
          {' '}
          {t('honestData.asOf', { date: formatAsOfDate(locale, fact.asOf) })}
        </span>
      ) : null}
    </span>
  )
}

/** The financial-sector layout notice — an honest verdict, never a half-filled table. */
function LayoutNoticeBlock({ notice }: { notice: LayoutNotice }) {
  const { t } = useTranslation()
  return (
    <div
      data-testid="layout-notice"
      data-state={notice.state}
      className="flex flex-col gap-1 rounded-md border bg-muted/40 p-3"
    >
      <p className="text-sm font-medium text-foreground">
        {t('stockPage.financials.layoutNotice.title')}
      </p>
      <p className="max-w-prose text-sm text-muted-foreground">
        {t('stockPage.financials.layoutNotice.body')}
      </p>
    </div>
  )
}

/** The served badges: each one renders only on its own served evidence. */
function FilingBadges({ filing }: { filing: StockFinancialsResponse['filing'] }) {
  const { t } = useTranslation()
  const badges = filingBadges(filing)
  if (badges.length === 0) return null
  return (
    <ul className="flex flex-wrap items-center gap-2" data-testid="filing-badges">
      {badges.map((badge) => (
        <li key={badge.id}>
          <Badge variant="outline" data-badge={badge.id}>
            {t(badge.labelKey)}
          </Badge>
        </li>
      ))}
    </ul>
  )
}

/** The market & enterprise value header: the served operands, nothing computed. */
function EnterpriseValueHeader({
  evHeader,
  locale,
}: {
  evHeader: NonNullable<StockFinancialsResponse['evHeader']>
  locale: string
}) {
  const { t } = useTranslation()
  const rows = evHeaderRows(evHeader)

  return (
    <section
      data-testid="ev-header"
      aria-label={t('stockPage.financials.ev.title')}
      className="flex flex-col gap-2"
    >
      <h3 className="text-sm font-semibold text-foreground">
        {t('stockPage.financials.ev.title')}
      </h3>
      <dl className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {rows.map((row) => (
          <div
            key={row.key}
            data-ev={row.key}
            className="flex flex-col gap-0.5 rounded-md border p-2"
          >
            <dt className="text-xs text-muted-foreground">{t(row.labelKey)}</dt>
            <dd className="text-sm font-medium text-foreground">
              <EvValue fact={row.fact} locale={locale} />
            </dd>
          </div>
        ))}
        {/*
          The total is a server fact (`enterpriseValue` is not served by
          TKT-foundation-094), so the row renders the honest dash rather than a
          client-side EV reading (BR-RES-004).
        */}
        <div
          data-ev="enterpriseValue"
          className="flex flex-col gap-0.5 rounded-md border p-2"
        >
          <dt className="text-xs text-muted-foreground">
            {t('stockPage.financials.ev.enterpriseValue')}
          </dt>
          <dd className="text-sm font-medium text-foreground">
            <span data-slot="no-value" title={t('honestData.noDataTitle')}>
              —
            </span>
          </dd>
        </div>
      </dl>
    </section>
  )
}

/** One statement table: rows are the served line set, columns are the periods. */
function StatementTable({
  section,
  periods,
  basis,
  asOf,
  locale,
}: {
  section: StatementSection
  periods: FinancialPeriod[]
  basis: StatementBasis
  asOf: string
  locale: string
}) {
  const { t } = useTranslation()

  const cell = (row: StatementRow, period: FinancialPeriod): ReactNode => (
    <TableCell key={period.periodEndDate} data-period={period.periodEndDate}>
      <StatementValue figure={rowFigure(period, row)} format={row.format} locale={locale} />
    </TableCell>
  )

  return (
    <div className="flex flex-col gap-2">
      <h4 className="text-sm font-semibold text-foreground">{t(section.titleKey)}</h4>
      <TableContainer>
        <Table data-statement={section.key}>
          <TableCaption>
            {t('stockPage.financials.tableCaption', {
              date: formatAsOfDate(locale, asOf),
            })}
          </TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead>{t('stockPage.financials.columns.period')}</TableHead>
              {periods.map((period) => (
                <TableHead key={period.periodEndDate} className="text-right">
                  {periodLabel(period, basis, t, locale)}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {section.groups.map((group) => (
              <Fragment key={group.key}>
                {group.labelKey ? (
                  <TableRow data-group={group.key}>
                    <TableHead
                      scope="rowgroup"
                      colSpan={periods.length + 1}
                      className="text-xs uppercase tracking-wide text-muted-foreground"
                    >
                      {t(group.labelKey)}
                    </TableHead>
                  </TableRow>
                ) : null}
                {group.rows.map((row) => (
                  <TableRow key={row.key} data-row={row.key}>
                    <TableHead scope="row" className="font-normal text-foreground">
                      {t(row.labelKey)}
                    </TableHead>
                    {periods.map((period) => cell(row, period))}
                  </TableRow>
                ))}
              </Fragment>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </div>
  )
}

/**
 * The statements view for a 094-shaped payload. A payload that carries no served
 * line set is a pre-094 payload: the caller renders the legacy five-figure summary
 * instead (never an empty statement skeleton).
 */
export function StatementsView({
  data,
  asOf,
  locale,
}: {
  data: StockFinancialsResponse
  asOf: string
  locale: string
}) {
  const { t } = useTranslation()
  const [basis, setBasis] = useStatementBasis()
  const notice = data.layoutNotice?.applies ? data.layoutNotice : null
  const periods = periodsForBasis(data.periods, basis)

  const options = [
    { value: 'annual' as const, labelKey: 'stockPage.financials.basis.annual' },
    { value: 'quarterly' as const, labelKey: 'stockPage.financials.basis.quarterly' },
  ]

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        {/* The basis toggle is table chrome: a financial-sector notice renders no
            tables, so the toggle would be an inert control. The served badges are
            facts about the filing and stay. */}
        {notice ? null : (
          <div
            data-testid="statements-controls"
            role="group"
            aria-label={t('stockPage.financials.basis.label')}
            className="flex flex-wrap items-center gap-2"
          >
            {options.map((option) => {
              const active = basis === option.value
              return (
                <button
                  key={option.value}
                  type="button"
                  data-basis={option.value}
                  aria-pressed={active}
                  onClick={() => setBasis(option.value)}
                  className={
                    active
                      ? 'rounded-md border border-primary bg-primary px-2.5 py-1 text-xs font-medium text-primary-foreground'
                      : 'rounded-md border bg-background px-2.5 py-1 text-xs font-medium text-muted-foreground hover:text-foreground'
                  }
                >
                  {t(option.labelKey)}
                </button>
              )
            })}
          </div>
        )}
        <FilingBadges filing={data.filing} />
      </div>

      {notice ? <LayoutNoticeBlock notice={notice} /> : null}

      {!notice && data.evHeader ? (
        <EnterpriseValueHeader evHeader={data.evHeader} locale={locale} />
      ) : null}

      {!notice ? (
        periods.length === 0 ? (
          <p data-testid="statements-empty" className="text-sm text-muted-foreground">
            {t('stockPage.financials.emptyBasis')}
          </p>
        ) : (
          STATEMENT_SECTIONS.map((section) => (
            <StatementTable
              key={section.key}
              section={section}
              periods={periods}
              basis={basis}
              asOf={asOf}
              locale={locale}
            />
          ))
        )
      ) : null}
    </div>
  )
}
