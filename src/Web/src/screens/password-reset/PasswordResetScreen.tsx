import { useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'

import { isApiErrorCode } from '@/lib/api/client'
import { Button, buttonVariants } from '@/components/ui/button'
import { FormField } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useForgotPassword, useResetPassword } from '@/screens/password-reset/api'
import {
  isValidEmail,
  isValidPassword,
  PASSWORD_MIN_LENGTH,
} from '@/screens/register/validators'

/**
 * SCR-009 — Password Reset (FR-ACC-009; UC-ACC-004 main + alternate a;
 * UXR-ACC-012..015).
 *
 * A single route with two stages, distinguished by the presence of the e-mailed
 * `?token=`:
 *
 * - **Request stage** (no token): an e-mail input submits the reset request. The
 *   confirmation shown afterwards is fixed, neutral copy that never signals
 *   whether the address is registered (UXR-ACC-012).
 * - **Set-password stage** (token present): the screen is directly reachable from
 *   the e-mailed link — an external entry point, not only in-app navigation. The
 *   new password enforces the policy with inline validation (UXR-ACC-013); on an
 *   expired/invalid token (410 `TOKEN_EXPIRED`) a plain-language explanation with
 *   a re-request path replaces the form (UXR-ACC-014); on success the reset
 *   establishes a signed-in state (UXR-ACC-015).
 *
 * Fields are labelled, rules and errors are textual, and focus lands on the first
 * field of the active stage (UXR-G-027/028).
 */
export function PasswordResetScreen() {
  const { t } = useTranslation()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token') ?? ''

  return (
    <section
      data-screen="SCR-009"
      className="mx-auto flex w-full max-w-md flex-col gap-6"
    >
      <header className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">
          {t('screens.passwordReset.title')}
        </h1>
      </header>

      {token ? <SetPasswordStage token={token} /> : <RequestStage />}
    </section>
  )
}

/**
 * Request stage (SCR-009 §3.1): the e-mail input and the enumeration-neutral
 * confirmation (UXR-ACC-012). The trigger is disabled while the address is
 * invalid and locked while the mutation is pending; a dispatch failure shows a
 * plain-language error and preserves the input (UXR-G-004).
 */
function RequestStage() {
  const { t } = useTranslation()

  const [email, setEmail] = useState('')
  const [touched, setTouched] = useState(false)
  const [submitted, setSubmitted] = useState(false)

  const forgot = useForgotPassword()
  const trimmedEmail = email.trim()
  const emailValid = isValidEmail(email)

  const showEmailError = (touched || submitted) && !emailValid

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitted(true)
    if (!emailValid) {
      document.getElementById('password-reset-email')?.focus()
      return
    }
    forgot.mutate(trimmedEmail)
  }

  if (forgot.isSuccess) {
    return (
      <div
        data-testid="password-reset-confirmation"
        role="status"
        className="flex flex-col gap-3 rounded-md border bg-accent/40 p-4"
      >
        <h2 className="font-medium">{t('passwordReset.neutralTitle')}</h2>
        {/* Fixed, address-independent copy — never reveals whether the address
            is registered (UXR-ACC-012). */}
        <p className="text-sm text-muted-foreground">
          {t('passwordReset.neutralBody')}
        </p>
        <Link className={buttonVariants({ variant: 'outline' })} to="/sign-in">
          {t('passwordReset.backToSignIn')}
        </Link>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <p className="text-muted-foreground">{t('passwordReset.requestDescription')}</p>

      <form
        aria-label={t('passwordReset.requestFormAriaLabel')}
        className="flex flex-col gap-4"
        noValidate
        onSubmit={handleSubmit}
      >
        <FormField
          id="password-reset-email"
          label={t('passwordReset.emailLabel')}
          required
          error={showEmailError ? t('passwordReset.emailInvalid') : undefined}
        >
          {(control) => (
            <Input
              {...control}
              type="email"
              autoComplete="email"
              autoFocus
              disabled={forgot.isPending}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              onBlur={() => setTouched(true)}
            />
          )}
        </FormField>

        {forgot.isError ? (
          <p role="alert" className="text-sm text-destructive">
            {t('passwordReset.requestGenericError')}
          </p>
        ) : null}

        <Button type="submit" disabled={!emailValid || forgot.isPending}>
          {forgot.isPending
            ? t('passwordReset.requestSubmitting')
            : t('passwordReset.requestSubmit')}
        </Button>
      </form>
    </div>
  )
}

/**
 * Set-password stage (SCR-009 §3.2): reached only via the e-mailed link. Enforces
 * the password policy inline (UXR-ACC-013); an expired/invalid token shows the
 * explanation + re-request path (UXR-ACC-014); success signs the user in
 * (UXR-ACC-015). Input is preserved on failure (UXR-G-004).
 */
function SetPasswordStage({ token }: { token: string }) {
  const { t } = useTranslation()

  const [password, setPassword] = useState('')
  const [touched, setTouched] = useState(false)
  const [submitted, setSubmitted] = useState(false)

  const reset = useResetPassword()
  const passwordValid = useMemo(() => isValidPassword(password), [password])

  const showPasswordError = (touched || submitted) && !passwordValid
  const expired = reset.isError && isApiErrorCode(reset.error, 'TOKEN_EXPIRED')

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitted(true)
    if (!passwordValid) {
      document.getElementById('password-reset-password')?.focus()
      return
    }
    reset.mutate({ token, newPassword: password })
  }

  if (reset.isSuccess) {
    return (
      <div
        data-testid="password-reset-success"
        role="status"
        className="flex flex-col gap-3 rounded-md border bg-accent/40 p-4"
      >
        <h2 className="font-medium">{t('passwordReset.successTitle')}</h2>
        <p className="text-sm text-muted-foreground">
          {t('passwordReset.successBody')}
        </p>
        <Link className={buttonVariants()} to="/">
          {t('passwordReset.continueToApp')}
        </Link>
      </div>
    )
  }

  if (expired) {
    return (
      <div
        data-testid="password-reset-expired"
        role="alert"
        className="flex flex-col gap-3 rounded-md border p-4"
      >
        <div className="flex flex-col gap-1">
          <h2 className="font-medium">{t('passwordReset.expiredTitle')}</h2>
          <p className="text-sm text-muted-foreground">
            {t('passwordReset.expiredBody')}
          </p>
        </div>
        <Link className={buttonVariants()} to="/reset-password">
          {t('passwordReset.requestNewLink')}
        </Link>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <p className="text-muted-foreground">{t('passwordReset.setDescription')}</p>

      <form
        aria-label={t('passwordReset.setFormAriaLabel')}
        className="flex flex-col gap-4"
        noValidate
        onSubmit={handleSubmit}
      >
        <FormField
          id="password-reset-password"
          label={t('passwordReset.passwordLabel')}
          required
          hint={t('passwordReset.passwordRules')}
          error={
            showPasswordError
              ? t('passwordReset.passwordInvalid', { min: PASSWORD_MIN_LENGTH })
              : undefined
          }
        >
          {(control) => (
            <Input
              {...control}
              type="password"
              autoComplete="new-password"
              autoFocus
              disabled={reset.isPending}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              onBlur={() => setTouched(true)}
            />
          )}
        </FormField>

        {reset.isError && !expired ? (
          <p role="alert" className="text-sm text-destructive">
            {t('passwordReset.setGenericError')}
          </p>
        ) : null}

        <Button type="submit" disabled={!passwordValid || reset.isPending}>
          {reset.isPending
            ? t('passwordReset.setSubmitting')
            : t('passwordReset.setSubmit')}
        </Button>
      </form>
    </div>
  )
}
