import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearchParams } from 'react-router'

import { isApiErrorCode } from '@/lib/api/client'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useLogin } from '@/screens/signIn/api'
import { isSiteRelativePath, validateSignIn } from '@/screens/signIn/validators'
import { useDraftStore } from '@/store/draftStore'

/**
 * SCR-008 — Sign In (FR-ACC-002/004/010; UXR-ACC-008..011).
 *
 * E-mail + password form with a generic credential error that never reveals which
 * field failed (UXR-ACC-008); the e-mail is preserved on failure (UXR-G-004), the
 * submit control is disabled while invalid and locked while the mutation is
 * pending (UXR-G-018/019/020). The screen links to password reset and to
 * registration (UXR-ACC-011). On success the account's stored language preference
 * is applied and the user returns to their preserved in-progress work via
 * `?returnUrl` (UXR-ACC-009, M-3/M-4).
 */
export function SignInScreen() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const rawReturnUrl = searchParams.get('returnUrl')
  // Only honour a site-relative target; a `//host` or `/\host` value would make
  // the post-auth redirect cross-origin (SF3).
  const returnUrl =
    rawReturnUrl && isSiteRelativePath(rawReturnUrl) ? rawReturnUrl : null
  const setReturnUrl = useDraftStore((state) => state.setReturnUrl)

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [touched, setTouched] = useState({ email: false, password: false })
  const [submitted, setSubmitted] = useState(false)

  const login = useLogin()
  const validity = useMemo(() => validateSignIn({ email, password }), [email, password])

  // Preserve the return target across the auth hop (M-3, UXR-G-024).
  useEffect(() => {
    if (returnUrl) setReturnUrl(returnUrl)
  }, [returnUrl, setReturnUrl])

  const showEmailError = (touched.email || submitted) && !validity.email
  const showPasswordError = (touched.password || submitted) && !validity.password

  function focusFirstInvalid() {
    const id = !validity.email ? 'sign-in-email' : 'sign-in-password'
    document.getElementById(id)?.focus()
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitted(true)
    if (!validity.valid) {
      focusFirstInvalid()
      return
    }
    login.mutate(
      { email: email.trim(), password },
      {
        onSuccess: (data) => {
          // Apply the account's stored preference (UXR-ACC-009) before returning.
          void i18n.changeLanguage(data.languagePref)
          navigate(returnUrl ?? '/', { replace: true })
        },
      },
    )
  }

  const lockedOut = login.isError && isApiErrorCode(login.error, 'LOCKED_OUT')
  const invalidCredentials =
    login.isError && isApiErrorCode(login.error, 'INVALID_CREDENTIALS')

  return (
    <section
      data-screen="SCR-008"
      className="mx-auto flex w-full max-w-md flex-col gap-6"
    >
      <header className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">
          {t('screens.signIn.title')}
        </h1>
        <p className="text-muted-foreground">{t('screens.signIn.description')}</p>
      </header>

      <form
        aria-label={t('signIn.formAriaLabel')}
        className="flex flex-col gap-4"
        noValidate
        onSubmit={handleSubmit}
      >
        <FormField
          id="sign-in-email"
          label={t('signIn.emailLabel')}
          required
          error={showEmailError ? t('signIn.emailInvalid') : undefined}
        >
          {(control) => (
            <Input
              {...control}
              type="email"
              autoComplete="email"
              autoFocus
              disabled={login.isPending}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              onBlur={() => setTouched((state) => ({ ...state, email: true }))}
            />
          )}
        </FormField>

        <FormField
          id="sign-in-password"
          label={t('signIn.passwordLabel')}
          required
          error={showPasswordError ? t('signIn.passwordRequired') : undefined}
        >
          {(control) => (
            <Input
              {...control}
              type="password"
              autoComplete="current-password"
              disabled={login.isPending}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              onBlur={() => setTouched((state) => ({ ...state, password: true }))}
            />
          )}
        </FormField>

        {login.isError ? (
          <p role="alert" className="text-sm text-destructive">
            {lockedOut
              ? t('signIn.lockedOut')
              : invalidCredentials
                ? t('signIn.invalidCredentials')
                : t('signIn.genericError')}
          </p>
        ) : null}

        <Button type="submit" disabled={!validity.valid || login.isPending}>
          {login.isPending ? t('signIn.submitting') : t('signIn.submit')}
        </Button>
      </form>

      <div className="flex flex-col gap-1 text-sm">
        <Link className="w-fit underline" to="/reset-password">
          {t('signIn.forgotPassword')}
        </Link>
        <p className="text-muted-foreground">
          {t('signIn.registerPrompt')}{' '}
          <Link className="underline" to="/register">
            {t('signIn.registerLink')}
          </Link>
        </p>
      </div>
    </section>
  )
}
