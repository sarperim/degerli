# Agent Instructions (Değerli)

These instructions apply to all agent sessions working in `/home/sarp/degerli`.

## 1. Vertical Slice Workflow (`.pipeline/slices/`)

All features, fixes, and refactors are developed **one vertical slice at a time** and tracked in `.pipeline/slices/NNN-<slug>.md`:

1. **Before coding:** Create or update `.pipeline/slices/NNN-<slug>.md` with YAML frontmatter (`id: SLICE-NNN`, `status: in_progress`, `scope: [...]`) and sections for `Context & Goal`, `Rules & Invariants`, `Blockers & Dependencies`, and `Verification`.
2. **One change at a time:** Implement, verify, and report one slice (or one requested suggestion) at a time unless explicitly asked to batch.
3. **After verification:** Mark `status: done` and record the `Result` in the slice file.
4. **PR & Review Gate:** When a slice's work is finished and locally verified, commit on a slice branch and open a PR targeting `dev`. **Never merge a PR without running the review agents (`review-lead` / `diff-reviewer` / `security-reviewer`) first.**

## 2. Subagent Delegation (`Model: "flash"`)

- **Always use the Gemini Flash subagent whenever coding is needed.**
- When implementing code, config, or test changes, invoke a subagent via `invoke_subagent` with `Model: "flash"` (e.g. `TypeName: "self"`, `Model: "flash"`).
- The main agent acts as planner, architect, and reviewer: it investigates context, defines the slice spec, dispatches the `flash` subagent with a clear, self-contained prompt, and verifies the subagent's work.
