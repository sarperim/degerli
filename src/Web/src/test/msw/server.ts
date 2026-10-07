import { setupServer } from 'msw/node'

/**
 * Shared MSW server for L3 tests (decision B). Handlers are registered per test
 * via `server.use(...)` so tests are order-independent; unhandled requests are
 * treated as errors by the setup lifecycle in `src/test/setup.ts`.
 */
export const server = setupServer()
