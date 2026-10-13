import { useTranslation } from 'react-i18next'

import { useAdminStats } from '@/screens/admin/api'
import { SectionError } from '@/screens/admin/SectionError'

/**
 * Aggregate stats panel (SCR-012 §3.8; UXR-MDF-026).
 *
 * Aggregate counts only — registered/verified accounts, saved screens and DCF
 * scenarios. No per-user or per-account data of any kind is ever displayed
 * (SC-008/OBJ-005, BR-ACC-002/008).
 */
export function StatsSection() {
  const { t } = useTranslation()
  const stats = useAdminStats()

  return (
    <section data-testid="admin-stats" aria-label={t('admin.sections.stats')}>
      <h2 className="mb-4 text-h3 font-semibold">{t('admin.sections.stats')}</h2>

      {stats.isPending ? (
        <p className="text-sm text-muted-foreground">{t('common.loading')}</p>
      ) : stats.isError ? (
        <SectionError
          title={t('admin.stats.errorTitle')}
          body={t('admin.stats.errorBody')}
          onRetry={() => void stats.refetch()}
        />
      ) : (
        <div className="flex flex-col gap-3">
          <dl className="flex flex-wrap gap-8 text-sm">
            <Stat label={t('admin.stats.registered')} value={stats.data.accounts.registered} />
            <Stat label={t('admin.stats.verified')} value={stats.data.accounts.verified} />
            <Stat label={t('admin.stats.savedScreens')} value={stats.data.savedScreens} />
            <Stat label={t('admin.stats.dcfScenarios')} value={stats.data.dcfScenarios} />
          </dl>
          <p className="text-xs text-muted-foreground">{t('admin.stats.aggregateNote')}</p>
        </div>
      )}
    </section>
  )
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-lg tabular-nums">{value}</dd>
    </div>
  )
}
