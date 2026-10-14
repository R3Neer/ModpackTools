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

Local resource/shader ZIP discovery completed: seven own differentials and two
exact/over budget cases pass, along with managed-reader/protocol/PSD1 regressions.
Independent review passes 24 frozen fixtures and six boundaries, including Unicode
suppression, Hidden selected root directories, invalid ZIP contents, .zip's empty
name and invalid unrelated mod metadata. White-box checks confirm the kind boundary,
no archive opening, counting before suppression and exclusive-handle reopening.
Locked restore, format for all three projects, Release and Native AOT publish pass
without warnings. Inspection remains test-only, not a partial public command.

Single-JAR display metadata completed with bounded structural ZIP preflight and
typed optional-read statuses. Locked restore, all-project format, Release and Native
AOT passed without warnings. Eleven own differentials plus exact/over quotas,
missing/corrupt metadata, Unicode, exclusive reopening and one-million-digit
integer regression pass; managed/local-ZIP/PSD1/protocol regressions pass.
Independent black-box review passes 80 frozen cases, seven quotas and four integer
controls. White-box review passes ten structural/ZIP64 probes and targeted Unicode
checks. It found and closed an uncaught InvalidDataException, invalid-surrogate
key identity differences and BigInteger CPU amplification before the checkpoint.
The million-digit diagnostic changed from exceeding ten seconds to 57-270 ms in
review probes; these are regression observations, not an inventory benchmark.
The typed display projection isolates legacy coercions from business records;
request decoding remains strict. Native CLI still does not expose inventory.

Local JAR discovery/managed-path exclusion completed. Immutable local observations
reuse bounded JAR display reads and a shared DFS file traversal. Known candidates
are excluded before archive opening; matched files/directories and actual delivered
metadata bytes (including failed JSON reads) remain charged. Own seven differentials,
three aggregate-limit cases, invalid empty-project policy and per-archive uncertainty
pass. Independent review passes 28 frozen fixtures, eleven quotas and two invalid
empty/fully-managed policy cases. White-box probes verify locked known-file exclusion,
exact/over failed-JSON byte budgets, isolation from resources and handle disposal.
Archive policy validation is shared and runs before empty/excluded early returns.
Locked restore, all-project format, Release/Native AOT and relevant reader/protocol
regressions pass without warnings. This still does not switch public content list.

`d79bdde`: editorial metadata reader completed. Missing files/sections normalize
only in memory; immutable source data preserves unknown values and key spelling.
Validation priority and type-sensitive scalar/array intent-schema behavior match
the supported literal baseline. Locked restore, all-project format, warning-free
Release/Native AOT, 19 own differential cases, malformed-data diagnostics, PSD1
contracts and 15 protocol cases pass. Independent black-box review passes 52
cases with exact numerical comparisons and unchanged fixture hashes/attributes.
White-box review closes seven numerical controls and architecture/resource checks.
The buffered test inspector preserves Int64/Decimal/Double transport, including
overflowing integer conversion through BigInteger; Core retains literal nodes.
No public inventory route changes in this checkpoint.

`c03667c`: shared numeric extraction corrected without changing literal storage.
G15 display, bounded integer-to-Double conversion and typed Decimal/Double JSON
targets preserve legacy values/types. Nonfinite integer literals remain import
failures, while explicit floating exponents retain their existing infinity result.
Locked restore, all-project format, warning-free Release/Native AOT, 31 own project
pairs, ten error/limit controls, 19 editorial pairs, bounded numeric contracts and
15 protocol cases pass. Independent black-box 28 cases and white-box six public
pairs plus parser/architecture checks close the correction. Original main was
rechecked clean at 025b7f3. No full inventory performance claim follows from this.

`22d4399`: pure immutable mod classification completed for supported PSD1 inputs.
Typed physical rows preserve source-specific pin/version presence; metadata applies
to duplicate IDs without sorting or mutating input. Name/Side/Category presence,
case, null/array/table display and successful table-array projections match the
baseline; actual legacy override runtime failures now receive E07 diagnostics.
Warning-free Native AOT and all-project format/build, 17 own successes/four E07
failures, immutable/idempotent contracts, 19 editorial and 15 protocol regressions
pass. Independent black-box review passes 55 cases (47 success/eight E07); white-box
review passes seven extra successes/three failures and the ownership checks.
It additionally recommends collapsing a projected singleton field group for typed
metadata constructed in memory, a shape outside current literal parser fixtures.
`72e097a` closes that recommendation separately: a singleton projected field group
collapses to its scalar value, matching the actual module's programmatic nested
array case. Native assertion, 17-success/four-error classification regression and
15 protocol cases pass; all-project format, Release and Native AOT are warning-free.
White-box source and native assertion close the general typed contract.

