# TM implementation plan — CLI usability and informational messages

Status: temporary working document. Delete when the iterative TM cycle is closed.

## P1 — Canonical short aliases

- [ ] Extend the public `modpack` advanced function with explicit `h`, `u`, and `v` aliases so direct PowerShell parameter binding accepts them.
- [ ] Normalize bound short switches and literal forwarded `-h/-u/-v` tokens into the existing long-form command contract before presentation parsing/dispatch.
- [ ] Keep long options, common parameters, JSON/presentation flags, and existing error behavior intact.
- [ ] Verify global and command-scoped help semantics.

## P2 — Optional search context

- [ ] Add a search-only project resolver: explicit `--project` first, otherwise active project, otherwise `$null`.
- [ ] Make Modrinth search request construction accept a null project.
- [ ] For global search, omit Minecraft-version and loader facets while retaining the requested content-type facet.
- [ ] Persist global search caches with null project/Minecraft/loader context.
- [ ] Keep project-filtered search cache context unchanged.
- [ ] Change numeric search resolution so null-context/global caches are portable, while project-bound caches retain current project and compatibility mismatch checks.

## P3 — Search presentation and machine output

- [ ] Allow the human search renderer to receive a null project.
- [ ] Render an explicit global compatibility scope when no project filters the search.
- [ ] Use `No results were found.` for a global empty search and retain compatibility wording for project-filtered empty searches.
- [ ] Allow the machine-output wrapper to receive a null project and emit `project: null` without changing `search.results`.

## P4 — Informational-message policy and audit

- [ ] Add permanent `docs/message-style.md` defining completed, planned, in-progress, instructional, and diagnostic wording.
- [ ] Make generic transaction file lines state-aware:
  - [ ] applied: `Added` / `Changed` / `Removed`;
  - [ ] preview/dry-run: `Would add` / `Would change` / `Would remove`.
- [ ] Make the transaction footer use the same state semantics as its file lines.
- [ ] Replace the content-plan removal imperative (`remove`) with a neutral state transition.
- [ ] Re-audit first-party status/informational strings across commands, doctor, self-update, installer, and help; fix any additional violations found.
- [ ] Leave structured machine action values (`add/change/remove`) unchanged.

## P5 — User-facing documentation/help

- [ ] Update search help to explain global search, active/explicit filtering, and the scope of numbered results.
- [ ] Document `-h`, `-u`, and `-v` beside their long global forms.
- [ ] Update README/reference text that currently describes every numbered search result as project-bound.
- [ ] Keep examples concise and backward-compatible.

## P6 — Regression tests

- [ ] Add focused Pester coverage for direct PowerShell `-h`, `-u`, and `-v`.
- [ ] Cover literal forwarded short tokens to exercise the Nushell bridge invocation shape.
- [ ] Test a search with no active/explicit project.
- [ ] Test global cache null context and later numeric consumption by a real project.
- [ ] Preserve/test project-filtered mismatch rejection.
- [ ] Test null-project human/machine search presentation.
- [ ] Test applied vs preview/dry-run transaction wording.
- [ ] Test neutral removal-plan wording.
- [ ] Adjust Nushell contract tests only if required by implementation behavior.

## P7 — Integration verification

- [ ] Review the implementation diff against requirements and this plan.
- [ ] If a review reveals an architectural requirement change, update requirements/analysis/plan and re-review; otherwise fix code/tests only.
- [ ] Open a draft PR from `tm-cli-usability` to `main` and run repository CI.
- [ ] Diagnose and fix every failure caused by this work; repeat until CI is green.
- [ ] Perform a final static audit of first-party informational strings for policy violations.

## P8 — TM closure

- [ ] Delete `.iterative/tm-cli-usability/requirements.md`, `analysis.md`, and `plan.md`.
- [ ] Verify no temporary TM artifacts remain in the final tree.
- [ ] Re-run/confirm CI on the cleanup/final state.
- [ ] Complete the repository change only after the final review is stable and CI is green.

## Plan review 1

No material architectural changes. The plan matches the stabilized requirements/analysis and deliberately keeps project-filtered search references bound while making only global-search references portable.

## Plan review 2

No material changes. Plan is stable for implementation.
