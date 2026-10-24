---
scope:
  - .pipeline/plan/tickets/TKT-foundation-065.md
  - .pipeline/architecture/01-system-architecture.md
  - .pipeline/testing/market-data-foundation.md
---

# TKT-foundation-065: Share-count unit — the served value is `currentCapital` (paid-in capital), not a proven share count; decide the contract

- Status: partially resolved — **USER DECISION D4 = OPTION A recorded verbatim (2026-10-24); display half landed, contract half still owed.**
  - **The decision, verbatim:** *"D4 (065 share-count unit): A — relabel as paid-in capital (TRY); no `sharesOutstanding` claim from that lane."*
  - **Landed half (PR #137, TKT-foundation-072):** the **display** side is done — the served figure is labelled **"Ödenmiş sermaye" / "Paid-in capital"** with a localized **TRY** unit, and the old "Ödenmiş pay sayısı" / "Shares outstanding" copy was removed from both TR and EN catalogues. No UI surface now reads it as a count.
  - **Still owed (why this is not `done`):** acceptance (ii) also requires the **contract and model text** to stop claiming a count — `01` §10.9.11 corrected to paid-in capital, `03-api-design.md` §3 `shareCount` block renamed/aliased, `02-data-model.md` §3.1 `share_counts` column semantics reconciled, plus an implementation slice with exact file scope. **072 was presentation-only by design and explicitly changed no server contract or value**, so the API still serves `shareCount.sharesOutstanding` naming a monetary `currentCapital` figure. That is the remaining honesty gap and it needs its own ticket.
  - **Note for the planner:** the reserved §13.1 **v1.5.44** row was consumed by another ticket while this was blocked; the contract half must allocate above the landed max rather than reuse 44.
- Size: S
- **Priority: P2 — 058 follow-up / contract honesty (USER GRANT 2026-10-23 afternoon).** The `shares` lane is landed and serving (`shareCount.sharesOutstanding`), but the live unit cross-validation did **not** pass; the contract currently names a monetary capital figure as a share count.
- **Verified evidence (live, dev stack, 2026-10-23):**
  - **556 instruments ingested** by the `shares` run; five majors **match the known real counts**: ASELS 4,560,000,000 · THYAO 1,380,000,000 · TUPRS 1,926,795,598 · GARAN 4,200,000,000 · AEFES 592,105,263.
  - **64 of 556 rows carry sub-unit precision**, e.g. **LYDYE 1,831,580.69**, **AYCES 7,181,451.329**. A share count cannot carry fractions of a share in this store; the field is **money** (`currentCapital`, paid-in capital in TL) and equals a share count **only where the nominal value is 1 TL**.
  - The stored value is the sum of `/memberSecurities` `currentCapital` lines (tertip aggregation, 058); the API serves it as **`GET /api/v1/stocks/{symbol}/balance-sheet` → `shareCount.sharesOutstanding`**. The five-major agreement is therefore **evidence of nominal = 1 TL for those five**, not proof of a count for the other 551.
  - `share_counts.shares_outstanding` is `numeric(18,4)` — it preserves the fractional (monetary) values, so nothing in the store or transport is silently rounding them away.
- **The decision (planner pins the options honestly; the user chooses):**
  - **Option A — relabel the contract as paid-in capital (PLANNER-RECOMMENDED, no new source needed).** The served field becomes paid-in capital, not a share count: `shareCount.paidInCapital` (or `currentCapital`) + `asOf`, unit **TRY**, and the docs/`03` contract/`02` column semantics (or an additive API alias, if the column stays) state exactly what the source provides. `sharesOutstanding` stops being claimed from this lane until a real count exists. Downstream count consumers (per-share metrics, multiples/market-P/E, DCF baselines) stay blocked — they already are. This is the only option that is true today without inventing a divisor.
  - **Option B — serve a count with a per-instrument nominal divisor (CONDITIONAL — needs an evidenced nominal source).** `sharesOutstanding = currentCapital / nominal`, but **only** where the per-instrument nominal is sourced and recorded (the `/memberSecurities` capture carries `capital`/`currentCapital` — both monetary; it carries **no nominal**). A default `1 TL` is **forbidden** except where evidenced; a company without a proven nominal serves honest-null; the divisor and its provenance are stored per row (or on the contract) and the test pins both the divisor path and the honest-null path. If no nominal source exists, Option B cannot ship honestly.
  - **Option C — interim minimal (fallback).** Keep the store and serve **both**: `shareCount.paidInCapital` (the real value) **and** `sharesOutstanding: null`, explicitly "not yet sourced", until Option B's nominal is available. Never serve a money value under a count name.
- **Pinned rules regardless of direction:** never fabricate a share count; never divide by an assumed nominal; never zero-fill; no downstream count consumer may treat the current value as a count until the chosen option lands; the unresolved unit stays recorded in §10.9.11 (already) and the ticket's evidence table is permanent.
- Traces to: FR-MDF-002/003 (the `/memberSecurities` lane), FR-MDF-008 (provenance), FR-MDF-011 (honest public surface), FR-MOV-006 (market P/E / per-share supply — **stays blocked**), BR-MOV-010 (P/E exclusion doctrine), FR-RES-0xx financials lane (per-share display supply — stays blocked), FR-VAL-001 lane (DCF baseline supply — stays blocked); §10.9.11 (the `shares` lane + unit-unresolved record), §13.1 **v1.5.44 (reserved above landed max v1.5.41; the row lands with the decision/implementation)**; TKT-foundation-058 (the landed lane this follows up), TKT-foundation-033/031 (the honest-null history).
- Acceptance: **explicit criteria (decision gate; docs only in this ticket).**
  - **(i) User decision recorded verbatim on this ticket:** **A**, **B** (with an evidenced nominal source named), or **C**.
  - **(ii) Option A:** `01` §10.9.11 and the `03` stock payload contract are corrected to paid-in capital (unit TRY) with no `sharesOutstanding` claim from this lane; the `02` column semantics are reconciled (rename or documented-as-capital); a follow-up implementation ticket is sliced with exact file scope (API contract + store naming + tests).
  - **(iii) Option B:** the evidenced nominal source is named and captured; the implementation slice pins `count = currentCapital / nominal`, per-row divisor + provenance, honest-null where unproven, and red→green tests for both branches; no default-1-TL path exists.
  - **(iv) Option C:** the interim dual-field contract (`paidInCapital` + `sharesOutstanding: null`) is documented and sliced; the count remains honestly absent.
  - **(v) Evidence table:** the 64/556 sub-unit finding and the five-major table (served value vs known real count) are recorded on this ticket and not restated as a passed validation.
  - **(vi) No code change in this ticket** — the decision + contract text + evidence; implementation follows in its own ticket.
  - **(vii) No new consumer** reads the current value as a share count while the decision is open (verified by search in the lane's consumers).
- Architecture refs: `01-system-architecture.md` §10.9.11 (share counts — the unit-unresolved paragraph; corrected per the decision), §13.1 **v1.5.44 (reserved)**, `03-api-design.md` §3 (the stock payload's `shareCount` block — named as the affected contract; the implementation slice edits it), `02-data-model.md` §3.1 (`share_counts` column semantics — named as the affected model text); test plan: `.pipeline/testing/market-data-foundation.md` §14 (TC-MDF-092/093 remain the lane's cases; the decision adds the nominal/count cases only if Option B is chosen).
- UX refs: none (serving contract; no UI consumer displays it yet).
- Dependencies: **USER DECISION** (blocks all implementation); 058 is landed (PR #123) and 065's evidence comes from its live run.
- Parallel group: none

## Live evidence table (2026-10-23)

| Company | Served value (`currentCapital` sum) | Known real count | Reading |
|---|---|---|---|
| ASELS | 4,560,000,000 | 4,560,000,000 | matches (nominal = 1 TL) |
| THYAO | 1,380,000,000 | 1,380,000,000 | matches (nominal = 1 TL) |
| TUPRS | 1,926,795,598 | 1,926,795,598 | matches (nominal = 1 TL) |
| GARAN | 4,200,000,000 | 4,200,000,000 | matches (nominal = 1 TL) |
| AEFES | 592,105,263 | 592,105,263 | matches (nominal = 1 TL) |
| **LYDYE** | **1,831,580.69** | (not a count) | **fractional → money** |
| **AYCES** | **7,181,451.329** | (not a count) | **fractional → money** |

- **64 / 556 rows carry sub-unit precision.** The five matches prove nominal = 1 TL for those five; they do **not** prove a share count for the other 551. The store and the served field are monetary paid-in capital (`currentCapital`).
