# TM implementation plan — CLI usability and informational messages

Status: temporary working document. Delete when the iterative TM cycle is closed.

## P1 — Canonical short aliases

- [x] Extend the public `modpack` advanced function with explicit `h`, `u`, and `v` aliases so direct PowerShell parameter binding accepts them.
- [x] Normalize bound short switches and literal forwarded `-h/-u/-v` tokens into the existing long-form command contract before presentation parsing/dispatch.
- [x] Keep long options, common parameters, JSON/presentation flags, and existing error behavior intact.
- [x] Verify global and command-scoped help semantics in focused tests and the Nushell contract.

## P2 — Optional search context

- [x] Add a search-only project resolver: explicit `--project` first, otherwise active project, otherwise `$null`.
- [x] Make Modrinth search request construction accept a null project.
- [x] For global search, omit Minecraft-version and loader facets while retaining the requested content-type facet.
- [x] Persist global search caches with null project/Minecraft/loader context.
- [x] Keep project-filtered search cache context unchanged.
- [x] Change numeric search resolution so null-context/global caches are portable, while project-bound caches retain current project and compatibility mismatch checks.

## P3 — Search presentation and machine output

- [x] Allow the human search renderer to receive a null project.
- [x] Render an explicit global compatibility scope when no project filters the search.
- [x] Use `No results were found.` for a global empty search and retain compatibility wording for project-filtered empty searches.
- [x] Allow the machine-output wrapper to receive a null project and emit `project: null` without changing `search.results`.

## P4 — Informational-message policy and audit

- [x] Add permanent `docs/message-style.md` defining completed, planned, in-progress, instructional, and diagnostic wording.
- [x] Make generic transaction file lines state-aware:
  - [x] applied: `Added` / `Changed` / `Removed`;
  - [x] preview/dry-run: `Would add` / `Would change` / `Would remove`.
- [x] Make the transaction footer use the same state semantics as its file lines.
- [x] Replace the content-plan removal imperative (`remove`) with a neutral state transition.
- [x] Re-audit first-party status/informational strings across commands, doctor, self-update, installer, and help; no additional equivalent tense violations required code changes.
- [x] Leave structured machine action values (`add/change/remove`) unchanged.

## P5 — User-facing documentation/help

- [x] Update search help to explain global search, active/explicit filtering, and the scope of numbered results.
- [x] Document `-h`, `-u`, and `-v` beside their long global forms.
- [x] Update README/reference text that currently describes every numbered search result as project-bound.
- [x] Keep examples concise and backward-compatible.

## P6 — Regression tests

- [x] Add focused Pester coverage for direct PowerShell `-h`, `-u`, and `-v`.
- [x] Cover literal forwarded short tokens to exercise the Nushell bridge invocation shape.
- [x] Test a search with no active/explicit project.
- [x] Test global cache null context and later numeric consumption by a real project.
- [x] Preserve/test project-filtered mismatch rejection.
- [x] Guard null-project human/machine search presentation contracts.
- [x] Guard applied vs preview/dry-run transaction wording contracts.
- [x] Guard neutral removal-plan wording.
- [x] Extend the Nushell contract with `-h`, `-u`, and `-v` coverage.

## P7 — Integration verification

- [x] Review the implementation diff against requirements and this plan.
- [x] If a review reveals an architectural requirement change, update requirements/analysis/plan and re-review; otherwise fix code/tests only. No requirement change was needed during implementation.
- [x] Open draft PR #4 from `tm-cli-usability` to `main` and run repository CI.
- [x] Diagnose and fix failures caused by this work. One real compatibility regression in minimal removal-plan rendering was fixed; subsequent failures were isolated to the new test harness and were resolved without changing runtime semantics.
- [x] CI converged: Pester, repository installation, isolated presentation/failed-upgrade verification, self-update rollback, and the complete Nushell adapter job all passed.
- [x] Perform a final static audit of first-party informational strings for policy violations. No additional equivalent violations were found.

## P8 — TM closure

- [ ] Delete `.iterative/tm-cli-usability/requirements.md`, `analysis.md`, and `plan.md`.
- [ ] Verify no temporary TM artifacts remain in the final tree.
- [ ] Re-run/confirm CI on the cleanup/final state.
- [ ] Complete the repository change only after the final review is stable and CI is green.

## Plan review 1

No material architectural changes. The plan matches the stabilized requirements/analysis and deliberately keeps project-filtered search references bound while making only global-search references portable.

## Plan review 2

No material changes. Plan is stable for implementation.

## Implementation review 1

The branch diff matches the stabilized plan. The review found documentation drift rather than an architectural defect: README and Nushell documentation still described the old number-scope / short-option behavior. Both were updated. No change to requirements, analysis, or architecture was needed.

## Implementation reviews 2–6

CI exposed one real backwards-compatibility defect in the revised removal-plan message: a legacy/minimal renderer fixture may omit `VersionId`. The implementation was corrected to fall back to `installed -> removed`. The remaining failures in these iterations came exclusively from attempting to capture human output through artificial Pester/module boundaries; runtime output and all pre-existing tests were correct. The focused tests were rewritten as contracts over the actual loaded renderer ScriptBlocks instead of simulating R3CLI internals.

## Implementation review 7

No runtime, requirement, architecture, documentation, or message-policy changes were required. All functional and integration gates passed. Implementation is stable; proceed to TM cleanup and one final CI on the cleaned tree.
