# TKT-foundation-005: Database schema, migrations & seeds

- Status: todo
- Size: L
- Scope: EF Core 10 + Npgsql migrations for **every** table in `02-data-model.md` §3 (MDF facts incl. `quarantined_facts`; MOV macro + snapshots; SCR `metric_catalog` + `saved_screens`; RES `business_descriptions` + `sector_metric_medians`; VAL `dcf_baselines` + `dcf_scenarios`; ACC Identity schema + `language_pref` + `consent_records`; FDF fund tables; ops `ingest_runs` + `coverage_metadata`); views `v_current_universe`, `v_data_freshness`; seed data (`metric_catalog` 18 rows, `macro_series` 6 rows, `builder` role + builder account bootstrap) via checked-in idempotent seed; `migrate` compose service / `dotnet ef database update`. Register persistence through its own extension files — must NOT edit Program.cs beyond what TKT-foundation-001 established.
- Traces to: foundation (data model; C5)
- Acceptance (explicit): migrations apply cleanly to a fresh Postgres container and re-run is a no-op (forward-only); seeds are idempotent and re-runnable; constraints enforce the `02` §3 invariants — `source_ref`/`recorded_at` NOT NULL on every fact table, UNIQUEs (`saved_screens (user_id,name)`, `dcf_scenarios (user_id,instrument_id,name)`, `financial_statements` 5-column, `daily_prices` PK), CHECK `business_descriptions` published ⇒ both texts NOT NULL; `v_current_universe` returns the seeded universe.
- Architecture refs: `02-data-model.md` §1–§4, §8; `01-system-architecture.md` §3 (C5), §5 (ORM, database rows)
- UX refs: —
- Dependencies: TKT-foundation-001
- Parallel group: P-2

## Notes

Note: TC-MDF-009 (schema invariants) may be used as the red-first specification for this ticket's verification even though it formally gates TKT-mdf-002.
