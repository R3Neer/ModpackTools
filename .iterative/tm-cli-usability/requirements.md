# TM requirements — CLI usability and informational messages

Status: temporary working document. Delete when the iterative TM cycle is closed.

## R1 — Short aliases

- `-h` must behave as the short form of `--help`.
  - It must work for global help (`modpack -h`).
  - It must work for command help (`modpack search -h`, `modpack add -h`, etc.) wherever `--help` currently works.
- `-v` must behave as the short form of global `--version`.
- `-u` must behave as the short form of global `--update` (self-update), not of the project-content `update` command.
- Existing long forms and presentation/machine-output flags must remain compatible.
- The aliases must work through both the PowerShell command and the Nushell adapter/bridge.

## R2 — Project-independent search

- `modpack search <query>` must work when no active or explicit project exists.
- A project may still be supplied explicitly with `--project <id>` to request compatibility-filtered search results.
- Without a project, search is global on Modrinth except for the requested content type.
- Search result numbers must remain usable by later project commands, especially `modpack add <number>`.
- A search result number identifies the Modrinth project returned by the saved search, not the project context in which the search happened.
- Therefore a saved search must not reject a later target project merely because the search was made globally or with another project.
- Compatibility with the actual target project is validated when the later operation resolves/installs that Modrinth project.
- Search cache expiry and out-of-range protections remain in force.
- Human and machine search output must represent a missing search project cleanly.

## R3 — Informational-message policy

Create permanent project guidance for human-facing informational/status messages and audit the tool against it.

Required semantics:

1. **Completed action:** use past/completed wording (`Added`, `Changed`, `Removed`, `Updated`, `Created`, `Installed`, etc.). Never use a bare imperative/infinitive verb to describe something already applied.
2. **Preview / dry run / proposed action:** explicitly mark the action as hypothetical or planned (`Would add`, `Would change`, `Would remove`, or an equivalently unambiguous form). A preview must not look like either a completed action or an instruction to the user.
3. **In-progress action:** use progressive wording (`Searching…`, `Building…`, `Downloading…`).
4. **User instruction / next step:** imperative wording is allowed only when the text clearly addresses the user as guidance, e.g. `Run …`, `Use …`, `Select …`, preferably in an informational/next-step context rather than in a change list.
5. **State / diagnosis:** state facts directly (`Healthy`, `Missing`, `Pinned`, `Verification is incomplete`).
6. **Transaction summaries:** file-level lines and the final summary must agree on whether changes are planned, skipped, or applied.
7. **No misleading mixed tense:** output such as `Change pack.toml` followed by `✓ 4 file change(s) applied` is forbidden because the first line looks like a required user action while the second says it already happened.
8. **Machine-readable JSON:** preserve structured semantics. Do not turn machine fields into decorative prose merely to mirror human wording.

## R4 — Audit scope

Audit all first-party human-facing ModpackTools output, including at least:

- transaction/file-change summaries;
- content add/update/remove plans and results;
- build/diff/status/inventory/search/version/classification/resource/side/pin flows;
- doctor and self-update flows;
- installer messages owned by ModpackTools;
- help/next-step guidance where tense could be ambiguous.

The vendored R3CLI presentation library is out of scope unless ModpackTools passes it misleading message text.

## R5 — Tests and compatibility

- Add regression tests for all short aliases.
- Add tests proving search works with no project.
- Add tests proving a number from a global search can be consumed by an add operation in a project.
- Preserve an explicit-project search test and prove its number is also portable to another project.
- Add focused tests for completed vs preview transaction wording.
- Update affected help/docs and Nushell contract tests.
- Existing tests must continue to pass.
- CI must be green before the TM cycle is closed.

## R6 — TM hygiene

- Requirements, analysis and implementation-plan documents under `.iterative/` are temporary.
- Review each phase iteratively until a review produces no material change.
- During implementation review, distinguish defects from legitimate plan changes and update the plan only when the architecture/requirements actually changed.
- Delete all temporary TM documents before final merge/closure.
