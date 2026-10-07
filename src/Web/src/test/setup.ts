import '@testing-library/jest-dom/vitest'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { cleanup } from '@testing-library/react'

import { server } from '@/test/msw/server'

// MSW lifecycle (decision B): every test starts with a clean handler stack and
// an unhandled request is a test error, not a silent network fall-through.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

afterEach(() => {
  cleanup()
  server.resetHandlers()
})

afterAll(() => server.close())
