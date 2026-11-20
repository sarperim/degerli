import { useTranslation } from 'react-i18next'

import { SectionNav } from '@/components/ui/section-nav'
import { useIsBuilder, useSession } from '@/lib/auth/session'
import {
  ADMIN_SECTION_COMPONENTS,
  ADMIN_SECTION_KEYS,
  type AdminSectionId,
} from '@/screens/admin/adminSections'
import { AdminAccessDenied } from '@/screens/admin/AdminAccessDenied'
import { SectionError } from '@/screens/admin/SectionError'
import { useAdminUiStore } from '@/screens/admin/adminUiStore'

/**
 * SCR-012 Admin Dashboard shell (architecture AD-13; UX `market-data-foundation.md`
 * §3). Role-gated: a builder sees the five MDF sections; anyone else (regular
 * account or anonymous) sees the access-denied state (UXR-MDF-001/002). The active
 * section and per-section filters are session-preserved (UXR-G-021).
 *
 * Section content is contributed per-section via `ADMIN_SECTION_COMPONENTS`;
 * sibling admin tickets add their own section without touching this shell.
 */
export function AdminScreen() {
  const { t } = useTranslation()
  const session = useSession()
  const isBuilder = useIsBuilder()
  const activeSection = useAdminUiStore((state) => state.activeSection)
  const setActiveSection = useAdminUiStore((state) => state.setActiveSection)

  if (session.isPending) {
    return <p className="text-sm text-muted-foreground">{t('common.loading')}</p>
  }

  // A failed session fetch must not masquerade as access-denied (UXR-G-003);
  // surface a plain-language error with retry instead.
  if (session.isError) {
    return (
      <SectionError
        title={t('screens.admin.sessionErrorTitle')}
        body={t('screens.admin.sessionErrorBody')}
        onRetry={() => void session.refetch()}
      />
    )
  }

  if (!isBuilder) {
    return <AdminAccessDenied />
  }

  const items = (Object.keys(ADMIN_SECTION_KEYS) as AdminSectionId[]).map(
    (id, index) => ({
      id,
      index: String(index + 1).padStart(2, '0'),
      label: t(ADMIN_SECTION_KEYS[id]),
    }),
  )
  const ActiveSection = ADMIN_SECTION_COMPONENTS[activeSection]

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-col gap-1.5 border-b border-border pb-5">
        <h1 className="text-h1 font-semibold tracking-tight text-foreground">
          {t('screens.admin.title')}
        </h1>
        <p className="max-w-2xl text-sm text-muted-foreground">
          {t('screens.admin.description')}
        </p>
      </header>
      <SectionNav
        items={items}
        activeId={activeSection}
        onSelect={(id) => setActiveSection(id as AdminSectionId)}
        ariaLabel={t('admin.sectionsNavLabel')}
      />
      <ActiveSection />
    </div>
  )
}
