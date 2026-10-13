import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query'

import { apiFetch } from '@/lib/api/client'
import type { LoginRequest, LoginResponse } from '@/lib/api/contracts'
import { SESSION_QUERY_KEY } from '@/lib/auth/session'

/**
 * Identity mutations for SCR-008 sign-in and the global sign-out control
 * (`03` §6; FR-ACC-002/003).
 *
 * Both mutations invalidate `['session']` on success (`01` §8.4), so the header
 * auth state and the persistence gate refresh without a reload (mutation matrix).
 * The account language preference returned by sign-in is applied client-side by
 * the screen (UXR-ACC-009); sign-out leaves the device-local language untouched
 * (UXR-ACC-010).
 */

export interface LoginVariables {
  email: string
  password: string
}

export function loginUser(variables: LoginVariables): Promise<LoginResponse> {
  const body: LoginRequest = {
    email: variables.email,
    password: variables.password,
  }
  return apiFetch<LoginResponse>('/auth/login', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function useLogin(): UseMutationResult<LoginResponse, Error, LoginVariables> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: loginUser,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY })
    },
  })
}

export function logoutUser(): Promise<void> {
  return apiFetch<void>('/auth/logout', { method: 'POST' })
}

export function useLogout(): UseMutationResult<void, Error, void> {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: logoutUser,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY })
    },
  })
}
