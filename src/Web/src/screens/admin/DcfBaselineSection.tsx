import { useState } from 'react'
import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { useRegenerateDcfBaselines } from '@/screens/admin/api'

/**
 * DCF baseline regeneration section (SCR-012; UXR-MDF-025, UC-VAL-003; TKT-val-009).
 *
 * Baselines are derived rows, never hand-edited content (`02` §5.8): regeneration
 * re-derives every covered stock's baseline from canonical facts and bumps the
 * version — one active version per stock, prior versions retained (BR-VAL-008).
 *
 * The action is consequential, so it runs behind a confirmation that states plainly
 * that baselines are rebuilt from canonical facts and versioned (UXR-MDF-025). Cancel
 * aborts with no run; confirm triggers the C3b job, and the resulting run appears in
 * the run ledger without a manual refresh (the mutation invalidates the ledger query).
 */
export function DcfBaselineSection() {
  const { t } = useTranslation()
  const regenerate = useRegenerateDcfBaselines()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [succeeded, setSucceeded] = useState(false)

  const openConfirm = () => {
    regenerate.reset()
    setSucceeded(false)
    setConfirmOpen(true)
  }

  return (
    <section
      data-testid="admin-dcf-baselines"
      aria-label={t('admin.sections.dcfBaselines')}
    >
      <h2 className="mb-4 text-h3 font-semibold">{t('admin.dcfBaselines.heading')}</h2>
      <p className="mb-4 max-w-prose text-sm text-muted-foreground">
        {t('admin.dcfBaselines.explanation')}
      </p>

      <Button type="button" onClick={openConfirm}>
        {t('admin.dcfBaselines.regenerate')}
      </Button>

      {succeeded ? (
        <p
          role="status"
          data-testid="dcf-baselines-success"
          className="mt-4 max-w-prose text-sm"
        >
          <strong>{t('admin.dcfBaselines.successTitle')}</strong>{' '}
          {t('admin.dcfBaselines.successBody')}
        </p>
      ) : null}

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={(open) => {
          setConfirmOpen(open)
          if (!open) regenerate.reset()
        }}
        title={t('admin.dcfBaselines.confirmTitle')}
        description={t('admin.dcfBaselines.confirmDescription')}
        confirmLabel={t('admin.dcfBaselines.confirm')}
        cancelLabel={t('admin.dcfBaselines.cancel')}
        closeLabel={t('common.close')}
        pending={regenerate.isPending}
        error={regenerate.isError ? t('admin.dcfBaselines.errorBody') : undefined}
        onConfirm={() =>
          regenerate.mutate(undefined, {
            onSuccess: () => {
              setConfirmOpen(false)
              setSucceeded(true)
            },
          })
        }
      />
    </section>
  )
}
