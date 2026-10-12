# Implementation sequence and first checkpoints

Status: planned; no core/adapters have been implemented in this branch.
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

Remaining first action on implementation authorization: Phase 0, capability 1,
followed immediately by its green native checkpoint commit.
