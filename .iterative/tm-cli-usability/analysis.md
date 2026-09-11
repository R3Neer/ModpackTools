# TM analysis — CLI usability and informational messages

Status: temporary working document. Delete when the iterative TM cycle is closed.

## Current architecture

- `Public/modpack.ps1` is the single exported PowerShell entry point. It is an advanced function (`CmdletBinding`) and currently treats `--help` / `--version` as ordinary positional tokens before dispatching catalog commands.
- `Nushell/modpack.nu` is a wrapped Nushell command that forwards a string list to `Nushell/Invoke-ModpackBridge.ps1`; the bridge imports the PowerShell module and invokes `modpack` with the forwarded arguments plus `--json`.
- `Private/Commands.ps1` owns `Invoke-MpSearch` and currently resolves a project unconditionally.
- `Private/Modrinth.ps1` owns Modrinth search API construction, search-cache persistence, and numeric search-reference resolution.
- `Private/Rendering.ps1` owns human search rendering.
- `Private/MachineOutput.ps1` wraps renderers to snapshot the same domain objects into the JSON contract.
- `Private/Transaction.ps1` owns generic transactional file-change rendering. This is the direct source of the misleading `Add/Remove/Change <path>` lines after a completed operation.
- `Private/Operations.ps1`, `Private/Batch.ps1`, and `Private/Remove.ps1` reuse that transaction summary, so fixing the generic renderer fixes add/update/resource/side/classify/pin/remove file-change wording consistently.

## A1 — Short alias binding

PowerShell is the awkward part. Because `modpack` is an advanced function, a token such as `-v` is interpreted by PowerShell parameter binding before the function body. Merely replacing `-v` with `--version` inside `$Arguments` is therefore insufficient for direct PowerShell use.

Recommended implementation:

1. Add explicit switch parameters on the public `modpack` function with aliases `h`, `u`, and `v`.
   - `Help` / alias `h`
   - `SelfUpdate` / alias `u`
   - `Version` / alias `v`
2. Convert those bound switches back into canonical long-form tokens before the existing presentation parsing and dispatch path.
3. Also normalize literal positional `-h/-u/-v` tokens because the Nushell bridge forwards an argument array and may deliver them positionally rather than through direct named-parameter binding.
4. Keep the canonical engine entirely long-form after this normalization. This avoids teaching every command parser about two spellings.
5. `-h` is context-sensitive convenience: with a command token it becomes command help; with no command it becomes global help.
6. `-u` and `-v` remain global actions. If combined with a normal command, the canonical long-form path should reject the extra arguments rather than silently executing the command.

Risk: `-v` sits near PowerShell's common `-Verbose` parameter. An explicit parameter alias must be regression-tested in CI. The desired CLI contract takes precedence over using `-v` as an abbreviation for `-Verbose`; full `-Verbose` remains available.

## A2 — Search without requiring a project

Current behavior is project-bound in three places:

1. `Invoke-MpSearch` calls `Resolve-MpCommandProject` unconditionally.
2. `Invoke-ModrinthSearchRequest` always adds a Minecraft-version facet and, for mods, a loader facet from the project.
3. `Resolve-ModrinthSearchNumber` rejects a saved reference when its saved project/compatibility context differs from the consuming project.

Only the first two constraints must be relaxed for a project-free search. The third remains useful for searches that were deliberately compatibility-filtered by a project.

Recommended semantics:

- Preserve current convenience when an active project exists: `modpack search query` uses that active project as a compatibility filter.
- If no active project exists, `modpack search query` performs a global Modrinth search and does not fail merely because there is no project.
- `--project <id>` continues to force a specific compatibility-filtered search.
- Global search cache entries store null project/compatibility context. Because no project owned that search, their numeric results are portable to a later target project.
- Project-filtered cache entries keep the existing project/compatibility binding and existing mismatch/change protections.
- `Resolve-ModrinthSearchNumber` therefore branches on cache context: if `ProjectId` is empty, validate only cache existence/age/range; if `ProjectId` exists, preserve the current project and compatibility checks.
- Compatibility for a global-search result is enforced later when the actual target project operation resolves a compatible Modrinth version and dependency graph.

