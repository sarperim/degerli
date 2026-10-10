import { setupServer } from 'msw/node'

import { csrfTokenHandler } from '@/test/msw/handlers'

/**
 * Shared MSW server for L3 tests (decision B). The antiforgery-token endpoint
 * (`03` §2) is seeded as a permanent handler because every unsafe API call now
 * fetches it first; all other handlers are registered per test via `server.use(...)`
 * so tests are order-independent. Unhandled requests are treated as errors by the
 * setup lifecycle in `src/test/setup.ts`.
 */
export const server = setupServer(csrfTokenHandler())
