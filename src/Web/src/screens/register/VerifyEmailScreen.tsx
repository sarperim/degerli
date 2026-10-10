import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'

import { Button, buttonVariants } from '@/components/ui/button'
import { useResendVerification, useVerifyEmail } from '@/screens/register/api'

/**
 * SCR-007 — e-mail verification link landing (FR-ACC-008; UXR-ACC-006/021).
 *
 * The screen is reached from the verification e-mail with `?token=`. A valid
 * token shows the success notice; an expired/invalid one shows a plain-language
 * explanation with a resend path (UXR-ACC-021). On success the verify mutation
 * invalidates `['session']`, so an already-open session's blocked-save state
 * clears immediately — no re-authentication (M-9, UXR-G-030).
 */
export function VerifyEmailScreen() {
  const { t } = useTranslation()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token')

  const verify = useVerifyEmail()
  const resend = useResendVerification()
  const started = useRef(false)

  useEffect(() => {
    if (token && !started.current) {
      started.current = true
      verify.mutate(token)
    }
  }, [token, verify])

  const failed = !token || verify.isError

  return (
    <section
      data-screen="SCR-007"
      className="mx-auto flex w-full max-w-md flex-col gap-4"
    >
      <h1 className="text-2xl font-semibold tracking-tight">
        {t('verifyEmail.title')}
      </h1>

      {token && verify.isPending ? (
        <p role="status" className="text-muted-foreground">
          {t('verifyEmail.checking')}
        </p>
      ) : null}

      {verify.isSuccess ? (
        <div role="status" className="flex flex-col gap-1 rounded-md border bg-accent/40 p-4">
          <h2 className="font-medium">{t('verifyEmail.verifiedTitle')}</h2>
          <p className="text-sm text-muted-foreground">{t('verifyEmail.verifiedBody')}</p>
        </div>
      ) : null}

      {failed ? (
        <div role="alert" className="flex flex-col gap-3 rounded-md border p-4">
          <div className="flex flex-col gap-1">
            <h2 className="font-medium">{t('verifyEmail.failedTitle')}</h2>
            <p className="text-sm text-muted-foreground">{t('verifyEmail.failedBody')}</p>
          </div>

          {resend.isSuccess ? (
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
