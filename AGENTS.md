# Hybrid-core branch instructions

These instructions govern the migration branch, not the released product.

- Work only on `codex/hybrid-core` in its isolated checkout. Do not edit, commit,
  merge, install, release, or push to `main` as part of this migration.
- The current user-authorized phase is evaluation, measurements, and migration
  documentation. Do not implement product functionality until the user explicitly
  authorizes implementation. Diagnostic benchmark fixtures are not product code.
- Read `docs/migration/commit-policy.md` and `docs/migration/decisions.md` before
  implementation. Follow the compatibility contract and comparison matrix.
- Commit a coherent, newly working capability immediately after its applicable
  build, behavioral checks, and diff checks pass, before starting another capability.
  Do not wait for an entire command family or phase to be finished.
- Never commit per helper function. Keep inseparable types, parser, behavior,
  tests, and shipped documentation in the same logical capability commit.
- Keep migration-only records in separate documentation/evaluation commits so
  they can be excluded from a future product integration. Never squash away these
  checkpoints or rewrite published history without explicit authorization.
- Preserve unrelated changes. Stage explicit task-owned paths; never `git add .`.
- Benchmarks and comparison tests use isolated configuration and fixture copies.
  Never run competing writers on a user's pack; never weaken transaction hashing
  or recovery guarantees for a performance claim.
- No performance claim without raw samples, versions, workload, cache conditions,
  and clear distinction between startup-only and end-to-end measurements.
- No push, public PR, release, installation, or old-engine removal is authorized
  by local branch preparation or local commit authorization.
- Migration documents, this file, and benchmark-only fixtures must not enter
  `main`. Future integration must selectively replay product commits; merging the
  whole branch would also introduce migration documents through its history.

