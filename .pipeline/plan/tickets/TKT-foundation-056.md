---
scope: []
---

# TKT-foundation-056: Parked hardening/improvement backlog — USER-PARKED 2026-10-22 ("functional first")

- Status: blocked
- Size: L (when unparked; sliced then, not now)
- **Priority: PARKED BY USER DECISION 2026-10-22 — DO NOT DISPATCH.** The user reviewed the hardening/improvement list and explicitly parked it: functional work first; this backlog is sliced into real tickets only when the user unparks it. This file is the durable record of the parked set (board views are regenerable; this is committed).
- The parked items (each becomes its own sliced ticket on unpark, with evidence gathered then):
  1. CI hardening: secret scanning, dependency audit, container scanning (beyond the current required checks).
  2. Deploy migration safety: readiness checks + rollback posture.
  3. Source-contract probes (live shape drift detection against committed doubles).
  4. VYK cursor/fact transaction window (the accepted residual in the adapters' cursor-before-store ordering).
  5. Scheduler advisory locks (multi-instance safety).
  6. Metric formula versioning (recompute provenance across formula changes).
  7. OpenAPI contract codegen (contract drift eliminated at build time).
  8. Units branding (display-side unit semantics).
  9. Stale-"why" extensions (the 039 lane's explanatory freshness copy, deeper).
  10. Performance budgets (beyond Xc014's current scope).
  11. IMPLEMENTATION_STATUS.md (a standing implementation-status surface).
  12. Live-vs-CI verification doc (what CI proves vs what only live criteria prove).
  13. Restore drill (backup restore actually exercised).
  14. (Added 2026-10-22, from the auto-update wave's planning:) a live BIST-calendar source probe as an alternative to 048's static config list.
- Traces to: none (parked record; user decision 2026-10-22).
- Acceptance: none (not dispatchable). Unparking = the user asks the planner to slice items from this list; each slice traces to its own evidence then.
- Architecture refs: none.
- UX refs: none.
- Dependencies: none (blocked on the user's unpark, by design).
- Parallel group: none
