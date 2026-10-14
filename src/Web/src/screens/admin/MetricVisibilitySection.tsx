import { useState } from 'react'
import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState } from '@/components/ui/empty-state'
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import type { AdminMetricRow } from '@/lib/api/contracts'
import { useAdminMetrics, useToggleMetricVisibility } from '@/screens/admin/api'
import { familyKey } from '@/screens/admin/copy'
import { SectionError } from '@/screens/admin/SectionError'

/**
 * Screener metric-visibility section (SCR-012 §3.6; UXR-MDF-023/024, FR-SCR-017,
 * UC-SCR-004).
 *
 * Lists all 18 screenable metrics with their current visibility state and lets the
 * builder hide/unhide each one. A toggle is consequential — a hidden metric
 * disappears from SCR-003's criteria builder and saved screens referencing it report
 * the dropped criterion on re-run — so it is gated behind a confirmation that states
 * both consequences (UXR-MDF-024); cancel aborts with no change.
 */
export function MetricVisibilitySection() {
  const { t, i18n } = useTranslation()
  const language = i18n.resolvedLanguage ?? 'tr'
  const metrics = useAdminMetrics()
  const toggle = useToggleMetricVisibility()
  const [target, setTarget] = useState<AdminMetricRow | null>(null)

  const label = (metric: AdminMetricRow) =>
    language === 'en' ? metric.labelEn : metric.labelTr

  return (
    <section data-testid="metric-visibility" aria-label={t('admin.sections.metrics')}>
      <h2 className="mb-4 text-h3 font-semibold">{t('admin.sections.metrics')}</h2>

      {metrics.isPending ? (
        <p className="text-sm text-muted-foreground">{t('common.loading')}</p>
      ) : metrics.isError ? (
        <SectionError
          title={t('admin.metrics.errorTitle')}
          body={t('admin.metrics.errorBody')}
          onRetry={() => void metrics.refetch()}
        />
      ) : metrics.data.metrics.length === 0 ? (
        <EmptyState
          title={t('admin.metrics.emptyTitle')}
          description={t('admin.metrics.emptyBody')}
        />
      ) : (
        <TableContainer>
          <Table data-testid="metric-visibility-table">
            <TableHeader>
              <TableRow>
                <TableHead>{t('admin.metrics.columnMetric')}</TableHead>
                <TableHead>{t('admin.metrics.columnFamily')}</TableHead>
                <TableHead>{t('admin.metrics.columnState')}</TableHead>
                {/* Actions column: visually-hidden header keeps the cell/column
                    relationship for assistive tech (UXR-G-028). */}
                <TableHead className="sr-only">{t('admin.metrics.columnActions')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {metrics.data.metrics.map((metric) => (
                <TableRow key={metric.metricCode}>
                  <TableCell className="font-medium">{label(metric)}</TableCell>
                  <TableCell>{t(familyKey(metric.family))}</TableCell>
                  <TableCell>
                    {metric.isScreenable
                      ? t('admin.metrics.screenable')
                      : t('admin.metrics.hidden')}
                  </TableCell>
                  <TableCell>
                    <Button
                      type="button"
                      variant={metric.isScreenable ? 'outline' : 'default'}
                      size="sm"
                      onClick={() => {
                        toggle.reset()
                        setTarget(metric)
                      }}
                    >
                      {metric.isScreenable
                        ? t('admin.metrics.hide')
                        : t('admin.metrics.show')}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      <ConfirmDialog
        open={target !== null}
        onOpenChange={(open) => {
          if (!open) {
            setTarget(null)
            // Clear a prior failed toggle so reopening the dialog starts clean.
            toggle.reset()
          }
        }}
        title={t('admin.metrics.confirmTitle', {
          metric: target ? label(target) : '',
        })}
        description={t('admin.metrics.confirmDescription')}
        confirmLabel={
          target?.isScreenable
            ? t('admin.metrics.confirmHide')
            : t('admin.metrics.confirmShow')
        }
        cancelLabel={t('common.cancel')}
        closeLabel={t('common.close')}
        pending={toggle.isPending}
        error={toggle.isError ? t('admin.metrics.toggleError') : undefined}
        onConfirm={() => {
          if (!target) return
          toggle.mutate(
            { metricCode: target.metricCode, isScreenable: !target.isScreenable },
            { onSuccess: () => setTarget(null) },
          )
        }}
      />
    </section>
  )
}
