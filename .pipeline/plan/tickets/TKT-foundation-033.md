---
scope:
  - src/Ingestion/Statements/VykStatementDecoder.cs
  - src/Ingestion/Statements/VykModels.cs
  - src/Ingestion/Statements/StatementsSourceAdapter.cs
  - src/Ingestion/Kap/VykModels.cs
  - fixtures/Degerli.Fixtures/CannedSourceCatalog.cs
  - tests/Api.IntegrationTests/VykStatementsSourceTests.cs
  - .pipeline/architecture/01-system-architecture.md
---

# TKT-foundation-033: VYK share-count mapping — produce `SHARES_DILUTED` from the VYK disclosure layer so the market P/E blocks can go live

- Status: superseded (2026-10-22 — by TKT-foundation-058, USER GRANT autonomous unblock; see the supersede note at the end)
- Size: M
- **Priority: authored, NOT dispatch-ready.** The 2026-10-19/20 routing expects this blocked — see the blocked reason. Do not dispatch until the blocker clears (this is a network/env blocker plus a missing real reference shape, not a code unknown).
- Blocked reason (2026-10-20, from the live-run session): **(a) the production MKK gateway `apigw.mkk.com.tr` is NXDOMAIN from this environment** (a long-standing pin) and **(b) the reachable dev gateway is a frozen March-2023 archive whose payloads do not contain the fact we need to map.** Live-verified: no `SHARES_DILUTED` line item is produced from the VYK list+detail layer — `ifrs-full:NumberOfShares` is absent from **all stored AND quarantined payloads** of the 2026-10-19 first real statements run (61 statements + 2,970 line items, 22 + 1 quarantined). There is no real reference shape to build a faithful mapping against, and a mapping validated only against constructed bodies would be exactly the fabrication-adjacent work the honesty doctrine forbids. **Unblock condition:** `apigw.mkk.com.tr` resolvable from a networked environment (the run report's standing "pin the KAP production host" item), after which a bounded live capture of one share-bearing FR disclosure supplies the real shape. Note TKT-foundation-031 makes the overview live WITHOUT this ticket (honest-null P/E blocks); this ticket is what later flips those blocks to real values.
- Scope: **map the VYK disclosure detail's share-count fact onto the canonical `SHARES_DILUTED` line item** (`FixtureModels.SharesDiluted`, the balance-sheet code `MarketSnapshotEngine` and the metrics engine consume). Investigation-first: determine where the dev/production VYK detail (or its attachment/XBRL layer) carries the share count (`ifrs-full:NumberOfShares` or equivalent — possibly NOT in the `presentation` tree the decoder currently reads, but in an attachment resource), then extend `VykStatementDecoder`/`StatementsSourceAdapter` to emit the line item with the same provenance, quarantine and honest-empty conventions as every other mapped fact (unmapped/missing reported, never dropped). A shape-faithful constructed capture body carrying the fact is added to the L2 catalog **additively** (existing goldens unchanged; any permitted delta pre-listed in the PR).
- Traces to: FR-MDF-002/003 (statement ingestion — the share-count fact), FR-MOV-006 (market P/E / div yield — the consumers), BR-MOV-010 (P/E exclusion doctrine — the share-bearing subset), FR-MDF-008 (provenance on the new fact); TC-MDF statement-ingestion L2 lane re-run green; foundation pseudo-domain (explicit criteria below).
- Acceptance: **explicit criteria per the foundation convention.** **(i) L2 mapping tests:** a constructed (post-unblock: live-captured) disclosure detail carrying the share-count fact → the statements run produces a `SHARES_DILUTED` line item with the correct value, statement type `BS`, and provenance ref; a detail WITHOUT the fact → no line item, honestly absent (no zero, no fabricated row) — red before, green after. **(ii) Downstream integration:** on a world seeded with those facts, `MarketSnapshotEngine` picks the shares up (the existing FU-shaped path — no engine change expected; TKT-foundation-031's partial-shares semantics govern mixed worlds). **(iii) Existing goldens re-run green unchanged** (additive catalog body only; pre-list any permitted delta). **(iv) Live criteria (gated on the unblock condition, orchestrator-triggered, paced):** against the PRODUCTION gateway, one paced XU030 statements run produces real `SHARES_DILUTED` rows (`provenance=kap`), and a subsequent `market-snapshot` run computes a non-null `marketPe` over them. **Pacing: ≥12 s between KAP calls** (free plan; 401-penalty-box behavior on bursts — wait, never retry-storm). Attempt and result recorded on the ticket, never fabricated.
- Architecture refs: `01-system-architecture.md` §10.9.11 (KAP VYK gateway — the disclosure-detail/attachment layer where the fact lives), §10.9.14 (TKT-foundation-031's decoupling doctrine — this ticket flips the share-derived blocks to real values), §13.1 change-record row; `02-data-model.md` (canonical line-item chart of accounts — `SHARES_DILUTED` placement); test plan: the KAP statements L2 lane (`.pipeline/testing/` market-data foundation statements section) + a new mapping-case row.
- UX refs: SCR-001 (market overview valuation block — the served values this eventually populates; rendering already handles both states).
- Dependencies: TKT-foundation-032 (correct URI resolution is a prerequisite for any production-gateway fetch) — and the external unblock condition above.
- Parallel group: none (blocked).

### Investigation result (2026-10-22, decisive — blocker re-classified)
The hosts-file probe (user added `185.53.63.11 apigw.mkk.com.tr` in WSL) resolved the DNS question and produced a definitive answer: **`apigw.mkk.com.tr` is not a functioning production endpoint.**
- Port 80 answers instantly with `302 → https://apigwdev.mkk.com.tr/...` — the "prod" hostname literally redirects to the TEST gateway.
- Port 443 completes a TLS handshake (wildcard `*.mkk.com.tr` cert) but no HTTP service answers for that Host (hangs, 45s+).
- Quad9's A record for the hostname (`185.53.63.11`) is a **dev-gateway node** — verified: the dev hostname on that exact IP serves HTTP 200 in 0.3s.
- Google (8.8.8.8) and Cloudflare (1.1.1.1) return NXDOMAIN.
Conclusion: the earlier "prod gateway NXDOMAIN from this environment" framing was incomplete — the hostname itself carries no production service; whatever DNS view resolves it lands on shared test infrastructure. The real unblock for 033 is **not network access** but obtaining the actual production VYK dissemination endpoint and an appropriate subscription from MKK (the account currently holds a Free-plan on the TEST gateway only, per the .env notes). Until then, share counts remain honestly unavailable on every environment, and the per-share/multiples/DCF-baseline lanes stay blocked behind this ticket.
Operational note: the hosts line is harmless (it only redirects to the dev gateway) but no longer needed; it can be removed.

### SUPERSEDED (2026-10-22, dated correction — USER GRANT: the user is away, autonomous unblock with notice)

**This ticket is superseded by TKT-foundation-058 — the share-count store from VYK `/memberSecurities`.** New live-probe evidence (2026-10-22, orchestrator, dev gateway + existing Free-plan creds) disproved the blocker's premise and replaced the design:

- `GET /api/vyk/memberSecurities` serves **671 members / 1,062 security lines, `currentCapital` populated on 1,061** — including ASELS (A+B tertip = 4.56B combined, matching Aselsan's real count), THYAO 1.38B, TUPRS 1.93B, GARAN 4.2B, AEFES 592M. Capture: `/tmp/opencode/member-securities.json` (ephemeral; 058 commits the shape-faithful double).
- The 2026-10-22 host probe (above) already established there is **no separate production endpoint** (302 to the test gateway) — the source is the gateway we already ingest from, via an endpoint not previously in our pipeline.
- The old design — mapping `SHARES_DILUTED` from disclosure-detail XBRL (`ifrs-full:NumberOfShares`) — is **dead on the evidence**: the fact is absent from all stored and quarantined payloads, and the members-securities route carries the counts directly. 058 keys the store on the trading-open security lines with the unit question cross-validated live against known real counts (never assumed) and honest absence for unmatched instruments.

Everything below this note is the historical record of the superseded design and its blocker analysis. Do not dispatch this ticket; dispatch 058.