Default resource order reading completed as an internal native capability.
Lexical selection and reversal retain duplicate/comment/nesting behavior while
preserving empty successful cases and improving actual failures with E08. Positive
matched-ID limits are checked before missing-file returns and result additions.
Warning-free Native AOT and all-project format/build, own eleven successes/four
failures, count/blank-line controls, native contracts and 15 protocol cases pass.
Independent black-box review passes 52 cases (38 successes, five Unclosed errors,
six E08 errors and three policy controls); white-box passes nine extra successes,
two errors and quota/adversarial controls. No public inventory switch yet.

Pure resource activation completed. Immutable active rows preserve ID order,
duplicates, one-based priorities and builtin/missing/physical source distinctions.
First-wins filename indexing and active filename sets use InvariantCultureIgnoreCase;
inactive Name sorting is stable/current-culture. Active-only editorial names reuse
the shared typed projection, with an ordinal per-ID cache and E09 failure handling.
Resource-only ZIP discovery ignores unrelated kinds. Warning-free Native AOT and
all-project format/build, own seven successes/six E09 cases/isolation, classification,
ZIP, native invariants and 15 protocol regressions pass. Independent black-box 54
cases and white-box seven additional successes/five E09 cases, isolation and cache/
immutability controls pass. This remains internal rather than a public inventory.

Full raw inventory composition completed. Interleaved readers preserve failure
priority; managed and ZIP quotas span all applicable kinds, and every policy is
validated before inventory IO. Mods/shaders sort stably while resources and original
JAR observations retain discovery identity/order. Inspector buffers complete JSON
and keeps typed numeric diagnostics. Warning-free Native AOT, all-project format/
build, own snapshot/priority/identity/aggregate/policy cases and managed/JAR/ZIP/order/
native/protocol regressions pass. Independent black-box review passes 63 invocations
plus 17 strengthened assertions with unchanged fixtures; white-box closes one exact
snapshot, two E02 priority assertions, suppressed-ZIP quotas and four extra invalid
policies. This does not yet replace public content list or provide complete health.
`3551bfc`: pure inventory selection completed. Immutable groups preserve ordering,
aliases, linguistic membership and ordinal substring matching across field boundaries.
Read-before-filter priority and exact domain hints remain compatible. Warning-free
all-project formatting/Release/Native AOT, 17 own successes/ten domain errors,
native contracts, raw inventory and 15 protocol regressions pass. Independent
black-box 94 cases (68 successes/26 errors) and white-box seven additional successes/
six exact errors pass; the earlier InvalidSide hint mismatch is corrected in the
final artifact. No reference/cache/health effects or public content-list switch.

Category definitions, reference assignment and persisted cache validation are
recorded below. Public content list still uses legacy.
`1c24204`: direct ResourcePacks member projection corrected separately from mod
binding. Singleton wrappers enumerate one field layer; no produced values fail,
explicit null remains a value, and mod singleton empty Name stays successful.
Warning-free locked restore/format/Release/Native AOT, expanded own resource nine
successes/eight E09 and mod 19 successes/six E07, inventory/native/protocol regressions
pass. Black-box 139 invocations (30 new, 55 classification, 54 resource regressions)
and white-box twelve additional contrasts close the correction with intact fixtures.
`974f42d`: PSD1 import duplicate-key validation now matches PowerShell's linguistic
collision guard while runtime tables keep ordinal lookup. Independent table scopes,
original key spelling and valid noncolliding Unicode keys are retained. Warning-free
locked restore/format/Release/Native AOT, 25 rejection assertions and scope/lookup
controls, 24 own editorial pairs, raw inventory and 15 protocol regressions pass.
Black-box 86 invocations (34 key/scope cases plus 52 editorial regressions) and
white-box ten independent pairs close the guard; fixtures remain intact.
`602244a`: pure ordered category definitions completed. Direct-table versus member
projection, optional/default fields, Int32 rounding/limits and all-category validation
retain legacy behavior. E11 improves actual failures and E12 stabilizes priority/name
ties. Warning-free locked restore/format/Release/Native AOT, own twelve successes/
seven E11 cases plus ordering/empty controls, native assertions and 15 protocol cases
pass. Independent black-box 71 cases (42 successes, 25 E11, four invalid imports)
and white-box 22 pairs plus deterministic tie controls pass with unchanged fixtures.
`237f0c5`: pure reference assignment completed. Immutable typed wrappers retain
original rows; cache projection records every occurrence and visible numbers use
last assignment by object identity. Categories are validated even for mod-free
views; null categories remain unreferenced and explicit unclassified duplication
is preserved. Grouped sorting avoids category-by-mod scans. Warning-free locked
restore/format/Release/Native AOT, native equal-record/repeated-object/null-category
controls, seven own E13-derived full views, 27 selection and 15 protocol regressions
pass. Independent black-box 28 original plus four separately captured E13 cases
(36 native calls), and white-box original/derived views, error priorities, no-write
and shared-parser controls close review. Original tie differences remain recorded;
this is not blanket legacy tie parity or a persisted/public inventory capability.

