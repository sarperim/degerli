import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'

import { isApiErrorCode } from '@/lib/api/client'
import { useSession } from '@/lib/auth/session'
import { Button, buttonVariants } from '@/components/ui/button'
import { useResendVerification, useVerifyEmail } from '@/screens/register/api'
import { peekPendingVerificationToken } from '@/screens/register/pendingVerificationToken'

/**
 * SCR-007 — e-mail verification link landing (FR-ACC-008; UXR-ACC-006/021).
 *
 * The screen is reached from the verification e-mail with `?token=`. A valid
 * token shows the success notice; an expired/invalid one shows a plain-language
 * explanation with a resend path (UXR-ACC-021). On success the verify mutation
 * invalidates `['session']`, so an already-open session's blocked-save state
 * clears immediately — no re-authentication (M-9, UXR-G-030).
 *
 * `verify-email` is session-scoped on the API, so a valid link opened without a
 * session answers 401 `UNAUTHENTICATED` rather than 410 `TOKEN_EXPIRED` (SF2):
 * that state directs the user to sign in instead of offering a resend that would
 * itself dead-end unauthenticated, while holding the token only in memory so
 * returning to `/verify-email` after sign-in completes verification automatically.
 */
export function VerifyEmailScreen() {
  const { t } = useTranslation()
  const [searchParams] = useSearchParams()
  const urlToken = searchParams.get('token')
  const token = urlToken ?? peekPendingVerificationToken()

  const session = useSession()
  const verify = useVerifyEmail()
  const resend = useResendVerification()
  const startedFor = useRef<string | null>(null)

  useEffect(() => {
    if (token && startedFor.current !== token) {
      startedFor.current = token
      verify.mutate(token)
    }
  }, [token, verify])

  // Drop the single-use token from the address bar once read, so it does not
  // linger in history or a shared URL (S3). `replaceState` bypasses router state,
  // so the verification above still sees the token.
  useEffect(() => {
    if (!urlToken) return
    const url = new URL(window.location.href)
    if (!url.searchParams.has('token')) return
    url.searchParams.delete('token')
    window.history.replaceState(
      window.history.state,
      '',
      `${url.pathname}${url.search}${url.hash}`,
    )
  }, [urlToken])

  const unauthenticated = isApiErrorCode(verify.error, 'UNAUTHENTICATED')
  const rateLimited = isApiErrorCode(verify.error, 'RATE_LIMITED')
  const alreadyVerified =
    !token && session.data?.authenticated === true && session.data.verified === true
  const verified = verify.isSuccess || alreadyVerified
  const checking =
    (Boolean(token) && verify.isPending) ||
    (!token && !verify.isError && session.isPending)
  const failed = !verified && !checking && (!token || verify.isError)

  return (
    <section
      data-screen="SCR-007"
      className="mx-auto flex w-full max-w-[450px] flex-col gap-6 rounded-[2px] border border-[var(--border-strong)] bg-card p-6 text-card-foreground"
    >
      <h1 className="text-2xl font-semibold tracking-tight">
        {t('verifyEmail.title')}
      </h1>

      {checking ? (
        <p role="status" className="text-muted-foreground">
          {t('verifyEmail.checking')}
        </p>
      ) : null}

      {verified ? (
        <div
          role="status"
          className="flex flex-col gap-3 border-l-2 border-[var(--brand-accent)] bg-muted/30 p-3"
        >
          <div className="flex flex-col gap-1">
            <h2 className="font-medium">{t('verifyEmail.verifiedTitle')}</h2>
            <p className="text-sm text-muted-foreground">{t('verifyEmail.verifiedBody')}</p>
          </div>
          <Link className={buttonVariants()} to="/">
            {t('verifyEmail.continueToApp')}
          </Link>
        </div>
      ) : null}

      {failed ? (
        <div
          role="alert"
          className="flex flex-col gap-3 border-l-2 border-[var(--brand-accent)] bg-muted/30 p-3"
        >
          <div className="flex flex-col gap-1">
            <h2 className="font-medium">
              {unauthenticated
                ? t('verifyEmail.unauthenticatedTitle')
                : rateLimited
                  ? t('verifyEmail.rateLimitedTitle')
                  : t('verifyEmail.failedTitle')}
            </h2>
            <p className="text-sm text-muted-foreground">
              {unauthenticated
                ? t('verifyEmail.unauthenticatedBody')
                : rateLimited
                  ? t('verifyEmail.rateLimitedBody')
                  : t('verifyEmail.failedBody')}
            </p>
          </div>

          {unauthenticated ? (
            <Link className={buttonVariants()} to="/sign-in?returnUrl=%2Fverify-email">
              {t('verifyEmail.signIn')}
            </Link>
          ) : rateLimited ? (
            <Button
              type="button"
              variant="outline"
              disabled={verify.isPending}
              onClick={() => {
                if (token) verify.mutate(token)
              }}
            >
              {t('verifyEmail.retry')}
            </Button>
          ) : resend.isSuccess ? (
            <p role="status" className="text-sm text-muted-foreground">
              {t('verifyEmail.resent')}
            </p>
          ) : (
            <div className="flex flex-col gap-1">
              <Button
                type="button"
                variant="outline"
                disabled={resend.isPending}
                onClick={() => resend.mutate()}
              >
                {resend.isPending ? t('verifyEmail.resending') : t('verifyEmail.resend')}
              </Button>
              {resend.isError ? (
                <p className="text-sm text-destructive">{t('verifyEmail.resendFailed')}</p>
              ) : null}
            </div>
          )}

          <Link className={buttonVariants({ variant: 'ghost' })} to="/register">
            {t('verifyEmail.backToRegister')}
          </Link>
        </div>
      ) : null}
    </section>
  )
}
