import { useSyncExternalStore } from 'react'

export const SUPPORTED_THEMES = ['light', 'dark'] as const
export type Theme = (typeof SUPPORTED_THEMES)[number]

export const DEFAULT_THEME: Theme = 'light'
export const THEME_STORAGE_KEY = 'degerli.theme'

function isSupportedTheme(value: string | null): value is Theme {
  return value === 'light' || value === 'dark'
}

export function resolveInitialTheme(): Theme {
  if (typeof window === 'undefined') return DEFAULT_THEME
  try {
    const stored = window.localStorage.getItem(THEME_STORAGE_KEY)
    if (isSupportedTheme(stored)) return stored
  } catch {
    // Ignore localStorage access errors in restricted/private contexts.
  }
  try {
    if (
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-color-scheme: dark)').matches
    ) {
      return 'dark'
    }
  } catch {
    // Ignore matchMedia errors.
  }
  return DEFAULT_THEME
}

let currentTheme: Theme = resolveInitialTheme()
const listeners = new Set<() => void>()

export function applyDocumentTheme(theme: Theme) {
  if (typeof document === 'undefined') return
  document.documentElement.classList.toggle('dark', theme === 'dark')
  document.documentElement.style.colorScheme = theme
}

export function getTheme(): Theme {
  return currentTheme
}

export function setTheme(next: Theme) {
  currentTheme = next
  applyDocumentTheme(next)
  if (typeof window !== 'undefined') {
    try {
      window.localStorage.setItem(THEME_STORAGE_KEY, next)
    } catch {
      // Ignore storage write errors.
    }
  }
  for (const listener of listeners) {
    listener()
  }
}

export function toggleTheme() {
  setTheme(currentTheme === 'dark' ? 'light' : 'dark')
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

export function useTheme(): Theme {
  return useSyncExternalStore(subscribe, getTheme, () => DEFAULT_THEME)
}

applyDocumentTheme(currentTheme)
