import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'

import { isApiErrorCode } from '@/lib/api/client'
import { Button, buttonVariants } from '@/components/ui/button'
import { FormField } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useRegister } from '@/screens/register/api'
import {
  isSiteRelativePath,
  PRIVACY_NOTICE_VERSION,
  validateRegistration,
} from '@/screens/register/validators'
import { useDraftStore } from '@/store/draftStore'

/**
 * SCR-007 — Register (FR-ACC-001/008/010; UXR-ACC-001..005, 021).
 *
 * Exactly three inputs — e-mail, password and privacy-notice consent — with
 * visible password rules and inline validation; the submit control is disabled
 * while invalid and locks while the mutation is pending (UXR-G-018/019/020). The
 * bilingual notice is reviewable before consent (UXR-ACC-002). On success the
 * user is signed in, shown the verification-e-mail notice stating that saving
 * requires verification, and offered the return to their preserved in-progress
 * work via `?returnUrl` + the draft store (UXR-ACC-005, UXR-G-024).
 */
export function RegisterScreen() {
  const { t } = useTranslation()
  const [searchParams] = useSearchParams()
  const rawReturnUrl = searchParams.get('returnUrl')
  // Only honour a site-relative target; a `//host` or `/\host` value would make
  // the post-auth link cross-origin (SF3).
  const returnUrl =
    rawReturnUrl && isSiteRelativePath(rawReturnUrl) ? rawReturnUrl : null
  const setReturnUrl = useDraftStore((state) => state.setReturnUrl)

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [consent, setConsent] = useState(false)
  const [touched, setTouched] = useState({ email: false, password: false })
  const [submitted, setSubmitted] = useState(false)

  const register = useRegister()
  const trimmedEmail = email.trim()
  const validity = useMemo(
    () => validateRegistration({ email, password, consent }),
    [email, password, consent],
  )

  // Preserve the return target across the auth hop (M-3, UXR-G-024).
  useEffect(() => {
    if (returnUrl) setReturnUrl(returnUrl)
  }, [returnUrl, setReturnUrl])

  const showEmailError = (touched.email || submitted) && !validity.email
  const showPasswordError = (touched.password || submitted) && !validity.password
  const showConsentError = submitted && !validity.consent

  function focusFirstInvalid() {
    const id = !validity.email
      ? 'register-email'
      : !validity.password
        ? 'register-password'
        : 'register-consent'
    document.getElementById(id)?.focus()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitted(true)
    if (!validity.valid) {
      focusFirstInvalid()
      return
    }
    register.mutate({ email: trimmedEmail, password, noticeVersion: PRIVACY_NOTICE_VERSION })
  }

  const emailTaken =
    register.isError && isApiErrorCode(register.error, 'EMAIL_TAKEN')

  if (register.isSuccess) {
    const target = returnUrl ?? '/'
    return (
      <section
        data-screen="SCR-007"
        className="mx-auto flex w-full max-w-md flex-col gap-4"
      >
        <h1 className="text-2xl font-semibold tracking-tight">
          {t('register.successTitle')}
        </h1>
        <div
          data-testid="registration-verification-notice"
          role="status"
          className="flex flex-col gap-1 rounded-md border bg-accent/40 p-4"
        >
          <h2 className="font-medium">{t('register.verificationSentTitle')}</h2>
          <p className="text-sm text-muted-foreground">
            {t('register.verificationSentBody', { email: trimmedEmail })}
          </p>
        </div>
        <Link className={buttonVariants()} to={target}>
          {returnUrl ? t('register.returnToWork') : t('register.noReturnUrl')}
        </Link>
      </section>
    )
  }

  return (
    <section
      data-screen="SCR-007"
      className="mx-auto flex w-full max-w-md flex-col gap-6"
    >
      <header className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">
          {t('screens.register.title')}
        </h1>
        <p className="text-muted-foreground">{t('screens.register.description')}</p>
      </header>

      <form
        aria-label={t('register.formAriaLabel')}
        className="flex flex-col gap-4"
        noValidate
        onSubmit={handleSubmit}
      >
        <FormField
          id="register-email"
          label={t('register.emailLabel')}
          required
          error={showEmailError ? t('register.emailInvalid') : undefined}
        >
          {(control) => (
            <Input
              {...control}
              type="email"
              autoComplete="email"
              autoFocus
              disabled={register.isPending}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              onBlur={() => setTouched((state) => ({ ...state, email: true }))}
            />
          )}
        </FormField>

        <FormField
          id="register-password"
          label={t('register.passwordLabel')}
          required
          hint={t('register.passwordRules')}
          error={showPasswordError ? t('register.passwordInvalid', { min: 10 }) : undefined}
        >
          {(control) => (
            <Input
              {...control}
              type="password"
              autoComplete="new-password"
              disabled={register.isPending}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              onBlur={() => setTouched((state) => ({ ...state, password: true }))}
            />
          )}
        </FormField>

        <section
          aria-labelledby="privacy-notice-title"
          className="flex flex-col gap-2 rounded-md border bg-muted/40 p-3"
        >
          <h2 id="privacy-notice-title" className="text-sm font-medium">
            {t('register.privacyNoticeTitle')}
          </h2>
          <p lang="tr" className="text-xs text-muted-foreground">
            {t('register.privacyNoticeTr')}
          </p>
          <p lang="en" className="text-xs text-muted-foreground">
            {t('register.privacyNoticeEn')}
          </p>
        </section>

        <FormField
          id="register-consent"
          label={t('register.consentLabel')}
          required
          error={showConsentError ? t('register.consentInvalid') : undefined}
        >
          {(control) => (
            <input
              {...control}
              type="checkbox"
              className="size-4 rounded border-input"
              disabled={register.isPending}
              checked={consent}
              onChange={(event) => setConsent(event.target.checked)}
            />
          )}
        </FormField>

        {register.isError ? (
          <div role="alert" className="flex flex-col gap-2 text-sm text-destructive">
            <p>{emailTaken ? t('register.emailTaken') : t('register.genericError')}</p>
            {emailTaken ? (
              <Link className="underline" to="/sign-in">
                {t('register.signInInstead')}
              </Link>
            ) : null}
          </div>
        ) : null}

        <Button type="submit" disabled={!validity.valid || register.isPending}>
          {register.isPending ? t('register.submitting') : t('register.submit')}
        </Button>
      </form>
    </section>
  )
}