`702fa0e`: category baseline projection uses PSCustomObject before property sorting;
one extra full multi-category comparison passes against frozen native v34, alongside
existing cases. This corrects the test harness, not product behavior.
`19266fe`: version-1 cache persistence completed. Streamed UTF-8 data retains all
occurrences and exact UTC O timestamps; an actual byte quota precedes writes, and
only a complete closed owned same-directory temporary replaces the cache. Failure
preserves the prior file; cleanup does not delete unrelated temporaries. Warning-free
locked restore/format/Release/Native AOT and own actual legacy read/resolve, Unicode,
exact/over quotas, invalid policy, locked replacement, cleanup, empty replacement,
handle/source integrity and numbering/protocol regressions pass. Atomic rename was
blocked by the default sandbox; the same new isolated temporary fixtures passed
outside that restriction. Black-box 27 controls include twelve full caches and 125
resolved legacy references; white-box checks include four concurrent writer pairs.
Neither cache crash durability nor source transactions/public dispatch is claimed.

`e1ceeb2`: bounded reference-cache read and pure numeric lookup completed. Preserve
extensible full rows, legacy token dialect/date kinds, last-occurrence duplicate
property order, deferred field/cast priorities, occurrence counts and selected-row
ownership. E15 improves only actual raw member/cast failures; existing read/domain
messages remain intact. Six resource bounds are explicit; no write or public switch.
Three projects pass locked restore, format verification, Release and Native AOT
without warnings. Final host v45 SHA256:
BEA0891E81CE5D9831FE0BFAE08E51FFCB03DAA2D6975784244B047AD22728CF;
CLI v52 SHA256:
9D69995BD9915E29D08ADF42700349D0F457EA418F6317390B512AE72BB91AB4.
Own 197 full differential/consumer checks, native assertions, seven numbering and
15 protocol regressions, and real isolated atomic writer checks pass. Independent
black-box final 395 checks plus ten threshold probes pass with intact fixtures;
white-box closes 51 cast/priority pairs, nullable consumers, duplicates and native
assertions. Earlier unchanged decoder components retain independent date/dialect/
budget/constructor checks. Frozen baseline and earlier findings are preserved.
Newtonsoft 13.0.4 token APIs are statically compiled, provenance pinned and MIT notice
shipped; no external managed DLL, JToken graph, reflection serializer or PS runtime.
One independent 1 Mi-character control-expansion sample used 47.36 MiB observed
working set and 113 ms; this is not a hard peak bound or complete performance gate.
The writer flushes around 16 KiB, but one escaped token can temporarily exceed that
buffer size, bounded separately by its lexical quota. No 16 KiB peak-memory claim.

Next correction: shared quoted numeric fields (category Order and descriptor
SchemaVersion), preserving existing errors and parser grammar. Then continue selector
consumers, health/declarations/providers and public read-only integration. Source
transactions, mutators, recovery, updater and full public performance gates remain.

`0ba0245`: quoted scalar Int32 compatibility shared by category Order, project
SchemaVersion and reference-cache strings. Original PSD values remain available
for diagnostics; nonstring branches and literal/import grammar are unchanged.
Malformed radix converter arguments normalize to FormatException at the scalar
boundary so actual category failures remain E11; no broad consumer catch added.
Independent baseline found +0x must cast zero. Preserve empty signed/suffixed hex
zero and keep missing binary digits invalid; prior failed artifacts stay frozen.
Three-project locked restore/format/Release/Native AOT is warning-free. Final host
v48 SHA256:14EFC49D2D0381FD17F0491509917118354B94D83FD19B0846E300D7AC7B6B77;
CLI v55 SHA256:91458923DF5230E21F5FFD0680B612854DE2000BD8202BA015226ADF7F9559EA.
Own 59 category/project pairs,221 cache pairs,category/native/protocol regressions
and41 public project controls pass. Independent black-box130 category +3 project
pairs and white-box typed/wrapper/giant/cast/empty-prefix cases close review with
unchanged fixtures. No public content-list switch or global performance claim.

