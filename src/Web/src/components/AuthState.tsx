import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'

/**
 * Authentication-state placeholder for the shell (FR-ACC-002/003).
 *
 * Signed-in state, role gating and session invalidation arrive with the
 * user-accounts tickets (TKT-acc-*); this scaffold renders the anonymous
 * state and sign-in/register entry points so every screen has the header slot.
 */
export function AuthState() {
  const { t } = useTranslation()

  return (
    <div className="flex items-center gap-2">
      <span className="hidden text-sm text-muted-foreground sm:inline">
        {t('header.anonymous')}
      </span>
      <Button type="button" variant="ghost" size="sm">
        {t('header.signIn')}
      </Button>
      <Button type="button" size="sm">
        {t('header.register')}
      </Button>
    </div>
  )
}
