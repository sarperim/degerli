#!/usr/bin/env node
/**
 * i18n parity check (architecture §10.6; TC-XC-002; wired as `npm run check:i18n`).
 *
 * Fails (exit 1) when:
 *   1. a key exists in one catalog but not the other (TR is the reference set), or
 *   2. a key exists in both catalogs but is referenced nowhere in the web source
 *      (an "unused in both" dead key).
 *
 * Usage:
 *   node scripts/i18n-parity.mjs [--tr <tr.json>] [--en <en.json>] [--src <dir>]
 *
 * The CLI options exist so the script can be exercised against fixture catalogs
 * (see the ticket's verification evidence); production runs use the defaults.
 */
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { dirname, extname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const WEB_ROOT = resolve(__dirname, '..')

const SOURCE_EXTENSIONS = new Set(['.ts', '.tsx', '.js', '.jsx', '.mjs'])
const SKIP_DIRECTORIES = new Set(['node_modules', 'dist', '.git', 'coverage'])

function parseArgs(argv) {
  const options = {
    tr: join(WEB_ROOT, 'src/i18n/catalogs/tr.json'),
    en: join(WEB_ROOT, 'src/i18n/catalogs/en.json'),
    src: join(WEB_ROOT, 'src'),
  }
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i]
    if (arg === '--tr') options.tr = resolve(argv[++i])
    else if (arg === '--en') options.en = resolve(argv[++i])
    else if (arg === '--src') options.src = resolve(argv[++i])
    else if (arg === '--help' || arg === '-h') {
      console.log(
        'Usage: node scripts/i18n-parity.mjs [--tr <tr.json>] [--en <en.json>] [--src <dir>]',
      )
      process.exit(0)
    } else {
      console.error(`Unknown argument: ${arg}`)
      process.exit(2)
    }
  }
  return options
}

/** Flatten a nested catalog into dot-notation leaf keys. */
function flattenKeys(value, prefix = '', out = []) {
  if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
    for (const [key, child] of Object.entries(value)) {
      flattenKeys(child, prefix ? `${prefix}.${key}` : key, out)
    }
  } else if (prefix) {
    out.push(prefix)
  }
  return out
}

function readCatalog(path, label) {
  let raw
  try {
    raw = readFileSync(path, 'utf8')
  } catch {
    console.error(`i18n parity: cannot read ${label} catalog at ${path}`)
    process.exit(2)
  }
  try {
    return flattenKeys(JSON.parse(raw))
  } catch (error) {
    console.error(`i18n parity: ${label} catalog is not valid JSON (${path}): ${error.message}`)
    process.exit(2)
  }
}

/** Recursively collect source files whose text may reference catalog keys. */
function collectSourceFiles(dir, out = []) {
  let entries
  try {
    entries = readdirSync(dir, { withFileTypes: true })
  } catch {
    return out
  }
  for (const entry of entries) {
    const full = join(dir, entry.name)
    if (entry.isDirectory()) {
      if (SKIP_DIRECTORIES.has(entry.name)) continue
      // The catalogs themselves are the definition, not a usage site.
      if (full.split(/[\\/]/).slice(-2).join('/') === 'i18n/catalogs') continue
      collectSourceFiles(full, out)
    } else if (entry.isFile() && SOURCE_EXTENSIONS.has(extname(entry.name))) {
      out.push(full)
    }
  }
  return out
}

function readCorpus(srcDir) {
  return collectSourceFiles(srcDir)
    .map((file) => readFileSync(file, 'utf8'))
    .join('\n')
}

/** True when `key` appears in the corpus as a standalone literal (not a prefix/substring). */
function isKeyUsed(key, corpus) {
  const escaped = key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const pattern = new RegExp(`(?<![\\w.])${escaped}(?![\\w])`)
  return pattern.test(corpus)
}

function main() {
  const { tr, en, src } = parseArgs(process.argv.slice(2))

  const trKeys = readCatalog(tr, 'TR')
  const enKeys = readCatalog(en, 'EN')
  const trSet = new Set(trKeys)
  const enSet = new Set(enKeys)

  const missingInEn = trKeys.filter((key) => !enSet.has(key))
  const missingInTr = enKeys.filter((key) => !trSet.has(key))
  const shared = trKeys.filter((key) => enSet.has(key))

  const corpus = readCorpus(src)
  const unused = shared.filter((key) => !isKeyUsed(key, corpus))

  let failed = false

  if (missingInEn.length > 0) {
    failed = true
    console.error(`i18n parity: ${missingInEn.length} key(s) present in TR but missing from EN:`)
    for (const key of missingInEn) console.error(`  - ${key}`)
  }
  if (missingInTr.length > 0) {
    failed = true
    console.error(`i18n parity: ${missingInTr.length} key(s) present in EN but missing from TR:`)
    for (const key of missingInTr) console.error(`  - ${key}`)
  }
  if (unused.length > 0) {
    failed = true
    console.error(
      `i18n parity: ${unused.length} key(s) present in both catalogs but unused in the source:`,
    )
    for (const key of unused) console.error(`  - ${key}`)
  }

  if (failed) {
    console.error('\ni18n parity: FAILED')
    process.exit(1)
  }

  console.log(
    `i18n parity: OK — ${shared.length} keys in sync across TR/EN, all referenced in source.`,
  )
}

main()