`d6c65f6`: pure Packwiz update-target selection completed. One per-call alias index
uses occurrence ordinals, avoiding repeated full-inventory scans and per-row alias
sets; collision lists allocate only when needed. Physical resources, original row
references, ordinal matching, complete errors and invariant path dedup are retained.
Unused category Order stays deferred; every source/path check precedes dedup. E16
stabilizes only ambiguous diagnostic ties. Stem preserves legacy regex dollar-anchor
behavior before one LF, with no trim or path normalization. No cache/network/writes.
Three projects pass locked restore/format/Release/Native AOT without warnings.
Final host v50 SHA256:60F9F97BE4E84921A95D77EAE5B1D9E1337982615D1D4CEFD551AB9EF4A4C372;
CLI v57 SHA256:C8B89046B234C88B499176297C7811617957C58BAAF58DA77B8B1680E15F04F9.
Own37 differential/grammar controls and exact1024 selector boundary, native parser/
identity/path-culture/whitespace/E16/three-LF cases,27 selection and15 protocol cases
pass. Black-box58 original pairs +8 grammar controls and white-box10 independent
pairs +two priority/grammar controls pass with intact fixtures. Native assertions
cover synthetic identity/LF/E16 cases separately; blind file-based coverage does not
claim them. Earlier managed preflight was followed by the required native gates.

`6bf8f76`: cached Selector text resolves without cloning a complete selected row or
walking unrelated date metadata. Full-row callers still own their clone. Null means
nonnumeric input; stored null/empty is a real empty string. Shared row selection
retains all earlier validation priorities. Three-project locked restore, formatting,
Release and Native AOT publish complete without warnings. Host v51 SHA256:
96136933F3BABC13175FA1926B8481DA2DFBCB17FD0D547213267409113C995A;
CLI v58 SHA256:7E7365BEFDBEA11388FB90D0A7EB63C53C27B25DFAF5207123A36B850C0E9060.
Own226 differential/consumer cases, native invariants and15 protocol cases pass.
Independent black-box395 plus10 threshold calls and white-box14 text/priority pairs
pass with unchanged fixtures. No public dispatch or timing improvement claim.
Next batch contract deliberately fixes one cache generation and buffers failures;
root absence and invalid-element transport priorities are documented separately.

`185d7fb`: numeric/literal batch composition completed, using a single lazily read
owned snapshot, a fixed caller clock, per-call raw numeric text memo and ordinal
case-insensitive result dedup. Empty mapped strings are retained; empty helper
input succeeds. Complete output is buffered. Fixed cache generation and global
transport grammar priority are explicit refinements; no public dispatch/mutation.
Three-project locked restore/format/Release/Native AOT completes without warnings.
Host v52 SHA256:59DC57563407434B33D605AA4C547ABB2C19B252E794334B4B985C02385ECDD7;
CLI v59 SHA256:7166B2198DD6092176C18D126A2A0BCA2C8B6A57FB8A8615311DE04F706E134E.
Own41 controls, native owned-snapshot replacement/deletion/24-hour/default-policy
assertions and15 protocol cases pass. Blind72 comparisons and white12 independent
pairs plus7 invalid-input and locked-empty noIO controls pass with intact fixtures.
The own preflight harness initially used the wrong cache basename and enumerated
empty JSON arrays; both were corrected before final native acceptance. Original
independent captures were retained. No claim of a concurrent-writer stress test.

`9c51063`: classification targets completed. Original mod rows are selected through a shared
occurrence-aware alias index, with locals allowed and repeated queries retaining
repeated original references. Update selection retains its independent type/source/
path/dedup policy; E17 only stabilizes ambiguous ID diagnostic ties. Three-project
locked restore/format/Release/Native AOT completes without warnings. Host v53 SHA256:
CC81DDB77B300BEC89069745B09C14199E5FC187812E9FE3B84331ADE8266033;
CLI v60 SHA256:D7D067020E6CB1D3214E4E3D8D6456FB6401C21D6CF15AA2D524C4F20C3749DB.
Own25 classification controls plus full-read priority,37 update controls, native
identity/LF/E17/batch-owned assertions and15 protocol cases pass. Independent blind
46 classification pairs plus8 grammar controls and58 update pairs plus8 grammar
controls pass; original two E17 captures and separate expected ties remain intact.
White8 classification pairs plus2 grammar/read priorities and10 update regression
pairs plus2 priorities pass with unchanged fixtures. No classification/side writes.

Resource-target Native v54 remains failed and uncommitted:62/66 blind checks pass;
four active Unicode metadata cases reveal an earlier imported-table lookup defect.
Preserve that artifact and original captures. Resource work is saved byte-for-byte
outside build inputs while a separate parser correction is validated and committed.
SafeValues7.6.6 and fresh probes distinguish captured CurrentCultureIgnoreCase import
lookup from ordinal literal lookup; lexical duplicate guards remain ordinal. Native
v55/v62 will validate the import correction alone; resource work resumes afterward.

