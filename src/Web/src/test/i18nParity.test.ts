import { execFileSync } from 'node:child_process'
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { afterEach, describe, expect, it } from 'vitest'

/**
 * TC-XC-002 — the i18n parity gate is a recorded, failing-case-verified CI gate
 * (NFR-MOV-002, `01` §6.1 web job, §10.6). The script ships with the SPA scaffold
 * (TKT-foundation-002); this spec formalizes it: run the real `i18n-parity.mjs`
 * against fixture catalogs that exercise each failure class, and assert the
 * checked-in catalogs pass.
 *
 * The gate must fail when a key is present in one catalog but not the other, or is
 * unused in both; TR is the reference set.
 */

const here = dirname(fileURLToPath(import.meta.url))
const WEB_ROOT = resolve(here, '..', '..')
const SCRIPT = join(WEB_ROOT, 'scripts', 'i18n-parity.mjs')
const REAL_TR = join(WEB_ROOT, 'src', 'i18n', 'catalogs', 'tr.json')
const REAL_EN = join(WEB_ROOT, 'src', 'i18n', 'catalogs', 'en.json')
const REAL_SRC = join(WEB_ROOT, 'src')

interface RunResult {
  status: number
  stdout: string
  stderr: string
}

function runParity(args: string[]): RunResult {
  try {
    const stdout = execFileSync(process.execPath, [SCRIPT, ...args], {
      encoding: 'utf8',
    })
    return { status: 0, stdout, stderr: '' }
  } catch (error) {
    const failure = error as { status?: number; stdout?: string; stderr?: string }
    return {
      status: failure.status ?? 1,
      stdout: failure.stdout ?? '',
      stderr: failure.stderr ?? '',
    }
  }
}

/** A temp fixture catalog directory + a source tree whose files reference `usedKeys`. */
function makeFixture(tr: Record<string, string>, en: Record<string, string>, usedKeys: string[]) {
  const dir = mkdtempSync(join(tmpdir(), 'i18n-parity-'))
  const srcDir = join(dir, 'src')
  mkdirSync(srcDir, { recursive: true })
  writeFileSync(join(dir, 'tr.json'), JSON.stringify(tr, null, 2))
  writeFileSync(join(dir, 'en.json'), JSON.stringify(en, null, 2))
  writeFileSync(
    join(srcDir, 'usage.ts'),
    usedKeys.map((key) => `t('${key}')`).join('\n') + '\n',
  )
  return {
    tr: join(dir, 'tr.json'),
    en: join(dir, 'en.json'),
    src: srcDir,
    cleanup: () => rmSync(dir, { recursive: true, force: true }),
  }
}

const cleanups: Array<() => void> = []
afterEach(() => {
  while (cleanups.length > 0) cleanups.pop()!()
})

function fixture(tr: Record<string, string>, en: Record<string, string>, usedKeys: string[]) {
  const built = makeFixture(tr, en, usedKeys)
  cleanups.push(built.cleanup)
  return built
}

describe('TC-XC-002 i18n parity gate', () => {
  it('passes on the checked-in TR/EN catalogs (production gate is green)', () => {
    const result = runParity(['--tr', REAL_TR, '--en', REAL_EN, '--src', REAL_SRC])

    expect(result.status).toBe(0)
    expect(result.stdout).toContain('OK')
  })

  it('passes on a fixture with symmetric, referenced keys', () => {
    const { tr, en, src } = fixture(
      { greeting: 'Merhaba', farewell: 'Hoşça kal' },
      { greeting: 'Hello', farewell: 'Goodbye' },
      ['greeting', 'farewell'],
    )

    const result = runParity(['--tr', tr, '--en', en, '--src', src])

    expect(result.status).toBe(0)
    expect(result.stdout).toContain('OK')
  })

  it('fails when a key is present in TR but missing from EN', () => {
    const { tr, en, src } = fixture(
      { greeting: 'Merhaba', onlyTr: 'Yalnız TR' },
      { greeting: 'Hello' },
      ['greeting', 'onlyTr'],
    )

    const result = runParity(['--tr', tr, '--en', en, '--src', src])

    expect(result.status).toBe(1)
    expect(result.stderr).toContain('onlyTr')
    expect(result.stderr).toContain('FAILED')
  })

  it('fails when a key is present in EN but missing from TR', () => {
    const { tr, en, src } = fixture(
      { greeting: 'Merhaba' },
      { greeting: 'Hello', onlyEn: 'EN only' },
      ['greeting', 'onlyEn'],
    )

    const result = runParity(['--tr', tr, '--en', en, '--src', src])

    expect(result.status).toBe(1)
    expect(result.stderr).toContain('onlyEn')
    expect(result.stderr).toContain('FAILED')
  })

  it('fails when a key exists in both catalogs but is unused in the source', () => {
    const { tr, en, src } = fixture(
      { greeting: 'Merhaba', deadKey: 'Kullanılmayan' },
      { greeting: 'Hello', deadKey: 'Unused' },
      ['greeting'],
    )

    const result = runParity(['--tr', tr, '--en', en, '--src', src])

    expect(result.status).toBe(1)
    expect(result.stderr).toContain('deadKey')
    expect(result.stderr).toContain('FAILED')
  })
})
