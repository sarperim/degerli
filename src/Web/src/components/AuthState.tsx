import { useTranslation } from 'react-i18next'

import { Button } from '@/components/ui/button'
import { useSession } from '@/lib/auth/session'
import { useLogout } from '@/screens/signIn/api'

/**
 * Header authentication-state area (FR-ACC-002/003; UXR-ACC-010).
 *
 * Reads the shared session query so the header reflects the authenticated state
 * on every screen and flips to anonymous immediately after sign-out (M-9,
 * mutation matrix). While signed in it renders the sign-out control; the sign-out
 * mutation revokes the session server-side and invalidates `['session']`, while
 * the device-local language selection is left untouched (UXR-ACC-010).
 *
 * The anonymous sign-in/register entry points navigate to their routes. They are
 * plain controls (not `Link`s) so the header stays router-independent — it renders
 * in isolation in component tests as well as inside the shell.
 */
export function AuthState() {
  const { t } = useTranslation()
  const { data } = useSession()
  const logout = useLogout()

  if (data?.authenticated) {
    return (
      <div className="flex shrink-0 items-center gap-2 whitespace-nowrap text-xs">
        <span
          data-testid="auth-email"
          className="hidden max-w-[12rem] truncate text-xs text-muted-foreground sm:inline"
        >
          {data.email}
        </span>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={logout.isPending}
          onClick={() => logout.mutate()}
        >
          {t('header.signOut')}
        </Button>
      </div>
    )
  }

  return (
    <div className="flex shrink-0 items-center gap-2 whitespace-nowrap text-xs">
      <span className="hidden text-xs text-muted-foreground sm:inline">
        {t('header.anonymous')}
      </span>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        onClick={() => window.location.assign('/sign-in')}
      >
        {t('header.signIn')}
      </Button>
      <Button
        type="button"
        size="sm"
        onClick={() => window.location.assign('/register')}
      >
        {t('header.register')}
      </Button>
    </div>
  )
}