Imported-table correction completed as a separate native checkpoint. Capture
CurrentCultureIgnoreCase per PsdReader instance/root/nested/array table, with ordinal
syntax duplicate guards and unchanged programmatic comparers. Three-project locked
restore/format/Release/Native AOT is warning-free. Host v55 SHA256:
6B7C482631465414B335FAD9575D259ECDCF2B0B894219A1A0ACE561046528AB;
CLI v62 SHA256:465D57E6178A1B85793C239CAC57BFD96499780585F9A2D1400AB3EF5CC57025.
Own12 culture comparisons,24 editorial cases plus malformed input, resource view9
successes/8 improved errors/kind isolation,59 quoted integer pairs, classification19
successes/6 improved errors, raw inventory4 snapshots/4 quotas/5 policies,27 selection
cases, public projects31+10 cases,15 protocol cases and native captured-culture/
programmatic assertions pass. Blind77 imported/publicRoot/resource-view controls and
white12 import pairs plus native assertions pass with unchanged fixtures. Resource
work resumes separately; v54 stays failed evidence, not a completed checkpoint.

Resource activation target selection completed after the separate940dd71 imported
table correction. Per-call ordinal alias indexing retains active/inactive/builtin/
missing/editorial five-field targets, null filenames, empty metadata identities,
duplicate occurrences and repeated request order. Invariant represented membership
and imported current-culture lookup remain distinct. E18 preserves actual candidate
construction failures; E19 canonicalizes editorial ordering, including the explicit
Turkish successful representative for imported-distinct/invariant-equal keys.
Three-project locked restore/format/Release/Native AOT completes without warnings;
final comment receives an additional Core format verification. Host v56 SHA256:
57C04EA2284EF51ABB7FADB6FC18B71A4EE04C556CC991AC732EE03D032DCD06;
CLI v63 SHA256:5873DF7274799672187272477029ACEE07EECEDA9F1494179241209785A9C709.
Own30 controls, exact1024 repeated-output boundary, native null/empty/identity/
invariant-skip/ordinal-match/E18/E19-insertion-reversal assertions and15 protocol
cases pass. Blind66 original controls pass with unchanged fixtures, including all
four previously failing E09 priorities. White9 full-field pairs,4 cultural E09
priorities, E18/grammar and native assertions pass. v54 failed evidence and original
captures remain intact. No resource ordering/config writes or DefaultOptions gate.

Default Options detection/status/prerequisite completed. Find returns the original
first mod by ordinal ID/name or current-culture nonbacktracking filename pattern,
without Source or physical-JAR restrictions. One config-leaf observation produces
coherent Installed/ConfigPresent/Ready; AssertInstalled checks only Installed and
retains the complete original prerequisite error. Fresh regex culture versus the
old PS first-compilation cache and one versus two config observations are explicit
refinements. Three-project locked restore/format/Release/Native AOT is warning-free.
Host v57 SHA256:DF29AAE5B0FC8E13081CE3A5D22AA5493C0A69A363F3BE2B415DA07336C4E6AD;
CLI v64 SHA256:51F1A85F5A0A0ED4BCA473EF268985992B2413BB60706025605321527A81A18B.
Own34 complete-status/mod/grammar controls, native original-reference/local-first/
no-config prerequisite/LF/current-culture assertions and15 protocol cases pass.
Blind72 status/assert calls and white6 full-status pairs plus5 grammar/full-read
priority controls pass with unchanged fixtures. Historical warm Turkish regex
captures remain separate from explicitly declared fresh-process expectations.
No config/source writes, installation check by JAR existence or public dispatch.

Category-cache reading and category-ID resolution completed as bdc920c. Category
schema remains unused; timestamp/project/index/Id priorities, ordinal keys, reserved
bypass and null-versus-empty row semantics remain distinct from inventory. Shared
bounded file decoding retains owned JSON/date metadata and all six limits. E20
provides context only for actual raw consumption failures. Three-project locked
restore/format/Release/Native AOT passes without warnings. Host v58 SHA256:
ACB926909AC290AB1F984C999EF3B82E4A912084C8B3FC89F4097BE61AEB361A;
CLI v65 SHA256:A92E1CA38F009D3AE320352A711F9863E2C25FE43BECC310EBCB869C0C6FECFF.
Own89 category controls,226 inventory comparisons,15 protocol cases and native
assertions pass. Blind164 category controls,395 inventory comparisons plus10
threshold probes and white19 category plus14 shared-consumer cases pass. Eight
blind fixture integrity manifests remain true; handles close and stderr stays empty.
Managed preflights are separate from this native acceptance. An initial build command
used an unintended empty dependency-cache path; it was stopped and rerun against
the prepared workspace cache with the same locked versions, and its owned scratch
directory was removed. No successful checkpoint relies on that aborted restore.

Next capability is category-list projection, separate from category-cache writes.
Original34 blind captures and9 white focused oracles show editorial counts differ
from physical counts, count errors precede definition/inventory errors, and category
list Id ordering is cultural rather than inventory-reference ordinal. Prepare counts
and definitions before physical IO, compose immutable numbered rows afterward, and
reuse one validated metadata snapshot. E21/E22 and the coherent observation refinement
are specified before implementation; public dispatch remains PowerShell.

