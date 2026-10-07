import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

/**
 * Vitest config for the web app (test strategy §5.3, §9.1).
 *
 * jsdom is the required environment for Testing Library; the `@` alias mirrors
 * `vite.config.ts` so tests import through the same paths as the app.
 */
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    globals: false,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    css: false,
    clearMocks: true,
    restoreMocks: true,
  },
})
