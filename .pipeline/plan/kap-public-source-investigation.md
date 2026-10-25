# KAP public-site source investigation — verified live 2026-10-25 (orchestrator)

**Purpose:** establish, with evidence, whether current (2026) KAP data is programmatically reachable
without MKK production VYK access. Triggered by USER DIRECTIVE 2026-10-25: *"i guess we need a kap
scraper so lets go on with architect and testbuilder etc so it could work… try libraries first then
if not work continue with it."*

## Verdict

**Yes.** KAP's public website exposes structured JSON endpoints with current data. A .NET adapter
over these endpoints can replace the frozen 2023 dev-gateway as the statements/disclosures source.
This supersedes the "wait for OQ-KAP-PROD" recommendation — the user has chosen to build the
public-site lane, with the VYK adapter retained for whenever prod access arrives.

## Library assessment (user asked: try libraries first)

| Library | Finding |
|---|---|
| **`pykap`** (cemsinano/pykap, PyPI, MIT, 65★) | **Endpoint discovery: invaluable. Runtime: broken.** Its `_get_announcement` HTML parser targets the *old* GWT page classes (`gwt-Label`, `taxonomy-context-value`) and returns `None` on the current Next.js announcement page — verified live on disclosure `1643141`. It is also Python; our ingestion is .NET. **We adopt its endpoints, not its code.** |
| `kap-tr-sdk`, `KAP_Scraper`, `kap-scraper` | Same class (public-site scraping, older page shapes, Python). Not evaluated further — pykap's endpoint set already covers what we need. |

## The source chain (all verified live, no auth, current to 2026-10-23)

### 1. `POST https://www.kap.org.tr/tr/api/disclosure/members/byCriteria` — disclosure list

Body (verified working):

```json
{
  "fromDate": "2026-10-01", "toDate": "2026-10-25",
  "disclosureClass": "FR",            // "" = all classes
  "subjectList": [],                  // ["4028328c594bfdca01594c0af9aa0057"] = Finansal Rapor
  "mkkMemberOidList": ["<company oid>"],  // e.g. ASELS 4028e4a1413b7ef401413bc2251e0047
  "inactiveMkkMemberOidList": [], "bdkMemberOidList": [],
  "fromSrc": false, "disclosureIndexList": []
}
```

**Verified results:**
- ASELS, `disclosureClass="FR"`, 2026-01-01→2026-10-25 → **3 rows**: `1643141` (2026 6 Aylık,
  published **04.08.2026 18:39:41** — the exact filing the user supplied), `1598316` (2026 3 Aylık,
  28.04.2026), `1561039` (2025 Yıllık, 24.02.2026).
- GARAN, all classes, 2026-10-01→2026-10-25 → **25 rows**, newest **23.10.2026 18:11** (yesterday),
  classes `ODA`/`DKB`.
- Row shape (rich): `disclosureIndex, disclosureClass, disclosureType, publishDate, year, ruleType,
  period, subject, summary, relatedStocks, stockCodes, kapTitle, attachmentCount, isLate, isOldKap,
  modifyStatus, hasMultiLanguageSupport, fundCode, disclosureCategory`.
- **Unverified assumption for the planner to test:** whether an empty `mkkMemberOidList` returns the
  *market-wide* list (needed for a "recent disclosures" feed). Do not assume it; verify with one
  paced probe and record the result.**Index space:** the public site's `disclosureIndex` is **its own current space** (ASELS 2026-06 FR =
`1643141`; dev VYK head = `1231017`). **These are NOT the VYK indices.** The two sources' indices must
never be mixed in one cursor/column.

### 2. `GET https://www.kap.org.tr/tr/api/notification/export/excel/{disclosureIndex}` — full financial report

- Content-type lies (`application/vnd.ms-excel`) — the body is **HTML** (~1.8 MB for ASELS 2026-06).
- Structure: **731 `<table>` elements, 1,467 `<tr>` rows** — the complete XBRL-tagged report rendered
  as tables. Verified content includes:
  - Header metadata: `Gönderim Tarihi:04.08.2026 18:39:41`, `Bildirim Tipi:FR`, `Yıl:2026`,
    `Periyot:2`, `Sunum Para Birimi: 1.000 TL`, `Finansal Tablo Niteliği: Konsolide`,
    `Cari Dönem 30.06.2026`, `Önceki Dönem …`.
  - **`Nakit Akış Tablosu (Dolaylı Yöntem)`** — the indirect-method **cash-flow statement** (line ~887
    of the flattened text), with `Faaliyetlerden Elde Edilen Nakit Akışları`,
    `Yatırım ya da Finansman Faaliyetlerinden Kaynaklanan Nakit Akışlarına Neden Olan Diğer Kalemlere
    İlişkin Düzeltmeler`, `Durdurulan Faaliyetlere İlişkin Net Nakit Akışları`, and the full hedging
    note family.
