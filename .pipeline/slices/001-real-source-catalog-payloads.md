---
id: SLICE-001
status: done
scope:
  - .gitattributes
  - fixtures/Degerli.Fixtures/Degerli.Fixtures.csproj
  - fixtures/Degerli.Fixtures/RealSourceCatalog.cs
  - fixtures/Degerli.Fixtures/Payloads/
  - tools/source-probes/contracts/
  - tests/Core.UnitTests/SourceContractProbeTests.cs
  - tests/Api.IntegrationTests/RealSourcePricesTests.cs
---

# SLICE-001: Extract `RealSourceCatalog` Payload Bodies to Embedded Files

## Context & Goal

`fixtures/Degerli.Fixtures/RealSourceCatalog.cs` grew to **55,877 lines** (~55.5k C# LOC) because all 43 real-source JSON and KAP Excel-HTML captures were embedded as raw C# string literals inside a single `.cs` file. That inflated repository C# LOC by 39%, slowed Roslyn builds and IDE analysis, re-allocated multi-megabyte UTF-8 byte arrays on every `RealSourceCatalog.Get()` lookup, and caused rebase collisions whenever parallel tickets added captures.

Extract all 43 payload bodies into standalone files under `fixtures/Degerli.Fixtures/Payloads/<source>/<name>.{json,html}` compiled as MSBuild `<EmbeddedResource>` items, shrinking `RealSourceCatalog.cs` to a pure metadata & loader catalog (~350 lines) with zero behavioral change.

## Rules & Invariants

1. **Public C# API unchanged:** `RealSourceCatalog`, `RealSourcePayload`, and `RealSourceProvenance` keep every public constant, property, and method signature (`Names`, `Build`, `Get`, `TryGet`, `Decode`, source constants, and capture metadata).
2. **Byte-for-byte payload identity:** Every entry in `RealSourceCatalog.Names` (43/43) must produce the exact same SHA-256 `RawBytes` and metadata (`Source`, `Method`, `DefaultPath`, `StatusCode`, `EncodingName`, `Provenance`, `ProvenanceLabel`) before and after extraction.
3. **Working-directory & container independence:** Payloads are embedded into `Degerli.Fixtures.dll` via `<EmbeddedResource Include="Payloads\**\*" LogicalName="real-sources/%(Filename)" />` and cached once in memory, so `dotnet test` and `Dockerfile.ci` (`dotnet /app/dist/fixtures/Degerli.Fixtures.dll`) need no loose-file path assumptions.
4. **Line-ending preservation:** `.gitattributes` marks `fixtures/Degerli.Fixtures/Payloads/**` as `-text` so Git checkouts never mutate captured bytes across platforms.
5. **Convention for future captures:** New real-source doubles are added as one file under `fixtures/Degerli.Fixtures/Payloads/<source>/<name>.{json,html}` plus one metadata entry in `RealSourceCatalog.cs` — never as inline payload literals in C#.
6. **Probe contract provenance:** Update `double.file` in the five `RealSourceCatalog`-backed `tools/source-probes/contracts/*.json` files to point to their extracted payload files, and assert `double.file` existence in `SourceContractProbeTests`.

## Verification

- [x] Pre-refactor vs. post-refactor manifest comparison across all 43 `RealSourceCatalog.Names` entries shows 0 SHA-256 or metadata diffs.
- [x] Catalog invariant check asserts 1:1 parity between `RealSourceCatalog.Names` and embedded `real-sources/*` manifest resources (no missing or orphaned payload files).
- [x] Narrow test runs pass: `SourceContractProbeTests` (21/21), `RealSourcePricesTests` + `KapPublicIdentityTests` + `KapPublicReportDecoderTests` (74/74), plus `dotnet publish fixtures/Degerli.Fixtures/Degerli.Fixtures.csproj`.

## Result

- `fixtures/Degerli.Fixtures/RealSourceCatalog.cs` reduced from **55,877 total lines (55,506 C# LOC)** to **700 total lines (497 C# LOC)** — deleting **55,009 lines of C#** (`9 files changed, 135 insertions(+), 55,292 deletions(-)`).
- All 43 payloads extracted to `fixtures/Degerli.Fixtures/Payloads/{evds,isyatirim,kap,kap-public}/` as standalone `.json`/`.html` files embedded under `real-sources/<name>` and cached once via `Lazy` + `FrozenDictionary`.
- Updated `double.file` provenance paths in the 5 `RealSourceCatalog`-backed `tools/source-probes/contracts/*.json` files and added file-existence verification in `SourceContractProbeTests`.
