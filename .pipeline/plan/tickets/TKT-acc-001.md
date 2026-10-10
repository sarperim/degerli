# TKT-acc-001: Design — SCR-007 Register, SCR-008 Sign In, SCR-009 Password Reset, SCR-010 Account Settings (visual design)

- Status: blocked
- Deferred (user, 2026-10-10): design implemented directly in the web app (no design tool). This per-domain design-artifact ticket is off the critical path; the domain's UI ticket(s) now build the screens directly against `TKT-foundation-011`. Revisit if a later design-fidelity pass is wanted.
- Size: M
- Scope: visual design for the four account screens in the chosen design tool (D-UX-TOOL): Register (exactly three inputs + reviewable bilingual privacy notice + verification-outcome landing states), Sign In (+ recovery/registration links), Password Reset (request stage with neutral confirmation; set-password stage reachable from an e-mail context), Account Settings (language preference, password change, entry points to saved work, deletion with double-confirmation, verification status + resend). All §5 states per screen. Export to `/design/acc/`. Must NOT touch `/src/**`.
- Traces to: UC-ACC-001..004 (screens hosting these)
- Acceptance (explicit, no TCs — design work): every element and state in `ux/user-accounts.md` (all four screens, §3–§9) is represented; forms show visible password rules and inline validation; the privacy notice is reviewable before consent; deletion confirmation states the consequences; TR + EN copy direction (both languages shown in the design); narrow-width operability incl. the e-mail-context reset stage; builder approves the export; behavioral decisions flow back through the ux-designer.
- Architecture refs: `01-system-architecture.md` §10.1 (auth flows the design must stay compatible with), §8.3 (UXR-ACC rows)
- UX refs: SCR-007, SCR-008, SCR-009, SCR-010; UXR-ACC-001..021
- Dependencies: TKT-foundation-011
- Parallel group: P-7
