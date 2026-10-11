import { setupServer } from 'msw/node'

import { csrfTokenHandler, sessionHandler } from '@/test/msw/handlers'

/**
 * Shared MSW server for L3 tests (decision B). The antiforgery-token endpoint
 * (`03` §2) is seeded as a permanent handler because every unsafe API call now
 * fetches it first; `GET /auth/session` is seeded as an anonymous session because
 * the global header (rendered on every screen) reads it on mount — tests that
 * need a signed-in state override it with `server.use(sessionHandler(...))`. All
 * other handlers are registered per test via `server.use(...)` so tests are
 * order-independent. Unhandled requests are treated as errors by the setup
 * lifecycle in `src/test/setup.ts`.
 */
export const server = setupServer(csrfTokenHandler(), sessionHandler())
