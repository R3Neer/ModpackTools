# Implementation sequence and first checkpoints

Status: implementation in progress; remaining phases are acceptance obligations.
Each numbered capability below is normally one commit once its stated behavior,
native build, and focused checks pass. Split only independently usable outcomes;
merge inseparable support files as described in [commit-policy.md](commit-policy.md).
Do not wait for an entire phase before committing. IDs refer to the comparison matrix.

## Phase 0: testable native boundary

1. Pin SDK/build settings and publish a native host with a real version result.
   Observable: machine version result from published win-x64 EXE (C01/C28).
2. Implement versioned request validation and exactly-one-final-result failures.
   Observable: supported request accepted; invalid/unknown schemas fail predictably
   with proper native exit status (C02/C08).
3. Add explicit typed JSON/result mapping and source-generated serialization.
   Observable: legacy version public envelope reproduced; no reflection fallback
   or human output contaminates data (C01/C08).
4. Add Nu version dispatch to the native executable, preserving normal/no-human
   behavior. Observable: version works in a fresh Nu process without spawning PS
   for that command (C01/C08/C28).
5. Add the equivalent PS version dispatch. Observable: schema/public error behavior
   preserved and performance measured from an already-running PS process (C01/C08/C28).

Protocol, types, and version can be a single first capability if splitting makes
the initial host artificial; do not commit empty projects as a completed core.
Legacy commands remain available through explicit dispatch ownership during coexistence.

## Phase 1: read-only vertical slice

6. Read PSD1 project/configuration data safely and resolve explicit/session context
   (C03/C07/C11). Keep originals and existing config paths.
7. Parse Packwiz manifests/managed content through Tomlyn syntax extraction
   (C04/C11), preserving identities, fields, unknown sections and exact untouched text.
8. Add local mods/resources/shaders and editorial metadata (C04/C07/C11).
9. Add filters, stable numbered references/cache validation, and resource order
   (C05/C06/C18). Check public ordering/collation rather than assuming it.
10. Add health snapshot consumption/invalidation with truthful unknowns and bounded
    work (C16/C25/C28). Never bypass strong verification for a stale cache.
11. Switch Nu inventory with typed results and native R3CLI, then PS inventory as
    separate working adapter checkpoints (C04-C10/C28).

At this boundary run both public adapters against full read-only fixtures, human
captures, native publication, and performance gates. Profile failed budgets before
adding remote/mutating scope. Machine mode must not load theme or R3CLI.

## Phase 2: declarations and read-only planning

12. Port Fabric declarations with limits/ranges; then Quilt alternatives/guards;
    then Forge/NeoForge syntax and Maven behavior as separate coherent outcomes
    (C12/C13). Retain native unknown states for unsupported constructs.
13. Add provider client/host normalization and per-invocation deduplication
    (C13/C16/C25). Test frozen provider answers before live latency.
14. Port resolver cost/roots/pins and a simple compatible solution (C14/C15).
15. Add backtracking/alternatives/cycles/ties/search limits in independently
    verifiable checkpoints (C14/C15). No pack writes.
16. Produce a preview plan bound to identity/fingerprint and compare complete
    plans against the legacy engine (C14-C20).

Keep pure selection, provider/network access, artifact parsing, and materialization
separate. A resolver checkpoint is not complete just because it produces some plan.

## Phase 3: controlled mutation and recovery

17. Add project lock/stage/preconditions with failures before any source write
    (C20/C23/C27).
18. Add durable journal/backups, atomic file application, and successful commit
    with injected mid-commit rollback (C21/C27).
19. Add process-interruption recovery and external-edit conflict preservation
    (C22/C27). Native process kill testing is mandatory before switching mutations.
20. Wire the first limited mutating capability, such as pin/unpin or category
    assignment, with exact whole-batch parity (C15/C18/C19).
21. Wire add/update, removal/cascade, and autoremove as separate capability groups
    after their relevant full matrix cases pass (C12-C23/C25/C27).
22. Port build/diff/doctor with artifact preservation and semantic export checks
    (C16/C24/C25).

No automatic dual mutation or automatic uncertain fallback after a partially
applied native operation. Each invocation has one writer/transaction owner.

## Phase 4: installed distribution

23. Package native core, adapter pair, resources/licenses/hashes and stable install
    manifest, independent of source/toolchains (C26).
24. Add versioned installation, helper-based update, native/fresh-shell smoke
    checks, open-file handling and rollback (C26/C27).
25. Validate actual configuration compatibility, complete cross-shell operation,
    legacy journals and full C01-C28 before proposing old-install removal.

Retirement, public release/push, moving directories, and selective integration into
main need destination-specific authorization. Main receives product-only commits,
not this migration record or its history. A release version is chosen only when
public behavior/distribution changes are finalized.

## Current completed preparation

- Isolated branch/worktree, baseline revision, commit policy and branch instructions.
- Compatibility contract, comparison matrix, language/AOT/API choices.
- Legacy/session/startup/hash baseline and warning-free syntax/Maven AOT probe.
- Performance gates, documentation boundary, installed-location/update design.