- **This is the 051 unblock.** The VYK lane had **0 cash-flow statements**; this document carries the
  full CF statement, current-period and comparative.
- Also available per disclosure: `/tr/api/notification/export/word/{id}`, `/tr/api/BildirimPdf/{id}`,
  `/tr/api/file/download/{oid}` (attachments).

### 3. `GET https://www.kap.org.tr/tr/sirket/{ticker}` + `GET /tr/api/search/combined`

Company directory/identity resolution (ticker → company oid). ASELS oid
`4028e4a1413b7ef401413bc2251e0047` = the same oid the `batch-news` endpoint uses. **Note:** the
public-site company oid is a *different identifier space* from VYK's numeric `companyId` (ASELS = 866).
Mapping between them must be stored, not assumed.

## Constraints the adapter MUST honour (non-negotiable, from the standing doctrine)

1. **Politeness/pacing:** this is a public website, not a contracted API. Pace every request
   (propose ≥3 s, configurable), cap pages per pass, use a realistic User-Agent, and never
   parallelise against it. A `robots.txt`/ToS check is part of the ticket: **record what it says
   verbatim**; if anything forbids this, surface it to the user rather than proceeding silently.
2. **Honesty rules unchanged:** no fabricated line items; unmapped company = named+counted skip;
   unfetchable detail = quarantine with the verbatim body; source-down = `failed`; honest-empty =
   skip with reason. **The excel-HTML is a different evidence class from VYK JSON** — label it as
   such in the store (`source` column) so a row's provenance is always answerable.
3. **Tests on doubles only** — record request/response fixtures from these probes as the committed
   doubles (redact nothing that isn't a credential; there are none).
4. **Two-source discipline:** VYK adapter stays wired and working (dev snapshot). The new lane must
   be a **separate source** selectable by config, with its own cursors and provenance. When/if prod
   VYK access arrives, switching back must be a config change, not a rewrite.
5. **Parser honesty:** HTML table extraction of financial line items is inherently messier than
   VYK's structured JSON. Every decoded line item must be traceable to its table/label; a value we
   cannot attribute is an honest-null, never a guess. Turkish label normalisation (e.g. thousands
   separators `.`/`,`, the `1.000 TL` presentation unit) must be pinned by tests with real fixtures.

## Blast radius / what this unblocks

- **051** (cash-flow decoder): the CF statement exists in the source. The decoder target becomes
  this lane's HTML tables.
- Statement coverage (25 instruments @ 2023 → all constituents @ current), multiples, sector
  medians, market P/E, DCF baselines — all downstream of this lane.
- "Recent KAP data" (disclosures feed) — via `byCriteria` (market-wide shape to be verified).

## Open questions for the planner

- OQ-1: **RESOLVED 2026-10-25 (orchestrator probe):** `byCriteria` with **empty `mkkMemberOidList`**
  returns the **market-wide** list — 12 rows for 2026-10-24→25 (a Saturday), classes `DKB 4 / FR 3 /
  ODA 3 / DG 2`, newest `1687040` (24.10.2026 09:57). Rows carry `stockCodes` + `kapTitle`, so the
  feed is filterable per bound universe client-side. **Caution:** the sample includes **"KAP TEST A.Ş."
  (stockCodes KT)** — a test entity exists in the production feed. The adapter must not special-case
  it silently; if it is excluded, the exclusion is a named, recorded rule (an honest filter with a
  reason code), never a quiet drop.
- OQ-2: Rate/ToS posture of kap.org.tr for automated access (robots.txt verbatim + judgement).
- OQ-3: Should the disclosures lane (078, just merged) also repoint to the public site, or stay on
  VYK dev until prod? (Recommendation: statements/FR first — highest value, clearest parsing;
  disclosures feed second.)
- OQ-4: Mapping public-site company oid ↔ VYK numeric companyId ↔ our instruments (store, don't assume).