This is narrower than making every search number globally portable, preserves existing safety/tests, and matches the requested workflow exactly.

## A3 — Human search output / machine contract

Human rendering currently requires a project and prints a `Project` line. It should accept a null project and render an explicit global-compatibility scope instead of fabricating a project.

Suggested wording:

- Project-filtered: current project line remains.
- Global: `Compatibility  Any Minecraft version / loader` (or equivalent concise wording).
- Empty result text should say `No results were found.` for global search and `No compatible results were found.` for filtered search.

Machine output already has `ConvertTo-MpMachineProject`, which returns `$null` for a null project. Therefore the wrapper can make the project parameter nullable while preserving the existing `project` field as JSON null and the existing `search.results` structure.

## A4 — Informational message audit

### Definite defect

`Write-MpTransactionSummary` currently derives file actions as `Add`, `Remove`, or `Change` regardless of transaction state. That creates the observed contradiction:

- `Change pack.toml`
- `✓ 4 file change(s) applied.`

The first line looks like an instruction, while the second says the action already occurred.

Required state-aware wording:

- completed transaction: `Added`, `Removed`, `Changed`;
- preview/dry-run: `Would add`, `Would remove`, `Would change`;
- final summary must use matching state language.

### Domain plan wording

`Write-MpContentPlan` is mostly neutral because it uses state transitions (`not installed -> version`). Its removal case currently says `<name>: remove (<reason>)`; that is also easy to misread as an instruction. Replace it with a state transition such as `<name>: <version> -> removed (<reason>)` or another non-imperative factual form.

### Audit of other first-party message families

The reviewed output families already mostly fit the proposed policy:

- progress: `Searching…`, `Building…`, download/verification messages;
- completed/state: `Active project`, `was created`, `installed and verified`, `No differences`, `UPDATED/CURRENT`, `Added/Changed/Removed` diff sections;
- next-step guidance: explicit `Run …`, `Use …`, `Select …` messages;
- diagnostics: factual health/verification warnings.

No evidence was found that the vendored R3CLI text itself owns the misleading tense; ModpackTools supplies the message strings.

The permanent policy should live in `docs/message-style.md` and be referenced by tests where feasible.

## A5 — Implementation shape

Prefer direct edits to the owning files over a late-loaded override file. Duplicate function definitions would make the runtime work but would hide the true source of behavior and make future maintenance unnecessarily theatrical.

Likely touched files:

- `Public/modpack.ps1`
- `Private/Commands.ps1`
- `Private/Modrinth.ps1`
- `Private/Rendering.ps1`
- `Private/MachineOutput.ps1`
- `Private/Transaction.ps1`
- `Private/Operations.ps1`
- `README.md` and/or help text describing search-number scope
- new `docs/message-style.md`
- new focused Pester test file, avoiding unnecessary surgery on the large existing test suite
- Nushell contract tests only if bridge behavior is not sufficiently covered by PowerShell array-invocation tests

## A6 — Test strategy

Create a focused `Tests/CliUsability.Tests.ps1` and test:

1. `modpack -h` and command-level `-h`.
2. `modpack -v --offline` and equivalence with `--version --offline`.
3. `-u` dispatch, using mocks so no network/update is performed.
4. literal array invocation of `-h/-v/-u` to simulate the Nushell bridge path.
5. global search request creation without a project.
6. global search cache creation with null project context.
7. a numeric reference from that global cache consumed by a later project without project-mismatch failure.
8. the existing project-filtered mismatch behavior remains intact.
9. transaction summary wording for applied, preview, and dry-run states.
10. neutral removal-plan wording.
11. machine search output with `project = null`.

## Analysis review 1

Material change found: omitted `--project` should preserve active-project filtering when available and fall back to global search only when no project exists.

## Analysis review 2

A broader portability design initially made every saved search number portable across projects. Review found that this removed an existing safety check unnecessarily. Portability is now limited to project-free/global searches; project-filtered caches remain bound.

## Analysis review 3

Re-checked the entry point, bridge, search cache semantics, transaction renderer, content-plan renderer, machine wrapper, batch consumers, self-update output, and test compatibility under the narrowed design. No further material changes. Analysis is stable for planning.
