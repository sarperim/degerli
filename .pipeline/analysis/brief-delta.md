# Brief Delta — Paste-Ready Text for `.pipeline/00-project-brief.md`

**Purpose:** consolidated, paste-ready text for every post-brief scope decision made at the BA gates (Gate 1: 2026-10-05; Gate 2: 2026-10-06). Analysis documents never modify the brief — the builder applies this delta by pasting. Until pasted, the approved analysis package (`.pipeline/analysis/`) is the authoritative record of these decisions.

**How to apply:** for each row below, find the quoted text in the brief section shown and replace it with the replacement text. Everything is additive — nothing in the approved brief is removed.

## Required changes (scope decisions)

| # | Brief location | Find | Replace with | Provenance |
|---|---|---|---|---|
| D1 | §5, V1 — Stock pages | `valuation (incl. historical valuation);` | `valuation (incl. historical valuation and a vs.-sector comparison of the stock's key multiples against sector medians);` | SD-002, Gate 1 2026-10-05 |
| D2 | §5, V1 — Stock pages | `balance sheet; dividend history` | `balance sheet (incl. book value and book value per share); dividend history` | FR-RES-027, Gate 2 2026-10-06 |
| D3 | §5, V1 — Simple, fully user-adjustable DCF | `— presented in plain language an ordinary investor can understand` | `— presented in plain language an ordinary investor can understand; plus a sensitivity table showing fair value across a grid of key assumptions (e.g., discount rate × growth), and named DCF scenarios saved and reloaded server-side per user account` | SD-001 (Gate 1) + FR-VAL-009/010 (Gate 2) |
| D4 | §5, V1 — User accounts | `**User accounts**: registration/login; saved screens are stored server-side per account` | `**User accounts**: registration/login with e-mail verification and password reset; saved screens and named DCF scenarios are stored server-side per account; users can delete their account (removing personal data and saved items)` | FR-ACC-008/009/006, Gate 2 2026-10-06 |
| D5 | §5, V0 — Fund data foundation | `Fund data foundation: NAV history, performance, holdings where publicly available — data only, no fund UI in V1` | `Fund data foundation: NAV history, performance, holdings where publicly available — data only, no fund UI in V1; the fund universe is bounded to equity funds and equity-heavy mixed funds first (expansion is a post-V1 decision)` | BR-FDF-006, Gate 2 2026-10-06 |

## Optional change (data policies from the MDF gate — recommended, keeps the brief self-contained)

| # | Brief location | Find | Replace with | Provenance |
|---|---|---|---|---|
| D6 | §5, V0 — Historical data as first-class | `ingest depth target: 10 years, or as far back as the sources allow` | `ingest depth target: 10 years, or as far back as the sources allow; price history is stored both raw and corporate-action-adjusted (adjusted is used for all historical analysis and labeled as such on screen); restated financials are stored alongside the original as-reported versions — the latest restated values are displayed, marked with an asterisk, a bottom-of-display footnote, and a warning` | BR-MDF-010/011, Gate 2 2026-10-06 |

## Notes

- MoSCoW levels of the added items: SD-001 (sensitivity table) **Should**; SD-002 (vs.-sector strip) **Should**; book value display **Should**; DCF scenario persistence **Should**; e-mail verification **Must**; password reset **Must**; account deletion **Must**; equity-funds universe bound **Must** (scope bound, not a feature).
- After pasting, the delta rows in `.pipeline/analysis/00-domain-map.md` (§ Scope deltas) remain as provenance records; no further action needed there.
