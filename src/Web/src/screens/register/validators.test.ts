import { describe, expect, it } from 'vitest'

import {
  isSiteRelativePath,
  PRIVACY_NOTICE_VERSION,
} from '@/screens/register/validators'

/**
 * Regression guards for the SCR-007 client constants/validators that must match
 * the server contract: the consent notice version the API accepts (B1) and the
 * `?returnUrl` site-relative guard (SF3).
 */
describe('registration validators', () => {
  it('consents against the notice version the API accepts ("2026-10")', () => {
    // Server-issued catalog: AuthOptions.NoticeVersions / appsettings.json.
    expect(PRIVACY_NOTICE_VERSION).toBe('2026-10')
  })

  it('accepts only site-relative return targets', () => {
    expect(isSiteRelativePath('/screener')).toBe(true)
    expect(isSiteRelativePath('/stocks/ASELS')).toBe(true)

    // Cross-origin vectors: protocol-relative and the backslash authority form.
    expect(isSiteRelativePath('//evil.com')).toBe(false)
    expect(isSiteRelativePath('/\\evil.com')).toBe(false)
    expect(isSiteRelativePath('https://evil.com')).toBe(false)
    expect(isSiteRelativePath('evil.com')).toBe(false)
  })
})
