# Differential comparison matrix

Status: acceptance matrix for the full migration. Completed checkpoints and their
limited evidence are recorded in implementation-plan.md; this matrix is not a
claim that every scenario has passed.
Legacy implementations/tests supply the reference, not an automatic correctness
oracle for known defects. Run each mutating engine on its own fixture copy.

| ID | Inputs/scenario | Required assertion | Purpose |
|---|---|---|---|
| C01 | Version, global/domain help, aliases | Same grammar/result; local version has no project/network dependency. | First protocol/CLI boundary. |
| C02 | Invalid/repeated/forbidden options, `--`, Nu option spelling | Equivalent rejection and no operation side effect. | Parser parity. |
| C03 | Explicit project, session project, project use/current | Same precedence and Nu environment update across invocations; help does not change session. | Context ownership. |
| C04 | Managed/local mods, active/inactive/builtin/missing resources, shaders | Full public inventory equality, empty/null/missing cases included. | Visible model. |
| C05 | Combined filters, categories, duplicate/ambiguous selectors | Same list ordering, displayed numbers, and next-command identity resolution. | Reference semantics. |
| C06 | Expired/corrupt/wrong-project inventory/search/category/version cache | Same error; numbers never silently renumber against new provider results. | Selection safety. |
| C07 | Spaces, Unicode, apostrophes, accented names, CRLF/LF | Exact argument/path transport and unaffected-file bytes; explicit collation comparison. | Windows and locale parity. |
| C08 | Public JSON, normal Nu, no-human Nu, process failure, domain failure | Exactly one final public result; correct unwrapping; no progress/ANSI leakage; no generic replacement of structured error. | Pipelines and failure transport. |
| C09 | Redirected terminal, NO_COLOR, color always/never, ASCII, narrow width | Functional content/roles/help/format behavior; fixed-width captures. | Presentation parity. |
| C10 | Missing/corrupt theme or R3CLI resources | Human diagnostic; machine-only core still works (intentional change). | Presentation independence. |
| C11 | Real PSD1/TOML, nested data, escaped keys, malformed/unsupported constructs | Same values or declared diagnostic; no evaluation, no implicit conversion. | Data boundary. |
| C12 | Fabric/Quilt/Forge/NeoForge, multiple mods, nested JARs, limits/ranges/aliases | Same declarations and unknowns; compare legacy Maven qualifier ordering. | Loader parity. |
| C13 | Provider client/server availability versus loader requirements | Same per-side requirements; provider availability never weakens loader declarations. | Environment policy. |
| C14 | Dependency alternatives/guards, cycles, backtracking, equal-cost ties, search limit | Same selected IDs/versions, cost priorities, failure limit and diagnostic. | Resolver parity. |
| C15 | Pins, local files, --all, exact --to, implicit/explicit downgrades | Same retained nodes, rejection, and permitted changes. | User intent. |
| C16 | Unrelated baseline conflict, affected conflict, unavailable provider/JAR, strict mode | Same known-error/warning/unknown split across list, doctor, build, and operations. | Consistent health policy. |
| C17 | Remove/cascade/autoremove, shared/explicit/local/pinned nodes, old orphans/cycles | Exact removed/retained set and references; no reinstalls. | Destructive selection safety. |
| C18 | Category batches, sides, resource enable/disable/move, duplicate targets | Same all-or-nothing metadata edits, priority and Default Options order. | Editorial/resource parity. |
| C19 | Dry-run, declined/default confirmation, --yes, no-op, add/update | Pack byte-identical when not applied; preserve each command's actual prompt policy and allowed cache writes. | Interaction parity. |
| C20 | External edit during copy/preparation, after preview, during commit | Reject or recover while preserving external data; plan identity enforced. | Concurrency. |
| C21 | Failure before first write and after selected commit writes | Restore originals and transaction-created files/directories; journal precedes writes. | Rollback. |
| C22 | Process kill, pending/committed journal, corrupt backup, external recovery conflict | Correct next-operation recovery or retained journal with actionable error. | Crash recovery. |
| C23 | Traversal/rooted paths, junctions/symlinks/dangling links, cloud placeholders | Same safe-path policy, untouched external target, valid cloud files supported. | Filesystem boundary. |
| C24 | Export mismatch/failure, strict/no-refresh build, stale/current/unverifiable artifact, diff | Semantic MRPack equality; previous artifact survives failures. | Distribution correctness. |
| C25 | Network outage, corrupt download/cache, changed file, missing metadata, cache invalidation | Correct hash checks, expiration, uncertainty, and no partial pack application. | External dependencies. |
| C26 | Relocated source, non-Documents install, open executable, package hash error, failed fresh-process smoke | Independent installed core, preserved theme/config, real rollback and correct adapter resolution. | Installer/updater. |
| C27 | Disk full, permission denial, cancellation at preparation/commit/recovery | Original data preserved or recoverable; failures truthful. | Fault tolerance. |
| C28 | Fixed workload/cache states, core versus adapter paths, offline resolver versus network | Raw samples, median/p95, memory or declared unavailable; performance targets and parity together. | Performance acceptance. |

## Fixture/harness requirements

Extract reusable data fixtures from `Tests/Engine.Tests.ps1` and related tests;
do not require the new core to run inside Pester or inspect renderer output.
Capture full public results and pack snapshots from the baseline first.
Control provider answers, clock/expiration, temporary roots, terminal width,
locale, installed versions, failure injection, and hashes. Record fixture identity.

Run legacy and replacement on separate copies with isolated configuration.
Never run both mutating a user's pack, or run fault injection on a live pack.
Do not erase a mismatch by dropping fields, sorting ordered arrays, rewriting
errors, or comparing only item counts. Keep failing pairs and classify them as
regression, deliberate approved difference, or legacy defect requiring a decision.

## Evidence status and gates

The initial language evaluation measured the unmodified legacy engine and diagnostic
startup executables. That evaluation did not validate C01-C28 against a replacement
engine. Subsequent native checkpoints have focused differential evidence recorded
in implementation-plan.md; they do not imply all C01-C28 or public-command gates pass.
Existing tests cover many cases; their existence does not establish current pass
status or full disk-full/process-kill coverage.

Read-only public switch: relevant C01-C11/C16/C25/C28 pass, both adapters use a
fresh process, numeric references remain compatible, and no pack file changes.
Mutation switch: relevant C12-C27 pass, including fault injection and journals;
one implementation owns mutations. Distribution switch: C26 plus installed
fresh-process checks; only afterwards consider removing the legacy installation.

Use independent inventories: (a) the real small metadata-only pack recorded in
the baseline; (b) synthetic scale fixtures with 100/500/1000 items and actual JARs;
(c) manifest-heavy/nested-loader fixtures; (d) failure/Unicode/link fixtures.
Do not extrapolate the first pack to all sizes or to online resolution.
