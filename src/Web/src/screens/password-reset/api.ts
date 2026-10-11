import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api/client'
import type {
  ForgotPasswordRequest,
  ForgotPasswordResponse,
  ResetPasswordRequest,
  ResetPasswordResponse,
} from '@/lib/api/contracts'
import { SESSION_QUERY_KEY } from '@/lib/auth/session'

/**
 * Identity mutations for SCR-009 password reset (`03` §6).
 *
 * `forgot-password` always answers a neutral 200 (never reveals whether the
 * address is registered, UXR-ACC-012), so the request mutation carries no
 * success payload the UI branches on. `reset-password` may establish a session,
 * so on success `['session']` is invalidated — the header auth state reflects
 * the signed-in state without a reload (UXR-ACC-015).
 */

/** `POST /auth/forgot-password` — always-200 neutral confirmation. */
export function requestPasswordReset(email: string): Promise<ForgotPasswordResponse> {
  const body: ForgotPasswordRequest = { email }
  return apiFetch<ForgotPasswordResponse>('/auth/forgot-password', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function useForgotPassword(): UseMutationResult<
  ForgotPasswordResponse,
  Error,
  string
> {
  return useMutation({ mutationFn: requestPasswordReset })
}

export interface ResetPasswordVariables {
  token: string
  newPassword: string
}

/** `POST /auth/reset-password` — 200 (may establish a session) / 410 `TOKEN_EXPIRED`. */
export function resetPassword(
  variables: ResetPasswordVariables,
): Promise<ResetPasswordResponse> {
  const body: ResetPasswordRequest = {
    token: variables.token,
    newPassword: variables.newPassword,
  }
  return apiFetch<ResetPasswordResponse>('/auth/reset-password', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function useResetPassword(): UseMutationResult<
  ResetPasswordResponse,
  Error,
  ResetPasswordVariables
> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: resetPassword,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY })
    },
  })
}