Category-list projection completed as9718822. Separate immutable editorial
preparation and physical composition preserve orphan assignment counts, physical
occurrences, duplicate defined/appended unclassified rows, cultural Id ordering and
one-based category references. E21 contextualizes raw assignment failures; E22
stabilizes diagnostic keys without replacing ordinary cultural sorting. One metadata
snapshot is an explicit coherent-observation refinement. Three-project locked
restore/format/Release/Native AOT passes warning-free. Host v59 SHA256:
575E35752E025EA92074407BEEC7BDCFB4C6CF63773DB16F01D8CBC7974A899D;
CLI v66 SHA256:A21185F04CA4D91C85225C030C3BA07F0A6DE4ED9B408595708F040B9AD2E147.
Own26 category controls,4 full inventory snapshots/priorities,4 quotas/5 policies,
7 numbering views/no-cache-write,15 protocol cases and native assertions pass.
Blind41 category controls plus28 original/4 explicitly E13-stable numbering cases
pass; four original E13 tie differences remain saved and are not marked identical.
White9 original oracles/3 stage controls/native assertions pass. Six blind integrity
manifests remain true. The malformed unrelated TOML line physical-stage control uses
the already declared E02 strict-reader refinement, not a legacy parity claim.

Next checkpoint persists category references atomically from the typed view, with
fixed-clock full-file comparisons and reader/resolver compatibility. Extract only
the existing inventory writer's bounded atomic file mechanics, then run its complete
writer regression before acceptance. Category writes remain outside source-pack
transactions/public dispatch, with E23 diagnostics specified before implementation.

Category-reference persistence completed asfe1e308. The typed view writes only
SchemaVersion/CreatedUtc/ProjectId and Index/Id/Name occurrences, retaining seven
UTC fractional digits, original numbers/text/order/duplicate IDs and initialized
empty arrays. Internal bounded atomic JSON mechanics are shared with the existing
inventory writer without changing its mappings, flush sites or errors. E23 covers
policy, byte quota and IO/cleanup diagnostics. Three-project locked restore/format/
Release/Native AOT passes without warnings. Host v60 SHA256:
8CCEB0F861313F1E0AC52EACED24E552937A9CD9A2FBECA909C1293978E94F55;
CLI v67 SHA256:AEBEBC0578C04DFBF44DD34CDDDA9CA3D6A99CE5F2926D0392E96D1704B13EDF.
Own30 category write/file/legacy-resolver/quota/locked/policy checks, inventory
atomic suite,15 protocol cases and default native assertions pass. Blind31 category
controls plus27 inventory writer regressions pass, including nine full category
files/21 legacy category resolutions and125 legacy inventory resolutions. Eight
concurrent category writers produce a complete candidate; two controlled completed
replacements establish ordered replacement behavior, without inferring process-exit
order or durability. White full-file/legacy-resolution/atomic/source-integrity and
focused shared-inventory-writer probes pass. Two blind pack integrity manifests stay
true and native stderr is empty. Actual IO uses approved escalation solely for owned
GUID fixtures; default sandbox denial of the original Move-Item is not a core bug.
The diagnostic empty-view producer remains confined to CoreContracts. Public commands
still use PowerShell; no source-pack transaction or installation was performed.

Next analysis covers strong source snapshots and change detection before health/
transactions. Preserve hidden/system files, .git exclusion, rejection of filesystem
links and full SHA256 content hashing. dist belongs to transaction snapshots even
though the health fingerprint excludes it. Independent analysis found the old
cultural Sort-Object -Unique can omit changes between ordinally distinct paths;
the next contract must explicitly repair that safety defect rather than reproduce it.

Protected source infrastructure completed asbd1b11a. Frozen owned snapshots hash all
included file bytes, retain hidden/system/dist, preserve Git/link/root-file semantics
and impose bounded counts/full paths/actual bytes. Complete changes correct cultural
unique and Keys-member shadowing losses under E24; E25 stabilizes health identity and
provides a distinct length-framed full-source plan identity. E26/E27 prevent partial
read results and actual case-insensitive filesystem aliases. Three-project locked
restore/format/Release/Native AOT passes warning-free. Host v61 SHA256:
18D909635FA0F34EDD1FFAB5231F6C9A7A378002A686F3C08FB7594BB044F806;
CLI v68 SHA256:4B2447424FDB7D257F47F346723C57DB1A9007B164668E99C5BF240ADE1CE85E.
Own23 source/hash/encoding/quota/IO controls,15 protocol cases and native assertions
pass. Blind67 complete tree/change/hash/policy/link/case-sensitive-filesystem controls
pass with three unchanged fixture audits. White independent framing/culture/delta/
encoding, all four quotas, junction/hardlink and ownership probes pass. A provisional
blind comparator itself used shadowed .Keys; fixed enumeration and a deliberate bad
hash negative control precede the full unchanged-expectation rerun. Failed evidence
is retained. Actual file symlink creation was unavailable due administrator privilege;
cloud-placeholder creation was unavailable too. Neither is claimed runtime tested.
No atomic multi-file snapshot, transaction ownership, mutation or new speed claim.

