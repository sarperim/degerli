---
scope:
  - src/Ingestion/Statements/StatementsJob.cs
  - src/Ingestion/Statements/KapPublicReportDecoder.cs
  - src/Ingestion/Statements/FinancialStatementStore.cs
  - src/Ingestion/Validation/QuarantineReason.cs
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - tests/Api.IntegrationTests/KapPublicReportDecoderTests.cs
  - tests/Api.IntegrationTests/KapPublicStatementsIngestTests.cs
  - .pipeline/testing/market-data-foundation.md
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-101: GUBRF statements SCHEMA_MISMATCH — the observability fix, the diagnosis, and either the decoder gap or the honest refusal

- Status: done (PR #163 squash-merged a2d1319; round-1 verdict MERGEABLE — v1.5.79 collision renumbered to v1.5.80, additive decoder verified, ASTOR capture honestly labelled; open nits DR-1..DR-5 recorded, DR-3 one-line revert + DR-5 wording to a sweep; LIVE CRITERIA PENDING: orchestrator re-walk of bare-currency companies + 292-quarantine bounded replay + GUBRF real diagnosis)
- Size: M
- **Priority: P1 — the user's 2026-10-29 "complete KAP data displayed" criterion needs GUBRF's fetched reports either stored or honestly refused with a reason that names the cause; today they are refused with a bare word and no evidence.**
- **The finding (live, 2026-10-28/29):** GUBRF's successfully-fetched report bodies (5 × ~1.3 MB, excel export) are refused by the statements job with `SCHEMA_MISMATCH` — the log line "statements: refused public-site payload for GUBRF (SCHEMA_MISMATCH)" (`StatementsJob.cs:578`) carries **no detail**, and the `quarantined_facts` rows since 2026-10-26 carry **NULL payloads** because the refusal path passes `null` evidence (`StatementsJob.cs:576` — `QuarantineAsync(..., null, reasonCode, ...)`). **292 `SCHEMA_MISMATCH` quarantines on 2026-10-28 across companies, some predating the 093 merge (01:00–06:00 UTC under the old decoder) — not a pure regression, but GUBRF is fully refused and undiagnosable from what the system kept.** Hypothesis to verify, never assume: GUBRF may publish a non-standard Excel layout the excel-HTML decoder's shape validation refuses.
- **What to implement (investigation ticket — diagnosis first, the fix follows the evidence):**
  1. **The observability fix (lands regardless of the diagnosis):** the refused-payload quarantine path carries its **evidence** — the payload or a bounded excerpt (the ~1.3 MB bodies get a bounded trim, the capture doctrine) — and the log line names the reason's **detail** (which validation refused it), so a future refusal is diagnosable from the system's own record. The NULL-payload gap is closed for this path, not papered over.
  2. **The capture (orchestrator-owned, paced, quota-gated):** one GUBRF report body committed as a bounded-trim double — captured when the export quota resets (the relay mitigation makes this one paced request; if a body already sits at `/tmp/opencode/captures/`, it is committed from there and no new request is made). No CI test ever calls kap.org.tr.
  3. **The diagnosis, recorded on the ticket against the committed body:** which shape check refuses it, and whether that refusal is right. Then **one of two outcomes, ruled by the evidence and pinned as the ticket's recorded conclusion:** (a) **a decoder gap fix** — the body is a legitimate FR report shape the decoder should accept (the fix lands with the GUBRF body as a golden double; additive only, no map entry guessed, the 093/098 map and fail-safes frozen input); or (b) **an honest refuse-with-reason** — GUBRF publishes a layout the doctrine genuinely refuses, in which case the bare `SCHEMA_MISMATCH` becomes a **named, specific reason** (a new closed `QuarantineReason`, documented in `01` §10.9.19) that names the shape divergence — never a wrong parse and never a silent hole.
  4. **The re-walk (the 092 replay doctrine):** once the refusal is fixed or honestly named, the affected quarantines re-walk once, bounded and persisted — the recorded replay rule, no new ladder.
- **Pinned non-goals:** no map entry, canonical code or fail-safe change beyond what the diagnosed gap requires (the 093/098 decoder surface is frozen input to the diagnosis); no store/schema change beyond the evidence the quarantine row already carries (the payload column exists — this ticket writes it); no serving/UI work; no capture beyond the one GUBRF body; no attempt to make the decoder accept everything (an honest refusal with a specific reason is a valid outcome).
- Traces to: FR-MDF-003 (statement decode correctness — a full company refused undiagnosed), FR-MDF-012 (named/counted skips — a bare `SCHEMA_MISMATCH` with NULL evidence names nothing), NFR-MDF-001 (no silent ledger hole — the NULL-payload rows are holes in the evidence, not just the data), FR-MDF-016; TKT-foundation-092 (the replay doctrine the re-walk rides), TKT-foundation-093/098 (the decoder surface this diagnoses).
- Acceptance: **TC-MDF-128** (test plan §39, v1.52 reservation — planner-authored per the lane precedent, test-planner confirmation pending) — L2 doubles only, zero live requests: (a) the observability fix — a refused public-site payload writes a quarantine row **with evidence** (payload non-null, bounded) and the log line names the reason's detail (asserted); (b) the committed GUBRF body produces the **recorded ruled outcome** — the fixed golden decode (case (a) of the diagnosis) or the specific named refusal (case (b)) — whichever the diagnosis ruled, pinned as the case either way, never left as a bare `SCHEMA_MISMATCH`; (c) non-regression — the five committed bodies and every 093/098 decoder golden unchanged; the statements-lane suites re-run green. Red→green recorded (red = the observability gap itself: the current head's refusal row carries NULL and the log names nothing).
- Architecture refs: `01-system-architecture.md` §10.9.19 (the quarantine/observability rule and — if the diagnosis rules a new reason — the closed reason's entry) + §13.1 **v1.5.80** (re-pinned at merge — the branch's change-record row); test plan §39 (TC-MDF-128).
- UX refs: none (ingest lane).
- Dependencies: TKT-foundation-093/098/094 merged (PR #155/#156/#157 — verified on dev). **No code-scope overlap with any in-flight or queued ticket** (096's PR #158 scope is the metrics lane; 095/097 are Web/UI — verified against their frontmatter). The capture step waits on the orchestrator (quota-gated) — the observability fix and test scaffolding can proceed without it.
- Parallel group: none.
- **Live criteria (orchestrator, post-merge, paced, never CI):** GUBRF's five reports written (or honestly refused under the specific named reason), counts verbatim; the affected companies' quarantines re-walked once — replayed/recovered/still-quarantined counts verbatim.

---

## Implementation record (2026-10-29, implementation lane)

**PR:** https://github.com/sarperim/degerli/pull/163 (branch `tkt-foundation-101`, against `dev`).

### The diagnosis, recorded before any code changed — and it corrects the ticket's hypothesis

The body the orchestrator's paced pass produced (`/tmp/opencode/captures/kap-export-GUBRF-1605802.json`,
1,283,696 bytes) is **not a GUBRF body**: it carries **ASTOR ENERJİ A.Ş.**'s heading and disclosure
index **1605802** (submitted 2026-05-11, Yıl 2026 / Periyot 1 → `3 Aylık`, `Bildirim Tipi: FR`,
`Finansal Tablo Niteliği: Konsolide`). **The "GUBRF may publish a non-standard Excel layout"
hypothesis is disposed of for this body:** it is the ordinary excel-HTML shape
(`financial-table`, `taxonomy-field-title`) the decoder has always read — a full decode of the
untrimmed body produced BS 113 / IS 66 / CF 78 lines with **zero conflicts** and no shape refusal.

