import { expect, type Page } from '@playwright/test'

/**
 * Network-quiet window helper (test strategy §5.4, §15; TC-XC-021).
 *
 * The product frames data as end-of-day and must not imply real-time updates, so
 * L4 tests assert that defined quiet windows observe **no** matching requests.
 * `recordRequests` exposes the raw recorder so a test can prove the helper is not
 * vacuous (a positive control that deliberately triggers a request).
 */

/** Default matcher: any `/api/` request. */
export const API_REQUEST_PATTERN = /\/api\//

export interface NetworkRecorder {
  /** Matching request URLs seen so far (live). */
  readonly urls: string[]
  /** Stops recording and returns the matched URLs. */
  stop(): string[]
}

/** Starts recording requests whose URL matches `match`. */
export function recordRequests(
  page: Page,
  match: RegExp = API_REQUEST_PATTERN,
): NetworkRecorder {
  const urls: string[] = []
  const listener = (request: { url(): string }): void => {
    if (match.test(request.url())) {
      urls.push(request.url())
    }
  }

  page.on('request', listener)
  return {
    urls,
    stop() {
      page.off('request', listener)
      return urls
    },
  }
}

export interface QuietWindowOptions {
  /** Length of the quiet window in milliseconds. */
  windowMs?: number
  /** Request matcher; defaults to {@link API_REQUEST_PATTERN}. */
  match?: RegExp
}

/**
 * Observes a quiet window of `windowMs` and asserts no request matched `match`.
 * Fails with the offending URLs rather than a bare assertion.
 */
export async function assertNetworkQuiet(
  page: Page,
  options: QuietWindowOptions = {},
): Promise<void> {
  const windowMs = options.windowMs ?? 3_000
  const recorder = recordRequests(page, options.match ?? API_REQUEST_PATTERN)

  await page.waitForTimeout(windowMs)
  const urls = recorder.stop()

  expect(
    urls,
    `expected no request matching ${options.match ?? API_REQUEST_PATTERN} during the ` +
      `${windowMs} ms quiet window, but saw ${urls.length}: ${urls.join(', ')}`,
  ).toEqual([])
}
