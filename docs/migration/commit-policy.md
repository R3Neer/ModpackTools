# Strict commit policy

## Unit of progress

A commit records one small, externally checkable capability or one indispensable
infrastructure capability. Examples: produce a version result; reject an unsupported
request schema; read a project descriptor; list managed mods; preserve Nu session
selection; recover one interrupted transaction. It is not one commit per function,
file, or arbitrary number of lines, and not one commit for a whole subsystem.

Keep all inseparable supporting work together: types, implementation, focused
behavioral tests, and shipped documentation that makes this capability understandable.
If two outcomes have independent acceptance cases and can compile independently,
split them. If splitting requires placeholders or creates misleading partial
behavior, keep them together and explain why in the commit body.

## Mandatory checkpoint

As soon as a new capability is observable and its applicable checks pass, inspect
the diff, stage its explicit paths, and commit BEFORE beginning the next capability.
This also applies to a small fix, diagnostic improvement, or measurable optimization.
Do not defer a green checkpoint until the end of a work session or migration phase.

A capability may start as an internal callable unit with a focused acceptance test;
it need not already be wired to every public command. However, a no-op scaffold or
an untested stub must not be described as completed behavior.

## Checks required for each commit

- Product code: normal build and focused behavioral/differential tests for the
  added capability, plus formatting/lint appropriate to the touched language.
- Rust: `cargo fmt --check`, `cargo check --locked`, focused `cargo test --locked`,
  and `cargo clippy --locked --all-targets -- -D warnings` once the workspace exists.
  Use the pinned target/configuration; compile in Release before a performance claim.
- Adapter changes: syntax validation, contract cases, and a fresh shell process.
- Transaction/recovery changes: relevant injected failures and unchanged-original
  checks. These must pass before the checkpoint, not be deferred to a final suite.
- Documentation/evaluation-only: inspect factual claims, local Markdown links,
  data consistency, and `git diff --check`. Compilation is not applicable and must
  not be claimed. Evaluation programs must compile/run before their results are committed.
- Every commit: `git diff --check`, staged-diff review, no generated artifacts or
  unrelated files, and a truthful commit message identifying the visible outcome.

Run the broader compatibility suite at each completed public command, dependency
upgrade, and phase boundary. Repeat it earlier if a shared contract was changed.
Do not rerun expensive unrelated suites merely to manufacture activity.

## Size and timing

Aim for one or two comparison cases per capability, often tens to a few hundred
changed lines. This is guidance, not a gate: parser extraction and a reliable
transaction primitive can require more. Reassess any scope that covers more than
three independent observable outcomes. There is no time-based auto-commit and no
line-count target that justifies committing broken work.

Unfinished work may remain uncommitted in this isolated checkout. Do not add WIP
commits to the green implementation sequence. Record a blocker in migration docs
if necessary; do not hide a failure under a successful build-only checkpoint.

## History and messages

Use `feat(core): ...`, `fix(core): ...`, `perf(core): ...`, `test(compat): ...`,
`feat(nu): ...`, `feat(ps): ...`, `build: ...`, and `docs(migration): ...`.
Describe the behavior, not "progress", "misc", or the language being used.
For nontrivial commits record the comparison case IDs, validation, and intentional
differences. A measured optimization records before/after conditions and results.

Do not amend, squash, or rebase away checkpoints after proceeding to the next
capability. Correct a mistake in a new focused commit. Do not push or publish
without separate user authorization.

Keep migration docs/benchmarks in `docs(migration)` or `test(evaluation)` commits,
separate from product code. Updating the progress record does not require adding
a migration file to a product commit. This enables selective product integration
without migration documentation entering `main`, including through commit history.

## Illustrative granularity

Good: version payload + its test; then request validation + its test; then Nu
version round-trip + fresh-process test. Bad: commit each serializer/helper
separately; or wait until version, inventory, search, and both adapters are complete.
