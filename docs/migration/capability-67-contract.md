# Capability67: owned candidate acquisition for dependency resolution

ResolutionAcquisition is an invocation-owned IResolutionCandidates implementation.
CreateAsync captures initial health with the SAME provider/artifact clients and
content ledger used for subsequent candidates; FromObservedNodesAsync accepts an
already owned typed Before snapshot and debits that ledger. Observed Item/Graph
identities must agree before IO. Source getters expose immutable observations and
counters; no source/cache JSON enters ResolutionNode. Canonical content IDs have
the modrinth: prefix. Request aliases/slugs and root preparation remain separate.

ProjectHealth and candidates share ContentNodeReader/ContentNodeBudget. Preserve
installed Item/intent, semantic pin/optional/default, exact installed filename
precedence for the same version, dependencies and version-only identity lookup,
new transitive item/title/side/type/category, selected file, verified declarations,
and truthful warnings. Candidate listing downloads no artifacts; a selected mod
alone acquires its JAR. Resource/shader candidates do not parse/download JARs.
Missing installable File fails after node construction, before selected-cache store
and memo publication. Recoverable hash failure retains File metadata and uncertainty,
without inventing declarations or persisting mismatched artifact bytes. Node warning
diagnostics now preserve sentence/details/hint without requiring a renderer.

The source serializes mutable acquisition state, memoizes stable domains and
materialized nodes and cancels/drains before disposal. Initial health availability
passes through the SAME Domain guard/memo, so MaximumDomains is checked BEFORE the
next provider lookup. Ledger covers Before plus all candidates, nested provider/
manifest requirements, mods/aliases/warnings and their owned text/constraint wire.
No per-candidate ledger resets. Checked transport/model/artifact quotas remain
invocation-wide. Cancellation and resource exhaustion stay terminal.

Planning client opts into candidate evidence retention at CONSTRUCTION, before any
baseline request can fill endpoint memo. Each typed row is associated with its OWN
validated raw wire by exact version identity; conflicting duplicate IDs fail before
list store. Private aggregate wire quota applies only to this opt-in mode, leaving
standalone health/availability unchanged. Persist the selected raw version only
after construction and MissingFile check, before returning/memoizing it. Preserve
unknown fields and the existing bounded atomic timestamped metadata-store profile.
Never reread a mutable query cache or issue an extra version GET. Writer accepts
only this private already validated wire without WriteRawValue's unrelated default
validation-depth64 restriction; configured decoder/capture depth is authoritative.

OrderingDate shares candidate sorting's DateTime projection, including valid
publication-text fallback. Missing installed publication stays absent; nonempty
invalid publication fails explicitly when adapting to the resolution model, instead
of silently dropping its cost/downgrade evidence. Normal dates retain original
captured ticks, not a guessed UTC conversion.

No installed project files are changed by this capability. Metadata/artifact cache
writes are the existing observation side effects; no bound plan, add/update/repair
request preparation, transaction or public mutation is claimed. Full no-PowerShell
setup, update, human presentation and Nu grammar remain pending.

Accepted product checkpoint: 6764a98. Locked restores, format verification and
strict Release builds passed for all three projects. Both Native AOT publications
were warning-free. Core92 default contracts passed, including the initial-health
domain guard before the second lookup, shared ledgers and opt-in wire retention.

Independent black-box final92: six original-PowerShell comparisons passed for
installed Quilt, new resource/shader, missing file, version-only dependency and
hash failure. Complete node/cache evidence, request counts/order and handle release
were checked; all 42 frozen fixture files and four original sources remained intact.
Report: assessment/review-blackbox/candidate-acquisition-v92-final-report.md in the
reorganization workspace. This inspector tests FromObservedNodes, not CreateAsync.

White-box review accepted final92 default and a focused TCP case. Five findings
were closed: nested requirement accounting, raw writer depth, textual date fallback,
observed identity coherence and initial domain guarding. The earlier ten TCP cases
ran on provisional91 and are not attributed to final92. Report:
assessment/review-whitebox/capability-67-resolution-acquisition.md.

Parent CLI101 gates passed content-list differentials, protocol15, version and fresh
Nu machine checks. These public read regressions do not claim public mutations.

Frozen executable SHA256:
- Core92: 75CAB8C9C0552A7FC2F37ECE0B4C09FD15A8D605EDC5AD3A9F3E3C5E92C3391C
- CLI101: E8C3A369842052E95FB47EC7BF3ACC58464402E25DB40D472ADA8DBA3AC46330
- Unchanged Nu bridge: 907D82CA03341CFFFAA2E3C6B2EBA7A3D127C39C47099E3EC6F60143D2B86F2C
