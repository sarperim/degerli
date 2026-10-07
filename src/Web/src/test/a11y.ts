import axe, { type Result, type RunOptions } from 'axe-core'

/**
 * Accessibility assertion helper for component tests (decision C, jsdom leg).
 *
 * Color-contrast is disabled by default: jsdom has no layout/canvas engine, so
 * the rule cannot run deterministically there — it is covered by axe in the
 * Playwright e2e job on the real browser.
 */
const DEFAULT_RUN_OPTIONS: RunOptions = {
  rules: {
    'color-contrast': { enabled: false },
  },
}

export type A11yViolation = Result

/** Run axe against a container and return its violations (empty === clean). */
export async function runA11y(
  container: HTMLElement,
  options: RunOptions = {},
): Promise<A11yViolation[]> {
  const results = await axe.run(container, {
    ...DEFAULT_RUN_OPTIONS,
    ...options,
    rules: {
      ...DEFAULT_RUN_OPTIONS.rules,
      ...options.rules,
    },
  })
  return results.violations
}

/** Human-readable one-line-per-violation summary for failure output. */
export function formatA11yViolations(violations: A11yViolation[]): string {
  return violations
    .map((violation) => `${violation.id}: ${violation.help} (${violation.nodes.length} node/s)`)
    .join('\n')
}
