import { useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'

import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FormField } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { SkeletonText } from '@/components/ui/skeleton'
import { ApiError } from '@/lib/api/client'
import type { LanguagePref } from '@/lib/api/contracts'
import { useSession } from '@/lib/auth/session'
import { SUPPORTED_LANGUAGES, type SupportedLanguage } from '@/i18n'
import {
  useChangePassword,
  useDeleteAccount,
  useMe,
  useUpdateLanguagePreference,
} from '@/screens/accountSettings/api'
import {
  PASSWORD_MIN_LENGTH,
  validatePasswordChange,
  type ValidationField,
} from '@/screens/accountSettings/validators'
import { useResendVerification } from '@/screens/register/api'

/**
 * SCR-010 — Account Settings (FR-ACC-005/006/007; UC-ACC-003;
 * UXR-ACC-016..021).
 *
 * Exactly the five documented sections and nothing else (UXR-ACC-016):
 * language preference (immediate + persisted, UXR-ACC-017), password change
 * (inline validation, session preserved, UXR-ACC-018), entry points to saved
 * screens and DCF scenarios, account deletion behind a focus-trapping
 * confirmation (UXR-ACC-019), and the e-mail verification status with the
 * resend action while unverified (UXR-ACC-021).
 *
 * Signed-in only: an anonymous direct visit presents the sign-in path
 * (UXR-ACC-020). The screen's server state is the minimal account read
 * (`GET /me`).
 */
export function AccountSettingsScreen() {
  const { t } = useTranslation()
  const session = useSession()
  const authenticated = session.data?.authenticated === true
  const me = useMe(authenticated)

  return (
    <section
      data-screen="SCR-010"
      data-route-id="accountSettings"
      className="mx-auto flex w-full max-w-2xl flex-col gap-6"
    >
      <header className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">
          {t('screens.accountSettings.title')}
        </h1>
        <p className="text-muted-foreground">
          {t('screens.accountSettings.description')}
        </p>
      </header>

      {session.isPending ? (
        <LoadingState />
      ) : !authenticated ? (
        <AnonymousState />
      ) : me.isPending ? (
        <LoadingState />
      ) : me.isError || !me.data ? (
        <LoadErrorState onRetry={() => void me.refetch()} />
      ) : (
        <div className="flex flex-col gap-8">
          <LanguageSection
            current={currentLanguage(me.data.languagePref)}
          />
          <PasswordSection />
          <EntryPointsSection />
          <DeletionSection />
          <VerificationSection
            email={me.data.email}
            verified={me.data.verified}
          />
        </div>
      )}
    </section>
  )
}

/** Resolve the UI language from the account preference (TR default). */
function currentLanguage(pref: LanguagePref): SupportedLanguage {
  return pref === 'en' ? 'en' : 'tr'
}

function LoadingState() {
  const { t } = useTranslation()
  return (
    <div role="status" aria-label={t('accountSettings.loading')}>
      <SkeletonText lines={4} />
    </div>
  )
}