Health/declaration prerequisites next need version ordering and then range evaluation.
Compare-MpVersion's general mode is deliberately SemVer-like rather than a standard
SemVer parser; Maven already has a reusable C# algorithm. Original129 blind pairs and
48 white General/Maven comparisons reveal nonunit signed integers, invariant cultural
equality shortcuts, null binding and distinct lazy/eager numeric failure priorities.
These contracts and E28/E29 are fixed before implementation. Range/provider/health
evaluation remains separate; public content commands still use PowerShell.

Version ordering completed as8f60748. Pure General/Maven comparison retains exact
signed integers and unknown General syntax, invariant cultural equality, null
binding and lazy/eager numeric priorities. Span scans avoid unconsumed General
Split arrays; closed Maven nodes and hard depth1..64 bound recursive comparison.
E28/E29 contextualize actual numeric errors and explicit resource failures without
inventing unknown results. Three-project locked restore/format/Release and both
win-x64 Native AOT publications pass without warnings. Host v62 SHA256:
B3F6BA966EBB3F40791463D1B8ABBBC6E4952848EDA044BF908B89AFE0786A28;
CLI v69 SHA256:F3642E433E5C07066E81C8AED13AEE2823E3484F875694DB5209F7FEA2440BDE.
Own106 comparison/policy/grammar cases,15 protocol cases and native assertions pass.
Blind158 cases preserve129 original captures plus25 quota/policy and4 grammar
controls; white48 original outcomes plus18 architecture/limit controls and default
native assertions pass. Native stderr is empty, original baselines remain intact.
Managed preflights are separate: default atomic-fixture rename restrictions required
approved execution outside the sandbox; an overlapping publish removed a managed
apphost during a preliminary harness run. Neither failed preflight is acceptance
evidence. The complete own harness subsequently passed against the frozen native host.

Range analysis now has167 original captures (72true/38false/45unknown/12raw errors)
plus50 object-property lowering probes. A closed typed Text/Any/All/Unknown boundary
will keep loader-data coercion outside evaluation, preserve tri-state short circuits
and avoid standard-range substitutions. This remains analysis, not implemented range
matching. Public content dispatch, providers and dependency health remain PowerShell.

Typed range evaluation completed asb0f33e7. A closed immutable tagged value keeps
Text/Any/All/Unknown separate from owned declaration lowering. General/Maven syntax,
tri-state short circuits, visited interval-shape checks and Int32 increment promotion
match the original. JSON member lookup is Ordinal; explicitly supplied comparers
preserve literal-table origins, including nested any/all. Input-resource decoding
preludes include inactive members, while evaluator quotas apply only to visited work.
E30/E31 and consumed E28/E29 provide contextual limits/conversion failures.
Three-project locked restore/format/Release and both Native AOT publications pass
without warnings. Host v63 SHA256:
4D70700F9095475558CD15A05BE83DDE4BBEFF2BA799FE503E07635DFDAEEF3D;
CLI v70 SHA256:F3837B623689198E0A9B386AA92A177A7DBA711F2307B305D36C4B64225BF59F.
Own44 range checks,106 ordering regressions,15 protocol cases and default native
assertions pass. Blind255 cases comprise167 originals,50 lowering,4 lookup controls,
31 new policies/quotas and3 grammar cases; white35 originals plus16 controls,48
ordering regressions and default assertions pass. Baselines167/50 remain unchanged,
stderr is empty and no product blocker remains. The own initial managed ANY mismatch
exposed a JSON-versus-literal comparer error fixed before publication. White's first
assessment read the wrong error-envelope field; black's provisional wildcard999.x
expectation omitted its generated1000 quota. Both assessment-only mistakes are
retained and their complete final reruns pass without changing original expectations.

Next declaration capability extracts Fabric/Quilt root manifest semantics into typed
requirements, aliases, sides, schema diagnostics and nested-path declarations.
Archive ownership/byte limits and actual nested JAR traversal remain separate effects;
no health result or supported-loader claim is inferred from display metadata alone.

