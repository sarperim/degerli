# TKT-foundation-002: Web SPA scaffold, i18n catalogs & app shell

- Status: done
- PR: https://github.com/sarperim/degerli/pull/1
- Size: M
- Scope: create `/src/Web`: Vite + React 19 + TypeScript; Tailwind CSS v4 + shadcn/ui; react-i18next with `tr.json` (default) + `en.json` catalogs; TanStack Query client; zustand draft store persisted to sessionStorage (M-3 skeleton); React Router v7 (library mode) with placeholder routes for SCR-001..012; global header shell (language toggle, auth-state placeholder, stock-search placeholder); `scripts/i18n-parity` script wired into package.json (`check:i18n`). Must NOT touch `/src/Api`, `/src/Core`, `/src/Ingestion`, `/src/ContentPipeline`.
- Traces to: foundation (architecture AD-03, AD-09; i18n discipline §10.6)
- Acceptance (explicit): `npm ci && npm run lint && tsc --noEmit && npm run build` all pass; the parity script fails when a key exists in one catalog but not the other, or is unused in both, and passes on the checked-in catalogs; the shell renders Turkish by default and the EN toggle switches instantly without reload, preserving URL and scroll; all 12 screen routes render placeholders.
- Architecture refs: `01-system-architecture.md` §3 (C1), §5 (frontend framework, data fetching, routing, client state, i18n, UI kit rows), §8.1 (M-3, M-4), §10.6
- UX refs: SCR-001..SCR-012 (routes only — structure placeholders); UXR-G-013, UXR-G-014 (language toggle shell behavior)
- Dependencies: none
- Parallel group: P-1

## Notes

Note: the parity script is the mechanism TC-XC-002 later formalizes as a recorded CI gate (TKT-int-004).