function LoadErrorState({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation()
  return (
    <div
      role="alert"
      data-testid="settings-load-error"
      className="flex flex-col items-start gap-2 rounded-lg border border-dashed p-4"
    >
      <p className="font-medium">{t('accountSettings.loadError')}</p>
      <Button type="button" variant="outline" size="sm" onClick={onRetry}>
        {t('common.retry')}
      </Button>
    </div>
  )
}

/* ------------------------------------------------------------------ *
 * 1. Language preference (UXR-ACC-017).
 * ------------------------------------------------------------------ */

function LanguageSection({ current }: { current: SupportedLanguage }) {
  const { t, i18n } = useTranslation()
  const update = useUpdateLanguagePreference()
  const active: SupportedLanguage =
    i18n.resolvedLanguage === 'en' ? 'en' : 'tr'

  const labelFor: Record<SupportedLanguage, string> = {
    tr: t('header.turkish'),
    en: t('header.english'),
  }

  function changeLanguage(next: SupportedLanguage) {
    if (next === active) return
    // Optimistic immediate application; reverted if the save fails (§5).
    void i18n.changeLanguage(next)
    update.mutate(
      { languagePref: next },
      { onError: () => void i18n.changeLanguage(active) },
    )
  }

  return (
    <section
      data-settings-section="language"
      data-testid="settings-language"
      className="flex flex-col gap-3"
    >
      <h2 className="text-lg font-semibold">{t('accountSettings.languageTitle')}</h2>
      <div
        role="group"
        aria-label={t('accountSettings.languageLabel')}
        className="inline-flex w-fit items-center rounded-md border p-0.5"
      >
        {SUPPORTED_LANGUAGES.map((lng) => {
          const isActive = lng === active
          return (
            <Button
              key={lng}
              type="button"
              size="sm"
              variant={isActive ? 'default' : 'ghost'}
              aria-pressed={isActive}
              lang={lng}
              disabled={update.isPending}
              onClick={() => changeLanguage(lng)}
            >
              {labelFor[lng]}
            </Button>
          )
        })}
      </div>
      <p className="text-xs text-muted-foreground">
        {t('accountSettings.languageHint')}
      </p>
      {update.isError ? (
        <p role="alert" data-testid="language-error" className="text-sm text-destructive">
          {t('accountSettings.languageSaveFailed')}
        </p>
      ) : null}
      {/* The persisted account preference is the source of truth for a11y. */}
      <span className="sr-only" data-testid="language-preference">
        {current}
      </span>
    </section>
  )
}

/* ------------------------------------------------------------------ *
 * 2. Password change (UXR-ACC-018).
 * ------------------------------------------------------------------ */

function PasswordSection() {
  const { t } = useTranslation()
  const change = useChangePassword()

  const [current, setCurrent] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [touched, setTouched] = useState({ current: false, newPassword: false })
  const [submitted, setSubmitted] = useState(false)

  const validity = useMemo(
    () => validatePasswordChange({ current, newPassword }),
    [current, newPassword],
  )

  const serverFields = serverValidationFields(change.error)
  const currentServerError = serverFields.some((field) => field.field === 'current')
  const newServerError = serverFields.some((field) => field.field === 'newPassword')
  const genericError = change.isError && !currentServerError && !newServerError

  const showCurrentError =
    (touched.current || submitted) && !validity.current
  const showNewError = (touched.newPassword || submitted) && !validity.newPassword

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSubmitted(true)
    if (!validity.valid) {
      focusFirstInvalid(validity.current)
      return
    }
    change.mutate(
      { current, newPassword },
      {
        onSuccess: () => {
          setCurrent('')
          setNewPassword('')
          setTouched({ current: false, newPassword: false })
          setSubmitted(false)
        },
      },
    )
  }

  return (
    <section
      data-settings-section="password"
      data-testid="settings-password"
      className="flex flex-col gap-3"
    >
      <h2 className="text-lg font-semibold">{t('accountSettings.passwordTitle')}</h2>
      <form
        aria-label={t('accountSettings.passwordTitle')}
        className="flex flex-col gap-4"
        noValidate
        onSubmit={handleSubmit}
      >
        <FormField
          id="settings-current-password"
          label={t('accountSettings.currentLabel')}
          required
          error={
            showCurrentError
              ? t('accountSettings.currentRequired')
              : currentServerError
                ? t('accountSettings.currentMismatch')
                : undefined
          }
        >
          {(control) => (
            <Input
              {...control}
              type="password"
              autoComplete="current-password"
              disabled={change.isPending}
              value={current}
              onChange={(event) => setCurrent(event.target.value)}
              onBlur={() => setTouched((state) => ({ ...state, current: true }))}
            />
          )}
        </FormField>

        <FormField
          id="settings-new-password"
          label={t('accountSettings.newPasswordLabel')}
          required
          hint={t('accountSettings.passwordRules')}
          error={
            showNewError
              ? t('accountSettings.newRequired')
              : newServerError
                ? t('accountSettings.newTooShort', { min: PASSWORD_MIN_LENGTH })
                : undefined
          }
        >
          {(control) => (
            <Input
              {...control}
              type="password"
              autoComplete="new-password"
              disabled={change.isPending}
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              onBlur={() =>
                setTouched((state) => ({ ...state, newPassword: true }))
              }
            />
          )}
        </FormField>

        <div className="flex flex-col gap-2">
          <Button
            type="submit"
            className="w-fit"
            disabled={!validity.valid || change.isPending}
          >
            {change.isPending
              ? t('accountSettings.passwordSubmitting')
              : t('accountSettings.passwordSubmit')}
          </Button>

          {change.isSuccess ? (
            <p
              role="status"
              data-testid="password-success"
              className="text-sm text-success-foreground"
            >
              {t('accountSettings.passwordSuccess')}
            </p>
          ) : null}

          {genericError ? (
            <p role="alert" data-testid="password-error" className="text-sm text-destructive">
              {t('accountSettings.passwordError')}
            </p>
          ) : null}
        </div>
      </form>
    </section>
  )
}

/** Extract the `VALIDATION_FAILED` field list from an RFC 7807 problem body. */
function serverValidationFields(error: unknown): ValidationField[] {
  if (!(error instanceof ApiError)) return []
  const fields = error.problem?.params?.fields
  return Array.isArray(fields) ? (fields as ValidationField[]) : []
}

function focusFirstInvalid(currentValid: boolean) {
  const id = currentValid ? 'settings-new-password' : 'settings-current-password'
  document.getElementById(id)?.focus()
}

/* ------------------------------------------------------------------ *
 * 3. Entry points to saved work (FR-ACC-005).
 * ------------------------------------------------------------------ */

