import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query'

import { apiFetch, clearCsrfToken, isApiErrorCode } from '@/lib/api/client'
import type {
  LanguagePref,
  RegisterRequest,
  RegisterResponse,
  ResendVerificationResponse,
  VerifyEmailRequest,
} from '@/lib/api/contracts'
import { SESSION_QUERY_KEY } from '@/lib/auth/session'
import {
  clearPendingVerificationToken,
  setPendingVerificationToken,
} from '@/screens/register/pendingVerificationToken'

/**
 * Identity mutations for SCR-007 (`03` §6).
 *
 * Every identity mutation invalidates `['session']` on success (`01` §8.4), so
 * the header auth state and the persistence gate refresh without a reload; the
 * verify landing relies on this to unblock an open session immediately (M-9).
 */

export interface RegisterVariables {
  email: string
  password: string
  noticeVersion: string
  languagePref?: LanguagePref
}

export function registerUser(variables: RegisterVariables): Promise<RegisterResponse> {
  const body: RegisterRequest = {
    email: variables.email,
    password: variables.password,
    consent: { noticeVersion: variables.noticeVersion },
    ...(variables.languagePref ? { languagePref: variables.languagePref } : {}),
  }
  return apiFetch<RegisterResponse>('/auth/register', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function useRegister(): UseMutationResult<
  RegisterResponse,
  Error,
  RegisterVariables
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: registerUser,
    onSuccess: () => {
      clearCsrfToken()
      void queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY })
    },
  })
}

export function verifyEmail(token: string): Promise<void> {
  const body: VerifyEmailRequest = { token }
  return apiFetch<void>('/auth/verify-email', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function useVerifyEmail(): UseMutationResult<void, Error, string> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: verifyEmail,
    onSuccess: () => {
      clearPendingVerificationToken()
      void queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY })
    },
    onError: (error, token) => {
      if (isApiErrorCode(error, 'UNAUTHENTICATED') || isApiErrorCode(error, 'RATE_LIMITED')) {
        setPendingVerificationToken(token)
      } else {
        clearPendingVerificationToken()
      }
    },
  })
}

export function resendVerification(): Promise<ResendVerificationResponse> {
  return apiFetch<ResendVerificationResponse>('/auth/resend-verification', {
    method: 'POST',
  })
}

export function useResendVerification(): UseMutationResult<
  ResendVerificationResponse,
  Error,
  void
> {
  return useMutation({ mutationFn: resendVerification })
}
