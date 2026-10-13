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
  spelling correlation. Final own-artifact repetition passed 18 cases, and all seven independent
  fake-host transport probes passed.

- `dd6745a`: native project current: typed session snapshot, explicit-null versus absent
  context preserved, no configuration/discovery work. Locked restore, verified
  format, Release/AOT without warnings, 25 differential cases, two environment
  fallbacks and 11 invalid contexts passed. Independent review passed 15 baseline
  pairs and 20 context cases. Version/protocol/catalog regressions pass. Public
  current adapters are recorded separately below.

- `36372d7`: PS current adapter: explicit module snapshot with object parameter preserving
  null versus empty string; native machine path before renderer. Fifteen fresh-shell
  snapshot checks and failure priority pass; version/catalog regressions pass.
  Independent review passed 19 pairs (including legacy use then native current),
  four corrupt-theme cases and two human/help controls. Pester: 250 passed,
  zero failed, 55.92 seconds. Current does not change parent session.

- `3c51e15`: Nu current adapter: strict nullable session response and explicit environment
  snapshot, without parent mutation. Self-contained fresh-shell version/catalog/
  current checks and syntax verification pass. Independent review: 21 corpus
  pairs, four corrupt-theme cases, four final targeted regressions and one human
  current control. Help/leading presentation flags keep their legacy path.

- Native project use: pure effective-ID resolution before configuration/discovery,
  exact selected descriptor ID and session result without parent mutation. Locked
  restore, format verification, Release/AOT without warnings, 18 differentials
  and E04 pass. A v11 target [] versus legacy null for missing selector was found,
  corrected and retested on v12. Independent review passed 33 cases, an additional
  uppercase descriptor success, two E04 repeats and four protocol/environment
  controls; fixture hashes unchanged, empty stderr. Protocol/Nu regressions pass.

- PS project use: module selection changes only after strict validated native
  success. A test-only AOT transport peer exercises 11 malformed replies;
  fresh-shell transitions/current/catalog/version gates pass. Pester final:
  250 passed, zero failed, 45.58 seconds. Independent review: 38 initial pairs,
  ten targeted cases, three extras, then nine final regressions and eight mock
  replies. It found a thrown-target representation discrepancy and accepted
  casing-altered mock replies; both were corrected and retested. JSON and PS
  ErrorRecord targets now each preserve their legacy representation.

- Nu project use: updates parent environment only after validated success, retaining
  legacy case-sensitive update behavior. Omitted context preserves actual Nu
  environment exports, including custom conversions. Fresh-shell syntax/version/
  catalog/current/use gates, eight environment cases and twelve malformed replies
  pass. Independent review passed 25 comparisons, six environment conversions and
  eight mock replies, with unchanged fixtures. Command/operation correlation is
  exact; selector correlation is token-membership only, deliberately avoiding a
  second presentation parser in the adapter. It is not full request correlation.

- PS typed positional targets: positional values can be numbers/bools. Legacy
machine arguments already normalize to strings; only positional-error targets
retain the original value types. An initial review proposal to restore raw public
arguments was disproved by the initializer and direct probes before implementation.
Keep Core/public argument strings and project original arity-error targets in PS.
The fix passed eight fresh-process baseline/native pairs and the independent
eight-case review, including exact terminating-error element types, false-ID
selection and true's help coercion. Use/current regression checks pass.

No complete inventory, resolver, native transaction or installed
distribution exists yet. Catalog review does not prove their parity or performance.

- `2b619f5`: visible System-only projects are included; Hidden and System+Hidden
  invalid projects remain excluded without parsing. Locked restore, format,
  Release/NativeAOT without warnings, four differentials, protocol and Nu
  regressions pass. Independent reviewer reproduced v12's exclusion and passed
  v13's four cases with unchanged hashes and directory/file attributes.

`0dd015c`: managed Packwiz reader completed as an internal native capability.
Locked restore, direct Core/CLI/test-host format verification, warning-free Release
and Native AOT publication passed. Own native tests cover 13 differential cases,
E05, three budgets, four malformed/resource/escape failures and two blank-line
regressions. Shared catalog (21 differentials plus seven exception/limit cases),
four project-attribute cases, 15 protocol cases and native PSD1 contracts pass.
Independent black-box review passes the frozen 35-fixture corpus, six additional
multiline fixtures, exact/over budget boundaries and junction cases. White-box
review caught successful AST-versus-lexical differences, then quadratic whitespace
searches; both were corrected before the checkpoint. The final 262144-line valid
metadata probe completed in 112 ms rather than exceeding 10 seconds. This is a
diagnostic regression measurement, not an inventory benchmark or performance-gate
claim. Real cloud placeholders were not tested.

Next checkpoint: local JAR/ZIP discovery and metadata, then editorial/resource-order
composition into RawInventorySnapshot. Native inspection tests make each internal
capability observable without partial public content-list success. Public content
list still uses the legacy engine.