function EntryPointsSection() {
  const { t } = useTranslation()
  return (
    <section
      data-settings-section="entry-points"
      data-testid="settings-entry-points"
      className="flex flex-col gap-3"
    >
      <h2 className="text-lg font-semibold">{t('accountSettings.entryPointsTitle')}</h2>
      <div className="flex flex-wrap gap-3">
        <Link className={buttonVariants({ variant: 'outline' })} to="/screens">
          {t('accountSettings.entryScreens')}
        </Link>
        <Link className={buttonVariants({ variant: 'outline' })} to="/scenarios">
          {t('accountSettings.entryScenarios')}
        </Link>
      </div>
    </section>
  )
}

/* ------------------------------------------------------------------ *
 * 4. Account deletion (UXR-ACC-019).
 * ------------------------------------------------------------------ */

function DeletionSection() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const remove = useDeleteAccount()
  const [confirmOpen, setConfirmOpen] = useState(false)

  function confirmDelete() {
    remove.mutate(undefined, {
      onSuccess: () => navigate('/', { replace: true }),
      onError: () => setConfirmOpen(false),
    })
  }

  return (
    <section
      data-settings-section="deletion"
      data-testid="settings-deletion"
      className="flex flex-col gap-3"
    >
      <h2 className="text-lg font-semibold">{t('accountSettings.deletionTitle')}</h2>
      <p className="text-sm text-muted-foreground">
        {t('accountSettings.deletionBody')}
      </p>
      <Button
        type="button"
        variant="destructive"
        className="w-fit"
        data-testid="delete-account"
        onClick={() => {
          remove.reset()
          setConfirmOpen(true)
        }}
      >
        {t('accountSettings.deleteButton')}
      </Button>

      {remove.isError ? (
        <p role="alert" data-testid="delete-error" className="text-sm text-destructive">
          {t('accountSettings.deleteError')}
        </p>
      ) : null}

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title={t('accountSettings.deleteDialogTitle')}
        description={t('accountSettings.deleteDialogDescription')}
        confirmLabel={t('accountSettings.deleteConfirm')}
        cancelLabel={t('common.cancel')}
        closeLabel={t('common.close')}
        destructive
        pending={remove.isPending}
        onConfirm={confirmDelete}
      />
    </section>
  )
}

/* ------------------------------------------------------------------ *
 * 5. E-mail verification status (UXR-ACC-021).
 * ------------------------------------------------------------------ */

function VerificationSection({ email, verified }: { email: string; verified: boolean }) {
  const { t } = useTranslation()
  const resend = useResendVerification()

  return (
    <section
      data-settings-section="verification"
      data-testid="settings-verification"
      className="flex flex-col gap-3"
    >
      <h2 className="text-lg font-semibold">{t('accountSettings.verificationTitle')}</h2>
      <p data-testid="verification-email" className="text-sm">
        {t('accountSettings.verificationEmailLabel')}: {email}
      </p>
      <Badge
        data-testid="verification-badge"
        variant={verified ? 'success' : 'warning'}
        className="w-fit"
      >
        {verified
          ? t('accountSettings.verifiedBadge')
          : t('accountSettings.unverifiedBadge')}
      </Badge>
      <p className="text-sm text-muted-foreground">
        {verified
          ? t('accountSettings.verifiedBody')
          : t('accountSettings.unverifiedBody')}
      </p>

      {!verified ? (
        resend.isSuccess ? (
          <p role="status" data-testid="resend-sent" className="text-sm text-muted-foreground">
            {t('accountSettings.resent')}
          </p>
        ) : (
          <div className="flex flex-col items-start gap-1">
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="w-fit"
              disabled={resend.isPending}
              onClick={() => resend.mutate()}
            >
              {resend.isPending
                ? t('accountSettings.resending')
                : t('accountSettings.resend')}
            </Button>
            {resend.isError ? (
              <p role="alert" className="text-sm text-destructive">
                {t('accountSettings.resendFailed')}
              </p>
            ) : null}
          </div>
        )
      ) : null}
    </section>
  )
}

/** Anonymous direct access → sign-in path (UXR-ACC-020). */
function AnonymousState() {
  const { t } = useTranslation()
  return (
    <div
      data-testid="settings-anonymous"
      className="flex flex-col gap-2 rounded-lg border p-4"
    >
      <p className="font-medium">{t('accountSettings.anonymousTitle')}</p>
      <p className="text-sm text-muted-foreground">
        {t('accountSettings.anonymousBody')}
      </p>
      <div className="flex flex-wrap gap-3">
        <Link
          className={buttonVariants({ size: 'sm' })}
          to="/sign-in?returnUrl=%2Fsettings"
        >
          {t('accountSettings.signIn')}
        </Link>
        <Link
          className={buttonVariants({ size: 'sm', variant: 'outline' })}
          to="/register?returnUrl=%2Fsettings"
        >
          {t('accountSettings.register')}
        </Link>
      </div>
    </div>
  )
}
