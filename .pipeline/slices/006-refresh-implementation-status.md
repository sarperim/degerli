---
id: SLICE-006
status: done
scope:
  - .pipeline/slices/006-refresh-implementation-status.md
  - IMPLEMENTATION_STATUS.md
  - scripts/implementation-status.sh
  - .pipeline/plan/tickets/TKT-foundation-051.md
---

# SLICE-006: Refresh `IMPLEMENTATION_STATUS.md` and restore green `scripts/implementation-status.sh`

## Context & Goal

`IMPLEMENTATION_STATUS.md` was last verified on `2026-10-25` (before `TKT-foundation-035`, `044..045`, `051`, `061`, `064`, `066..102`, and `SLICE-001..005` landed). Running `./scripts/implementation-status.sh` currently exits `1` with `10 failure(s) out of 157 checks`:

1. Nine tickets listed as `todo` in `## Open tickets` (`TKT-foundation-035`, `066`, `067`, `068`, `069`, `070`, `071`, `073`, `076`) are now `done` in `.pipeline/plan/tickets/`.
2. `TKT-foundation-051` is listed as `todo` in `## Open tickets`, while `.pipeline/plan/tickets/TKT-foundation-051.md` has `- Status: **superseded — 2026-10-25 ...` (where the leading `**` markdown bold before `superseded` also prevents `ticket_class()` in `scripts/implementation-status.sh` from extracting the status token cleanly).
3. Seven rows listed as `in-review` in `## Open tickets` (`TKT-foundation-044`, `045`, `061`, `064`, `074`, `078`) have since merged and flipped to `done` in `.pipeline/plan/tickets/`, and per Update Rule 2 ("must be refreshed on the next landed change") should be removed from `## Open tickets`.
4. Several prose claims in `IMPLEMENTATION_STATUS.md` are outdated relative to landed tickets/slices (notably `TKT-foundation-086`'s recorded note that `IMPLEMENTATION_STATUS.md` still claimed the API served `currentCapital` under `sharesOutstanding`, `TKT-foundation-081..089` landing the `kap-public` lane and FCF/DCF decode, and `SLICE-002..004` unifying stock/DCF endpoints onto `EodSessionFreshness` and wiring `kap-public` in compose).

This slice refreshes `IMPLEMENTATION_STATUS.md` (and fixes the leading-asterisk parsing in `scripts/implementation-status.sh` and `.pipeline/plan/tickets/TKT-foundation-051.md`) so `./scripts/implementation-status.sh` and `./scripts/implementation-status.sh --self-test` pass with zero failures.

## Rules & Invariants

- All required sections (`## Status vocabulary`, `## Domain status`, `## Live-criteria ledger`, `## Open tickets`, `## Update rule`) and HTML comment markers (`<!-- status:domains -->`, `<!-- status:ledger -->`, `<!-- status:tickets -->`) in `IMPLEMENTATION_STATUS.md` must remain intact.
- Every `Checks` token in `## Domain status` (`dir:`, `file:`, `config:`, `config-empty:`, `noconfig:`, `ticket:`) must resolve against the repository and `src/Api/appsettings.json`.
- Every `live` or `live-degraded` domain row must cite at least one valid `LC-nn` row in `## Live-criteria ledger`, and every `live` domain row must cite at least one `LC-nn` row with a `YYYY-MM-DD` dated observation.
- Every `LC-nn` ledger row's `Last live observation` cell must start with `YYYY-MM-DD` or contain `not yet observed` / `never observed`.
- `## Open tickets` (`<!-- status:tickets -->`) must match the exact set of non-terminal tickets in `.pipeline/plan/tickets/TKT-*.md` (where terminal is `done`, `superseded`, or `cancelled`).
- Update `Last verified:` in `IMPLEMENTATION_STATUS.md` to `2026-11-03`.
- In `scripts/implementation-status.sh`, `ticket_class()` should strip optional leading markdown `*` characters after `- Status:` (and `.pipeline/plan/tickets/TKT-foundation-051.md` line 13 should move `**` after `superseded — `) so markdown-emphasized status lines parse cleanly. Add a `--self-test` case in `scripts/implementation-status.sh` covering `- Status: **superseded — ...`.

## Blockers & Dependencies

- Depends on `SLICE-003` (PR #175), `SLICE-004` (PR #176), and `SLICE-005` (PR #177) — all merged to `dev`.

## Verification

- `./scripts/implementation-status.sh` exits `0`.
- `./scripts/implementation-status.sh --self-test` exits `0`.

## Result

All tasks in `SLICE-006` completed and verified:
1. Updated `.pipeline/plan/tickets/TKT-foundation-051.md` line 14 to place markdown bolding after `superseded — `.
2. Updated `scripts/implementation-status.sh` `ticket_class()` to strip optional leading asterisks (`*`) following `- Status:` when parsing status tokens, and added a self-test case asserting markdown-bolded terminal statuses parse as terminal.
3. Updated `IMPLEMENTATION_STATUS.md`:
   - Updated `Last verified:` to `2026-11-03`.
   - Updated the top callout to note `kap-public` default wiring (`SLICE-003`) alongside the VYK DEV gateway code fallback.
   - Refreshed domain status evidence cells (`Financial statements`, `KAP disclosures`, `Share counts`, `Fundamentals serving`, `Valuation and DCF`) to reflect landed changes (`TKT-foundation-081..101`, `SLICE-001..005`) while keeping all `Checks` tokens intact.
   - Updated live-criteria ledger rows `LC-08` and `LC-09` with dated 2026-10-26 live observation records and `LC-19` with stock/DCF `EodSessionFreshness` unification notes.
   - Synchronized `## Open tickets` with the exact 10 non-terminal tickets (`TKT-foundation-056`, `TKT-foundation-065`, `TKT-mov-009`, `TKT-acc-001`, `TKT-mdf-001`, `TKT-mov-001`, `TKT-res-001`, `TKT-scr-001`, `TKT-val-001`, `TKT-mdf-019`).
   - Updated `## Known gaps and parked items` to describe the current state of KAP gateway fallback, share counts, FCF/DCF inputs, and holiday freshness.
4. Verified:
   - `./scripts/implementation-status.sh` exits `0` (141 checks passed).
   - `./scripts/implementation-status.sh --self-test` exits `0` (19/19 cases passed).

