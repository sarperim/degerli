import { useTranslation } from 'react-i18next'

import {
  StaleBadge,
} from '@/components/honest-data/markers'
import { Button } from '@/components/ui/button'
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { formatAsOfDate } from '@/lib/format'
import { useAdminSummary } from '@/screens/admin/api'
import { datePart, dataTypeKey, runStatusKey } from '@/screens/admin/copy'
import { SectionError } from '@/screens/admin/SectionError'
import { useAdminUiStore } from '@/screens/admin/adminUiStore'

/**
 * Ops summary section (SCR-012 §3.1; UXR-MDF-003/004/005/006/028).
 *
 * Per-job last run (status + completion time), per-data-type freshness with an
 * explicit stale marker for last-known-good types, description coverage counts,
 * and the open-quarantine count linking into the quarantine queue. Manual refresh
 * only — the summary deliberately does not auto-refresh (UXR-MDF-015/028).
 */
export function OpsSummarySection() {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage ?? 'tr'
  const summary = useAdminSummary()
  const setActiveSection = useAdminUiStore((state) => state.setActiveSection)

  return (
    <section data-testid="ops-summary" aria-label={t('admin.sections.summary')}>
      <div className="mb-4 flex items-center justify-between gap-3">
        <h2 className="text-h3 font-semibold">{t('admin.sections.summary')}</h2>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => void summary.refetch()}
        >
          {t('admin.summary.refresh')}
        </Button>
      </div>

      {summary.isPending ? (
        <p className="text-sm text-muted-foreground">{t('common.loading')}</p>
      ) : summary.isError ? (
        <SectionError
          title={t('admin.summary.errorTitle')}
          body={t('admin.summary.errorBody')}
          onRetry={() => void summary.refetch()}
        />
      ) : (
        <div className="flex flex-col gap-8">
          <div>
            <h3 className="mb-2 text-base font-medium">
              {t('admin.summary.jobsHeading')}
            </h3>
            <TableContainer>
              <Table data-testid="summary-jobs-table">
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('admin.summary.job')}</TableHead>
                    <TableHead>{t('admin.summary.status')}</TableHead>
                    <TableHead>{t('admin.summary.finishedAt')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {summary.data.jobs.map((job) => (
                    <TableRow key={job.job}>
                      <TableCell className="font-medium">{job.job}</TableCell>
                      <TableCell>
                        {job.lastRun
                          ? t(runStatusKey(job.lastRun.status))
                          : t('admin.summary.neverRun')}
                      </TableCell>
                      <TableCell>
                        {job.lastRun?.finishedAt
                          ? formatAsOfDate(locale, datePart(job.lastRun.finishedAt))
                          : '—'}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </div>

          <div>
            <h3 className="mb-2 text-base font-medium">
              {t('admin.summary.freshnessHeading')}
            </h3>
            <TableContainer>
              <Table data-testid="summary-freshness-table">
                <TableHeader>
                  <TableRow>
                    <TableHead>{t('admin.summary.dataType')}</TableHead>
                    <TableHead>{t('admin.summary.lastSuccess')}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {summary.data.freshness.map((row) => (
                    <TableRow key={row.dataType}>
                      <TableCell className="font-medium">
                        {t(dataTypeKey(row.dataType))}
                      </TableCell>
                      <TableCell>
                        <span className="inline-flex items-center gap-2">
                          {row.lastSuccess
                            ? formatAsOfDate(locale, datePart(row.lastSuccess))
                            : t('admin.summary.noLastSuccess')}
                          {row.stale ? <StaleBadge /> : null}
                        </span>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </div>

          <div>
            <h3 className="mb-2 text-base font-medium">
              {t('admin.summary.contentHeading')}
            </h3>
            <dl className="flex flex-wrap gap-6 text-sm">
              <div>
                <dt className="text-muted-foreground">
                  {t('admin.summary.descriptionsPublished')}
                </dt>
                <dd data-testid="coverage-count-published" className="text-lg tabular-nums">
                  {summary.data.contentCoverage.descriptionsPublished}
                </dd>
              </div>
              <div>
                <dt className="text-muted-foreground">
                  {t('admin.summary.descriptionsDraft')}
                </dt>
                <dd data-testid="coverage-count-draft" className="text-lg tabular-nums">
                  {summary.data.contentCoverage.descriptionsDraft}
                </dd>
              </div>
              <div>
                <dt className="text-muted-foreground">{t('admin.summary.universe')}</dt>
                <dd data-testid="coverage-count-universe" className="text-lg tabular-nums">
                  {summary.data.contentCoverage.universe}
                </dd>
              </div>
            </dl>
          </div>

          <div>
            <h3 className="mb-2 text-base font-medium">
              {t('admin.summary.quarantineHeading')}
            </h3>
            <p className="flex items-center gap-2 text-sm">
              <span data-testid="open-quarantine-count" className="text-lg tabular-nums">
                {summary.data.openQuarantineCount}
              </span>
              <button
                type="button"
                className="text-sm font-medium text-primary underline-offset-4 hover:underline"
                onClick={() => setActiveSection('quarantine')}
              >
                {t('admin.summary.openQuarantineLink')}
              </button>
            </p>
          </div>
        </div>
      )}
    </section>
  )
}