## Implemented checkpoints

- `2c632d3`: typed native version envelope; warning-free AOT, Release build,
  format verification, exact JSON/types, manifest-version and transport checks.
- `e837767`: bounded request protocol; 15 native cases including malformed JSON,
  duplicate fields, limits and lone-surrogate errors found by white-box review.
- `5ed3577`: version aliases/options/casing/newline semantics; 18 differential
  cases. Independent reviewer passed 16 cases with only authorized E01 normalized.
  New failures consistently expose array arguments; domain IDs/categories retained.

- `479c137`: Nu machine version with explicit core selection; fresh-shell tests
  with PS removed from PATH. Independent reviewer passed 9 differential pairs.
- `2c00559`: PS machine version before console initialization; fresh-shell tests
  force console initialization to fail. Independent reviewer passed 16 version
  cases and 3 cases with a corrupt theme in an isolated module copy.

Human version and non-version commands still use legacy. Core selection currently
uses explicit `MODPACKTOOLS_CORE_PATH`; this is a development/coexistence seam,
not the completed installed manifest design. Nu ownership currently requires
version/-V as first token; leading presentation options remain legacy.

- `1a438f0`: data-only PSD1 reader with immutable typed literals, resource limits
  and no PowerShell execution. Independent black-box corpus: 35 fixtures, including
  19 exact successful comparisons, real file copies and explicit rejections.
- `d3c2280`: PS native transport diagnostics use shared expected-error helpers;
  the pinned Pester 4.10.1 suite passed all 250 tests after correction.
- `36f3b3b`: native `project list` with bounded PSD1/TOML readers, typed project
  catalog and preserved nullable/numeric/duplicate target semantics. 21 native
  differential cases and 7 explicit error/empty/resource cases passed. Independent
  black-box corpus: 61 cases on v6, then 12 targeted final regressions on v9;
  full v9 corpus was not rerun. E01-E03 are recorded diagnostic exceptions.
  White-box review closed numeric conversion and parser-recovery findings.
  Release build/AOT publication/format checks were warning-free; protocol/version
  regressions passed. At this checkpoint, public adapters still used legacy catalogs.

Tomlyn recursion was tested with 5000 nested arrays: before preflight it terminated
the native process with StackOverflowException. Final resource tests produce a
structured failure, including malformed strings/delimiters that could otherwise
hide nesting. Separate valid bracket-string/comment controls pass. Two final native
probes with 5000 dotted-key segments (root key and table header) completed with one
successful envelope and empty stderr, so no hypothetical dotted-key limit was added.

- `373a65b`: PS machine project catalogs dispatch before presentation and
  validate typed nullable project fields using shared native transport. Independent
  review: 11 machine pairs, 4 corrupt-theme cases and 2 exact human/help pairs.
  Fresh-shell checks prove no renderer/legacy reader execution. Pester 4.10.1:
  all 250 passed in 38.16 seconds after rerunning outside the sandbox. The isolated
  attempt stalled before the first It; an isolated microtrace and an outside CLI
  run (13 passed in 1.77 seconds) identified an environment-dependent Pester setup
  problem, not a product regression. Exact registry cause was not asserted.

- `852bc4c`: Nu machine project catalogs bypass PowerShell with strict typed
  envelopes, nullable fields and case-sensitive legacy unwrapping. Fresh-shell
  Nu syntax/catalog/version gates pass with PATH empty. Independent review passed
  14 corpus cases, 4 extras and 5 corrupt-theme cases on the first revision;
  a subsequent hardening enforces empty stderr and exact command/operation
  spelling correlation. Seven independent fake-host transport probes pass;
  final own-artifact repetition passed 18 cases, and all seven independent
  fake-host transport probes passed.

- Native project current: typed session snapshot, explicit-null versus absent
  context preserved, no configuration/discovery work. Locked restore, verified
  format, Release/AOT without warnings, 25 differential cases, two environment
  fallbacks and 11 invalid contexts passed. Independent review passed 15 baseline
  pairs and 20 context cases. Version/protocol/catalog regressions pass. Public
  current adapters follow, with PS module state sent explicitly.

- PS current adapter: explicit module snapshot with object parameter preserving
  null versus empty string; native machine path before renderer. Fifteen fresh-shell
  snapshot checks and failure priority pass; version/catalog regressions pass.
  Independent review passed 19 pairs (including legacy use then native current),
  four corrupt-theme cases and two human/help controls. Pester: 250 passed,
  zero failed, 55.92 seconds. Current does not change parent session.

- Nu current adapter: strict nullable session response and explicit environment
  snapshot, without parent mutation. Self-contained fresh-shell version/catalog/
  current checks and syntax verification pass. Independent review: 21 corpus
  pairs, four corrupt-theme cases, four final targeted regressions and one human
  current control. Help/leading presentation flags keep their legacy path.

Next reviewed selection boundary: project use, with E04 and PS/Nu parent-state
ownership documented before implementation.

No inventory, session mutation, resolver, native transaction or installed
distribution exists yet. Catalog review does not prove their parity or performance.
