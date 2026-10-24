---
scope:
  - tools/source-probes/
  - tests/Core.UnitTests/SourceContractProbeTests.cs
  - docs/source-contract-probes.md
---

# TKT-foundation-068: Source-contract probes — live shape-drift detection against the committed doubles

- Status: todo
- Size: M
- **Priority: P2 — normal (USER DECISION D6, 2026-10-24: 056 item 3 unparked).**
- **Evidence (gathered 2026-10-24 — the drift incidents are real, not hypothetical):**
  - **İş daily feed vs chart feed (2026-10-23, TKT-foundation-047 live criterion):** the live `HisseTekil` response for the current session returned an **empty `value` array** for all 100 symbols (run 218: every symbol honestly `skipped`), while the Yahoo chart feed carried the session. No committed double predicted that a valid-looking envelope could be empty for the current session; the source choice flipped at runtime.
  - **KAP `ER005` stale-cursor class (2026-10-23, TKT-foundation-063):** the real gateway answered the persisted-cursor indices with **HTTP 400 `{"code":"ER005","message":"Bildirim bulunamadı."}`** for 22–24 of 30 companies per run (runs 212/227/233/236/244), while CI's doubles answered the requested indices normally. The class was invisible to the test suite.
  - **KAP base-path bug (TKT-foundation-032, `v1.5.10`):** every real call 404'd because `/api/vyk` was dropped by URI resolution, while CI was green — the L2 doubles bind at the server **root**, where root-relative and base-relative resolution coincide. A root-bound double cannot catch a prefix-carrying gateway.
  - **VYK `/memberSecurities` unit question (TKT-foundation-058/065):** the served value is `currentCapital` (paid-in TL); 64 of 556 rows carry sub-unit precision. Only live probing could settle it; the committed double could not discriminate a count from money.
  - The committed doubles are deliberately **shape-faithful captured bodies** (`01` §10.9), but nothing re-checks the live shape against them after the capture date.
- **Pinned approach:**
  1. A **probe harness** (not a required CI check) with one probe per source: İş prices (`HisseTekil`), the Yahoo chart contingency, KAP `/disclosures` + `/disclosureDetail`, `/members`, `/memberSecurities`, EVDS macro, TEFAS funds. Each probe:
     - makes **one bounded, paced** real request (honour the existing pacer intervals — KAP ≥12 s, İş ≥3 s);
     - compares the **structural shape** only (required keys, types, envelope discriminators, status semantics) against the committed double's shape;
     - emits a machine-readable drift report: source, field, expected vs observed, timestamp, redacted body excerpt.
  2. **Never values, never raw bodies.** Values change daily; the probe asserts shape, not data. Secrets are redacted; no payload is retained.
  3. **Never auto-edits a double.** A double is a contract artifact; drift produces a report + a routing ticket, and a human updates the double with a fresh capture (the §10.9 capture discipline).
  4. **Run mode decision to record before implementation:** manual CLI (recommended minimum), a scheduled run on the dev box, or an admin-triggered endpoint. Whatever is chosen, it must **not** gate PRs — live sources are rate-limited and legitimately flaky.
- **Acceptance (explicit criteria):**
  - **(i)** One probe per listed source runs paced and writes a report; a deliberately drifted local fixture (a double mutated in a unit test) is flagged field-by-field, and an unchanged shape is clean.
  - **(ii)** The harness is not wired into required CI (no PR trigger); the doc states why.
  - **(iii)** Secrets are redacted and raw bodies are not retained or committed.
  - **(iv)** The runbook (`docs/source-contract-probes.md`) names the coverage set, the run modes, the report location and the drift→ticket flow.
  - **(v)** No architecture amendment expected; if `01` §10.9's posture text changes, append a §13.1 row at that time (next free above the table max).
- Traces to: FR-MDF-008 (provenance), FR-MDF-010/011 (honest achieved depth), `01` §10.9 (real-source envelope contracts), test strategy §9.4 (recorded verification).
- Architecture refs: `01-system-architecture.md` §10.9 (envelope contracts and capture discipline), §6.1 cited (why live probes are not CI checks).
- UX refs: none.
- Dependencies: none.
- Parallel group: **P-1** (scopes disjoint from 074/075/076; no shared files).
