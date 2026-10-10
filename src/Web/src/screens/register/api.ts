import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api/client'
import type {
  RegisterRequest,
  SessionResponse,
  VerifyEmailRequest,
} from '@/lib/api/contracts'
import { SESSION_QUERY_KEY } from '@/lib/auth/session'

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
}

export function registerUser(variables: RegisterVariables): Promise<SessionResponse> {
  const body: RegisterRequest = {
    email: variables.email,
    password: variables.password,
    consent: { noticeVersion: variables.noticeVersion },
  }
  return apiFetch<SessionResponse>('/auth/register', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function useRegister(): UseMutationResult<
  SessionResponse,
  Error,
  RegisterVariables
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: registerUser,
    onSuccess: () => {
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
      void queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY })
    },
  })
}

export function resendVerification(): Promise<void> {
  return apiFetch<void>('/auth/resend-verification', { method: 'POST' })
}

export function useResendVerification(): UseMutationResult<void, Error, void> {
  return useMutation({ mutationFn: resendVerification })
}