Fabric/Quilt root declarations completed as f9eaa18. The pure parser owns ordered
typed values, aliases and recursive requirements, retaining partial prefixes and
separate root completeness/child-loop eligibility. E32 repairs Keys shadowing;
E33 bounds construction and decoding; E34 contextualizes incomplete declarations.
All three projects pass locked restore, formatting and Release warnings-as-errors;
both win-x64 Native AOT publications are warning-free. Host v64 SHA256:
856CF080DF97E2AB3BC12351667A9D2C01D91E698DEB12B55CC4CF9CE753FB0B;
CLI v71 SHA256:F11C01939CFAFD5D13BE9C2A3CB5F6DC469B2F6753CE999202BAD8A69E519657.
Own24 full-root/live-range checks,226 cache regressions,44 range checks,106 ordering
checks,15 protocol cases and default native assertions pass. Blind105 cases comprise
76 original-root cases and29 policy/quota/grammar controls; white78 independent
root/guard/codec/date controls and default assertions pass. Fixture audits and hashes
are unchanged and stderr is empty. Provisional assessment alias-shadowing and
alias-quota assumptions remain recorded separately from product fixes. White review
found nested array equality must use scalar collection filtering; that product error
was corrected and reviewed before publication. No archive traversal or speed claim.

Next checkpoint characterizes Forge/NeoForge root TOML declarations, substitutions,
dependency/features tables and Jar-in-Jar path intents. Use the pinned Tomlyn syntax
API and an owned semantic boundary; the existing lexical inventory reader cannot
stand in for full TOML declaration semantics. Actual archive traversal follows later.

Forge/NeoForge root declarations completed as adce511. The pinned syntax API builds
bounded owned TOML ancestry before semantic validation, then freezes exact names,
table/array distinctions and scalar values. Typed Maven ranges retain TOML uncertainty,
feature pipeline outcomes, warning order, version substitutions and partial prefixes.
Jar-in-Jar paths remain intents. Complex projections are reused within each parse;
diagnostic response bytes are independently bounded. Three-project locked restore,
formatting and strict Release checks and both Native AOT publications pass warning-free.
Final host v66 SHA256:CB0459E1B62CE020B4A6E9BDCC6FEE8A57B20B116198FCB1193DC58EAEF0406C;
CLI v73 SHA256:B492D90E33DAD8A22BAA3910D3A867903555388F9E23072FD84859DBFFF524B8.
Own26 full-root/live-Maven cases,24 Fabric/Quilt regressions,226 cache comparisons,
44 ranges,106 ordering,15 protocol cases and default native assertions pass. Blind133
cases comprise92 original roots and41 quota/policy/grammar controls; white69 direct
controls (47 original/semantic/culture/ordering pairs and22 limits/output cases),35
Fabric/Quilt regressions and default assertions pass. Hashes, fixture audits and
empty stderr are confirmed. Provisional v65 passes85 but fails2 of92 expanded roots
on Boolean ordering equality; the corrected v66 rerun passes unchanged originals.
One assessment-only empty-code-array comparison error is preserved separately from
the product correction. No Forge health, actual child IO or new speed claim follows.

Before archive integration, review repeated array-valued Fabric/Quilt version casts
for bounded ownership and allocation reuse; then implement the real root archive
facade and bounded nested traversal as independent, testable checkpoints. Keep
public content commands on the current engine until complete health/wiring gates pass.

Scoped root text reuse completed as abc0d3e. Non-string formatted nodes use an
invocation-local reference cache at their existing lazy conversion points, preserving
all access/failure priorities. Three-project locked restore/format/strict Release and
both Native AOT publications pass warning-free. Host v67 SHA256:
51BE3293DAAF1DC125BFB02A8F0BA44BB00F7DEDA84837626C01B2769DA600F1;
CLI v74 SHA256:E20D0D4B92840176F348F3AC7AA17DDC12E41964A0A5B75AB9976DA9134A2262.
Own24 root,26 Forge,44 range,106 ordering,15 protocol and default native assertions
pass. Blind141 cases comprise112 unchanged roots and29 policy/grammar controls;
white8 original priorities,19 root regressions, default assertions and36 independent
before/after process samples pass with unchanged output hashes and no source blocker.

Parent benchmark results are in benchmarks/root-text-projection-current-results.json:
seven alternating fresh processes per engine/shape,48 aliases,two8192-character
version parts, warmed binary/input filesystem caches, including parse/serialization/
stdout/startup. All response hashes match. Array sampled peak working-set median is
12193792 old versus11083776 new bytes; scalar11456512 versus11091968. Wall medians
64.6634/63.86ms for array and65.0743/67.3412ms for scalar are not a speed claim.
White's separate three-trial samples, retained in
benchmarks/root-text-projection-independent-samples.jsonl, instead increase
working-set for both shapes (48-alias array10260480/11091968 and scalar
11005952/12120064 old/new median bytes);
the noisy whole-process measurements do not establish a robust memory reduction.
Keep both results, distinguish sampled OS working-set from GC allocations, and do
not generalize this diagnostic fixture to public CLI performance. Source review
confirms one formatted result per reused owned node; a separate native allocation
evaluation may quantify that narrower cost. Actual archive ownership is next.