The refusal was **one field**: `Sunum Para Birimi: TL` — a currency and **no multiplier**.
`PresentationUnitPattern` (`^(?<number>[\d.,]+)\s*(?<unit>\S+)$`) cannot match a bare currency, so
`ParsePresentationUnit` returned `(null, null)`, the header's `UnitScale`/`Currency` were null, and the
store's mandatory-declaration clause refused the whole filing as `SCHEMA_MISMATCH` — with a `null`
evidence payload, which is why 292 refusals on 2026-10-28 (GUBRF's five among them) were
undiagnosable from the system's own record.

### The ruled outcome: case (a), the decoder gap fix

A declaration naming a currency and no multiplier states **one unit per currency unit** — the store's
`units` scale — and is not a missing declaration; inferring thousands or millions for it would be the
guess `02` §3.1 forbids. `ParsePresentationUnit` now reads the bare form through a shared
`CurrencyFor` helper used by **both** halves, so they cannot drift; every multiplier-bearing
declaration keeps its existing reading, which is why the change is additive and **no committed body's
decode moves**. No new closed `QuarantineReason` was needed (`QuarantineReason.cs` is **untouched**):
case (b) was the alternative ruling, not the finding.

### What landed

1. **`KapPublicReportDecoder.cs`** — `ParsePresentationUnit` reads a bare currency as `units`;
   `CurrencyFor(string)` extracted and shared by both branches.
2. **`FinancialStatementStore.cs`** — `DeclarationFailure(payload, out detail)` extracted as the
   **single** predicate shared by `UpsertAsync`'s refusal and the new public
   `DescribeRefusal(payload, reasonCode, presentationUnit)`, which names the specific clause that
   fired (declaration, conflicting value, absent FX series, restatement without a date, unknown
   version word, unknown identity) and quotes the filing's own declaration when the caller has it.
   No behaviour change to the refusal itself.
3. **`StatementsJob.cs`** — the refusal path no longer passes `null`: `RefusalEvidence` writes a
   **bounded digest** of the decode (symbol, index, source ref, reason, `detail`, the filing's
   identity block verbatim, decode counts, first 12 findings with 240-char excerpts — a few KiB
   against a ~1.3 MB body), and the log line names the reason's detail. The evidence is the decode's
   digest rather than the raw HTML because the sink receives a `KapPublicReport`, not the body.
4. **`RealSourceCatalog.cs`** — the committed capture `kap-public-report-1605802`, a bounded trim
   (header block + the full BS table `tbl_general_role_210015` + the full IS table
   `tbl_general_role_310003`; CF and equity tables omitted whole), labelled **honestly as ASTOR's
   body, index 1605802, not a GUBRF body**.
5. **Docs** — test plan §39 (TC-MDF-128, v1.56); `01` §10.9.19's new subsection and §13.1 v1.5.80.

### Acceptance: TC-MDF-128 (all three sub-cases shipped, L2 doubles only, zero live requests)

- **(a)** `KapPublicStatementsIngestTests.TC_MDF_128_a_a_refused_payload_keeps_bounded_evidence_and_the_log_names_the_detail`
  — a refused payload (the committed interim body with `1.000 TL` → `1.000 QAR`) is `partial`,
  writes nothing, counts three quarantines (the refused interim plus the two the double answers a 500
  for); the `SCHEMA_MISMATCH` row's `payload_json` is non-null, under 16 KiB, and names the symbol,
  index, reason, the filing's `presentationUnit` `1.000 QAR` with `unitScale` null, the decode counts
  and the `detail`; the log line contains that same detail.
- **(b)** `KapPublicReportDecoderTests.TC_MDF_128_b_a_bare_currency_declaration_reads_as_units_and_the_filing_decodes`
  — `TL` → `units`/`TRY`, zero `PRESENTATION_UNIT_UNRECOGNISED`, the filing's identity, two BS + two
  IS statements with no conflicts and pinned values (`TOTAL_ASSETS` 58.630.702.726,
  `TOTAL_EQUITY` 38.403.565.895, `REV` 9.286.457.701, `GROSS_PROFIT` 3.524.361.357), and the store
  **accepts** the payload (four inserted, no rejected reason codes).
- **(c)** `KapPublicReportDecoderTests.TC_MDF_128_c_the_five_committed_bodies_keep_their_decodes`
  — the five pre-existing bodies keep `thousands`/`millions`, their line/statement/finding counts
  (116/3, 346/20, 383/35, 314/22, 497/41) and null `ConflictingItemCodes`.

### Red → green, measured (the red re-run, not remembered)

With only the three product files (`KapPublicReportDecoder.cs`, `FinancialStatementStore.cs`,
`StatementsJob.cs`) reverted to the pre-ticket head — models, fixtures and every assertion kept, so the
red is behaviour and never a compile or environment failure — the `TC_MDF_128` filter reads
**2 failed / 1 passed**:

- (b) `Assert.Equal() Failure: Expected: "units", Actual: null` on `UnitScale` — the decoder gap;
- (a) `Assert.NotNull() Failure: Value is null` on the quarantine row's `PayloadJson` — **the
  NULL-evidence hole this ticket closes**;
- (c) passes **by construction** (it pins the five bodies' unchanged goldens), recorded as such rather
  than manufactured red.

**3/3 green** with the fix restored.

### Suites run (targeted, not the whole suite)

- **81/81 green** — `KapPublicReportDecoderTests | KapPublicStatementsIngestTests |
  RealSourceStatementsTests | StatementNormalizationTests | StatementVersioningTests`.
- **50/50 green** — the neighbours the catalog addition touches: `RealSourcePricesTests` (the catalog
  `Names`/`Build` contract), `CannedSourceCatalogTests`, `KapPublicRecomputeTests`,
  `KapPublicSourceTests`.
- `dotnet build` — **0 warnings / 0 errors**.

### Flagged items (nothing hidden)

- **The capture is ASTOR's, not GUBRF's.** The orchestrator's file was mis-named. It is committed
  under its true identity and its label says so; no GUBRF fact is manufactured, and the ticket's live
  criteria (GUBRF's five reports written or honestly refused, the 2026-10-28 quarantines re-walked)
  remain the orchestrator's post-merge step.
- **The evidence is a bounded digest of the decode, not a raw-body excerpt.** The sink receives a
  decoded `KapPublicReport`; carrying the HTML would mean changing the walker/report model, which is
  outside this ticket's scope and would not record more than the decode already does about *why*.
- **The re-walk half (item 4) is not executed here:** it rides TKT-foundation-092's recorded replay
  rule and is the orchestrator's post-merge paced step, as the ticket's live-criteria bullet states.
- **Scope:** every touched file is on the ticket's `scope:` list; `QuarantineReason.cs` was not needed
  and was not touched; no map entry, canonical code, fail-safe, schema, or serving file was touched.
