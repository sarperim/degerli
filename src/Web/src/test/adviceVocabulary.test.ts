import { execFileSync } from 'node:child_process'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { afterEach, describe, expect, it } from 'vitest'

/**
 * TC-XC-007 — no advice vocabulary in the i18n catalogs (UXR-G-017, NFR-MOV-006;
 * `01` §9). The app is informational-only; every user-facing string is served from
 * the catalogs, so a banned-term grep over the catalogs is the CI backstop.
 *
 * This spec formalizes the gate as a recorded, failing-case-verified check: run the
 * real `advice-vocabulary.mjs` against the checked-in catalogs (green) and against
 * fixtures that inject recommendation phrasing in each language (red).
 */

const here = dirname(fileURLToPath(import.meta.url))
const WEB_ROOT = resolve(here, '..', '..')
const SCRIPT = join(WEB_ROOT, 'scripts', 'advice-vocabulary.mjs')
const REAL_TR = join(WEB_ROOT, 'src', 'i18n', 'catalogs', 'tr.json')
const REAL_EN = join(WEB_ROOT, 'src', 'i18n', 'catalogs', 'en.json')

interface RunResult {
  status: number
  stdout: string
  stderr: string
}

function runGate(args: string[]): RunResult {
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

const cleanups: Array<() => void> = []
afterEach(() => {
  while (cleanups.length > 0) cleanups.pop()!()
})

function writeCatalogs(tr: unknown, en: unknown) {
  const dir = mkdtempSync(join(tmpdir(), 'advice-vocab-'))
  const trPath = join(dir, 'tr.json')
  const enPath = join(dir, 'en.json')
  writeFileSync(trPath, JSON.stringify(tr, null, 2))
  writeFileSync(enPath, JSON.stringify(en, null, 2))
  cleanups.push(() => rmSync(dir, { recursive: true, force: true }))
  return { tr: trPath, en: enPath }
}

describe('TC-XC-007 advice-vocabulary gate', () => {
  it('passes on the checked-in catalogs (production gate is green)', () => {
    const result = runGate(['--tr', REAL_TR, '--en', REAL_EN])

    expect(result.status).toBe(0)
    expect(result.stdout).toContain('OK')
  })

  it('passes on a fixture that keeps the disclaimer carve-out', () => {
    const { tr, en } = writeCatalogs(
      { disclaimer: { text: 'Yatırım tavsiyesi değildir; alım ya da satım önerisi içermez.' } },
      {
        disclaimer: {
          text: 'Not investment advice and not a recommendation to buy or sell any security.',
        },
      },
    )

    const result = runGate(['--tr', tr, '--en', en])

    expect(result.status).toBe(0)
    expect(result.stdout).toContain('OK')
  })

  it('fails when a Turkish catalog value recommends buying', () => {
    const { tr, en } = writeCatalogs(
      { verdict: { text: 'Bu hisse şimdi satın alın — güçlü bir fırsat.' } },
      { verdict: { text: 'A strong opportunity.' } },
    )

    const result = runGate(['--tr', tr, '--en', en])

    expect(result.status).toBe(1)
    expect(result.stderr).toContain('verdict.text')
    expect(result.stderr).toContain('FAILED')
  })

  it('fails when an English catalog value recommends selling', () => {
    const { tr, en } = writeCatalogs(
      { verdict: { text: 'Güçlü bir fırsat.' } },
      { verdict: { text: 'We recommend that you sell this stock now.' } },
    )

    const result = runGate(['--tr', tr, '--en', en])

    expect(result.status).toBe(1)
    expect(result.stderr).toContain('verdict.text')
    expect(result.stderr).toContain('FAILED')
  })
})
