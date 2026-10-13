import { useTranslation } from 'react-i18next'

import { EmptyState } from '@/components/ui/empty-state'
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
import { formatAsOfDate } from '@/lib/format'
import type {
  CoverageDataType,
  CoverageResponse,
  CoverageScope,
} from '@/lib/api/contracts'
import { useCoverage } from '@/screens/admin/api'
import { COVERAGE_SCOPE_KEYS, dataTypeKey, datePart } from '@/screens/admin/copy'
import { SectionError } from '@/screens/admin/SectionError'
import { useAdminUiStore } from '@/screens/admin/adminUiStore'

/**
 * Coverage report section (SCR-012 §3.3; UXR-MDF-011/012/013).
 *
 * Instrument and fund scopes, switchable; per entity it shows which data types
 * exist and from when, the listing date for instruments, and coverage gaps
 * explicitly — never omitted (FR-MDF-011, FR-FDF-004, NFR-MDF-003, NFR-FDF-001).
 */
export function CoverageSection() {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const scope = useAdminUiStore((state) => state.coverageScope)
  const setScope = useAdminUiStore((state) => state.setCoverageScope)
  const coverage = useCoverage(scope)

  return (
    <section data-testid="coverage" aria-label={t('admin.sections.coverage')}>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-h3 font-semibold">{t('admin.sections.coverage')}</h2>
        <fieldset className="flex items-center gap-3">
          <legend className="text-sm text-muted-foreground">
            {t('admin.coverage.scopeLabel')}
          </legend>
          {(['instrument', 'fund'] as CoverageScope[]).map((option) => (
            <label key={option} className="flex items-center gap-1.5 text-sm">
              <input
                type="radio"
                name="coverage-scope"
                value={option}
                checked={scope === option}
                onChange={() => setScope(option)}
              />
              {t(COVERAGE_SCOPE_KEYS[option])}
            </label>
          ))}
        </fieldset>
      </div>

      {coverage.isPending ? (
        <p className="text-sm text-muted-foreground">{t('common.loading')}</p>
      ) : coverage.isError ? (
        <SectionError
          title={t('admin.coverage.errorTitle')}
          body={t('admin.coverage.errorBody')}
          onRetry={() => void coverage.refetch()}
        />
      ) : isEmpty(coverage.data) ? (
        <EmptyState
          title={t('admin.coverage.emptyTitle')}
          description={t('admin.coverage.emptyBody')}
        />
      ) : (
        <CoverageTable data={coverage.data} locale={locale} />
      )}
    </section>
  )
}

function isEmpty(data: CoverageResponse): boolean {
  return data.scope === 'instrument'
    ? data.instruments.length === 0
    : data.funds.length === 0
}

function CoverageTable({
  data,
  locale,
}: {
  data: CoverageResponse
  locale: string
}) {
  const { t } = useTranslation()

  if (data.scope === 'instrument') {
    return (
      <TableContainer>
        <Table data-testid="coverage-instrument-table">
          <TableCaption>{t('admin.coverage.instrument')}</TableCaption>
          <TableHeader>
            <TableRow>
              <TableHead>{t('admin.coverage.instrument')}</TableHead>
              <TableHead>{t('admin.coverage.listingDate')}</TableHead>
              <TableHead>{t('admin.coverage.dataTypes')}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.instruments.map((row) => (
              <TableRow key={row.symbol}>
                <TableCell className="font-medium">{row.symbol}</TableCell>
                <TableCell>
                  {row.listingDate ? formatAsOfDate(locale, datePart(row.listingDate)) : '—'}
                </TableCell>
                <TableCell>
                  <DataTypeList dataTypes={row.dataTypes} locale={locale} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    )
  }

  return (
    <TableContainer>
      <Table data-testid="coverage-fund-table">
        <TableCaption>{t('admin.coverage.fund')}</TableCaption>
        <TableHeader>
          <TableRow>
            <TableHead>{t('admin.coverage.fund')}</TableHead>
            <TableHead>{t('admin.coverage.dataTypes')}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {data.funds.map((row) => (
            <TableRow key={row.code}>
              <TableCell className="font-medium">{row.code}</TableCell>
              <TableCell>
                <DataTypeList dataTypes={row.dataTypes} locale={locale} />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  )
}

function DataTypeList({
  dataTypes,
  locale,
}: {
  dataTypes: CoverageDataType[]
  locale: string
}) {
  const { t } = useTranslation()
  return (
    <ul className="flex flex-col gap-1">
      {dataTypes.map((entry) => (
        <li key={entry.dataType} className="flex flex-wrap items-center gap-2 text-xs">
          <span className="font-medium">{t(dataTypeKey(entry.dataType))}</span>
          <span
            className={
              entry.present ? 'text-muted-foreground' : 'font-medium text-destructive'
            }
          >
            {entry.present ? t('admin.coverage.present') : t('admin.coverage.gap')}
          </span>
          {entry.present && entry.availableFrom ? (
            <span className="text-muted-foreground">
              {formatAsOfDate(locale, datePart(entry.availableFrom))}
            </span>
          ) : null}
          {!entry.present && entry.notes ? (
            <span className="text-muted-foreground">{entry.notes}</span>
          ) : null}
        </li>
      ))}
    </ul>
  )
}
