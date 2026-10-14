#!/usr/bin/env node
/**
 * Advice-vocabulary gate (UXR-G-017, NFR-MOV-006, `01` §9; TC-XC-007;
 * wired as `npm run check:advice`).
 *
 * The platform is informational-only: it must never tell a reader to buy, sell or
 * otherwise act on a security. Every user-facing string is served from the i18n
 * catalogs, so a banned-term scan over both catalogs is the CI backstop that keeps
 * recommendation phrasing out of the product copy.
 *
 * Fails (exit 1) when any catalog value contains recommendation/advice vocabulary
 * (the maintained TR + EN term list below).
 *
 * Carve-out — `disclaimer.text`: the informational-only notice exists precisely to
 * *negate* advice ("not investment advice … not a recommendation to buy or sell"),
 * so it necessarily contains the vocabulary it disclaims. That one leaf is exempt
 * from the scan; every other catalog value is checked.
 *
 * The term list lives beside this script (I-XC-3). Changing it is a plan-change
 * reviewed with the test-planner — do not loosen it to make a build pass.
 *
 * Usage:
 *   node scripts/advice-vocabulary.mjs [--tr <tr.json>] [--en <en.json>]
 *
 * The CLI options let the gate be exercised against fixture catalogs (see the
 * ticket's verification evidence); production runs use the checked-in defaults.
 */
import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const WEB_ROOT = resolve(__dirname, '..')

/**
 * Banned recommendation vocabulary — the maintained TR + EN list (I-XC-3).
 *
 * Matching is case-insensitive (Turkish-aware: `toLocaleLowerCase('tr')`) and
 * substring-based, because Turkish suffixes attach to the stem ("tavsiyesi",
 * "önerisi"). Bare "al"/"sat" stems are deliberately NOT listed: as substrings they
 * match benign words, so the framed forms ("alım", "satım", "satın al", "almalı")
 * carry the intent instead.
 */
const TR_TERMS = [
  'tavsiye', // advice
  'öneri', // recommendation
  'önerir', // recommends
  'öneriyor', // (is) recommending
  'alım', // buying / purchase
  'satım', // selling / sale
  'satın al', // to buy
  'almalı', // should buy
  'satmalı', // should sell
]

const EN_TERMS = [
  'advice', // covered by the disclaimer carve-out only
  'recommend', // recommend / recommendation
  'buy',
  'sell',
  'price target',
  'target price',
]

/** The informational-only notice that negates advice (see the carve-out note above). */
const DISCLAIMER_KEY = 'disclaimer.text'

function parseArgs(argv) {
  const options = {
    tr: join(WEB_ROOT, 'src/i18n/catalogs/tr.json'),
    en: join(WEB_ROOT, 'src/i18n/catalogs/en.json'),
  }
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i]
    if (arg === '--tr') options.tr = resolve(argv[++i])
    else if (arg === '--en') options.en = resolve(argv[++i])
    else if (arg === '--help' || arg === '-h') {
      console.log('Usage: node scripts/advice-vocabulary.mjs [--tr <tr.json>] [--en <en.json>]')
      process.exit(0)
    } else {
      console.error(`Unknown argument: ${arg}`)
      process.exit(2)
    }
  }
  return options
}

/** Flatten a nested catalog into dot-notation leaf keys with their string values. */
function flatten(value, prefix = '', out = []) {
  if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
    for (const [key, child] of Object.entries(value)) {
      flatten(child, prefix ? `${prefix}.${key}` : key, out)
    }
  } else if (prefix) {
    out.push([prefix, value])
  }
  return out
}

function readCatalog(path, label) {
  let raw
  try {
    raw = readFileSync(path, 'utf8')
  } catch {
    console.error(`advice-vocabulary: cannot read ${label} catalog at ${path}`)
    process.exit(2)
  }
  try {
    return flatten(JSON.parse(raw))
  } catch (error) {
    console.error(
      `advice-vocabulary: ${label} catalog is not valid JSON (${path}): ${error.message}`,
    )
    process.exit(2)
  }
}

function findMatches(catalogPath, label, terms) {
  const findings = []
  for (const [key, value] of readCatalog(catalogPath, label)) {
    if (key === DISCLAIMER_KEY) continue
    if (typeof value !== 'string') continue
    const haystack = value.toLocaleLowerCase('tr')
    for (const term of terms) {
      if (haystack.includes(term.toLocaleLowerCase('tr'))) {
        findings.push({ key, term, value })
        break
      }
    }
  }
  return findings
}

function main() {
  const { tr, en } = parseArgs(process.argv.slice(2))

  const findings = [
    ...findMatches(tr, 'TR', TR_TERMS),
    ...findMatches(en, 'EN', EN_TERMS),
  ]

  if (findings.length > 0) {
    console.error(
      `advice-vocabulary: ${findings.length} value(s) contain recommendation vocabulary:`,
    )
    for (const finding of findings) {
      console.error(`  - ${finding.key} (matched "${finding.term}"): ${finding.value}`)
    }
    console.error('\nadvice-vocabulary: FAILED')
    process.exit(1)
  }

  console.log('advice-vocabulary: OK — no recommendation vocabulary in the TR/EN catalogs.')
}

main()
