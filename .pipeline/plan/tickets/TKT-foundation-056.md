---
scope: []
---

# TKT-foundation-056: Parked hardening/improvement backlog — PARTIALLY UNPARKED 2026-10-24 (USER DECISION D6)

- Status: blocked — **the remaining parked set (items 5, 10, 14) only.** The umbrella is no longer the dispatchable record for the unparked items; they are sliced into their own tickets (below).
- Size: L (only if the remaining items are unparked later; sliced then, not now)
- **History:** USER-PARKED 2026-10-22 ("functional first"). **USER DECISION D6 (2026-10-24) unparked items 1, 2, 3, 4, 6, 7, 8, 9, 11, 12, 13.** Each was sliced into its own ticket with current-repo evidence gathered at slice time (not guessed); the unparked items are no longer tracked here.
- **Unparked and sliced 2026-10-24 (USER DECISION D6) — all priority P2/normal except #13 which is P3/LOW by the user's explicit instruction:**
  1. CI hardening (secret scanning, dependency audit, container scanning beyond the current required checks) → **TKT-foundation-066**
  2. Deploy migration safety (readiness checks + rollback posture) → **TKT-foundation-067**
  3. Source-contract probes (live shape-drift detection against committed doubles) → **TKT-foundation-068**
  4. VYK cursor/fact transaction window (accepted residual in the adapters' cursor-before-store ordering) → **TKT-foundation-069**
  6. Metric formula versioning (recompute provenance across formula changes) → **TKT-foundation-070**
  7. OpenAPI contract codegen (contract drift eliminated at build time) → **TKT-foundation-071**
  8. Units branding (display-side unit semantics) → **TKT-foundation-072**
  9. Stale-"why" extensions (the 039 lane's explanatory freshness copy, deeper) → **TKT-foundation-073**
  11. IMPLEMENTATION_STATUS.md (a standing implementation-status surface) → **TKT-foundation-074**
  12. Live-vs-CI verification doc (what CI proves vs what only live criteria prove) → **TKT-foundation-075**
  13. Restore drill (backup restore actually exercised) → **TKT-foundation-076 — P3/LOW per USER DECISION D6.**
- **Still parked — DO NOT DISPATCH (USER DECISION D6 left these alone):**
  5. Scheduler advisory locks (multi-instance safety).
  10. Performance budgets (beyond Xc014's current scope).
  14. A live BIST-calendar source probe as an alternative to 048's static config list (048 ships the static list; the live probe stays parked).
- **§13.1 allocation for the unparked slices:** soft reservations **v1.5.45–v1.5.51** above the landed table max **v1.5.42** and the 064/065 reservations **v1.5.43/44** — specifically 066→45, 067→46, 069→47, 070→48, 071→49, 072→50, 076→51 (conditional). Reservations are **soft** and re-pin at merge; 068/073/074/075 are not expected to amend the architecture docs. See each ticket for the exact mapping.
- Traces to: none (parked record; user decision D6, 2026-10-24). The unparked slices trace to their own FRs/evidence inside each ticket.
- Acceptance: none for this file (not dispatchable). The unparked items are dispatchable through their own tickets; the parked items remain blocked on a future user decision.
- Architecture refs: none.
- UX refs: none.
- Dependencies: none (D6 partially lifted the block).
- Parallel group: none.
