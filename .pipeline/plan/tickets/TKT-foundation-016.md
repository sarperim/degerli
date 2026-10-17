---
scope:
  - src/Ingestion/Sources/Sources.cs
  - src/Ingestion/IngestionServiceCollectionExtensions.cs
  - src/Api/Program.cs
  - tests/Api.IntegrationTests/SourceClientCharsetTests.cs
---

# TKT-foundation-016: Charset-honoring source client

- Status: done — merged 2026-10-17 (PR https://github.com/sarperim/degerli/pull/88 @ 48acd12). Round-1 escalation G-R1-01 resolved by user-sanctioned ticket wording amendment (83ab357). Non-blocking residue recorded: R2-01 major (BOM handling in DecodeBody, latent), R2-02/R2-04 minor, R2-03/R2-05 routing items (architect/planner) — see reviews/TKT-foundation-016-round-2.md
- PR: https://github.com/sarperim/degerli/pull/88
- Size: S
- Scope: `ISourceClient` honors the response's declared `Content-Type; charset=` — windows-1254 / ISO-8859-9 (Turkish code pages) decode via the .NET code-pages provider, **registered once at product startup** (the registration currently lives only in the fixtures project and moves into `src/`); no declared or unknown charset → UTF-8, the current behavior. One seam, every adapter benefits — no per-adapter charset sniffing (§10.9.4). Must NOT change adapter parsing, jobs, or the fixtures catalog (this ticket only consumes the charset-faithful catalog bytes landed by the TKT-foundation-015 dependency; the catalog itself is not edited).
- Traces to: FR-MDF-001, FR-MDF-002, FR-MOV-014 (transport correctness of every source path); NFR-MDF-004
- Acceptance: TC-MDF-066
- Architecture refs: `01-system-architecture.md` §10.9.4, AD-14
- UX refs: —
- Dependencies: TKT-foundation-015 (the real-source catalog supplies the charset-faithful bytes)
- Parallel group: P-29
