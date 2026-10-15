# Capability69: complete observation-based content plans

Accepted product checkpoint e1beb06. ContentPlan.PrepareAsync composes
checked initial health, root preparation, the owned candidate source and dependency
search for Add, Update and Repair. It returns typed selected content, Before/After
changes with requested/dependency reasons, ordered requested identities, baseline,
final report and operation. It does not execute writes or constitute a fingerprint-
bound transaction. Cache observation side effects retain their existing individual
atomicity; project files are never written by this operation.

Compatibility phase order follows New-MpContentPlan, not a line-by-line translation:

- Validate bounded request/policies, then capture checked Before, compatibility
  environment/runtime overrides and reconciled baseline using one acquisition scope.
- Add resolves saved search numbers before prefix/URL parsing, identifies a remote
  project, deduplicates canonical requested identities, acquires compatible versions,
  takes the first exact ID/version-number URL match, checks pin and acquires the node.
- Update consumes physical selectors already resolved by the public adapter. Numeric
  INVENTORY cache lookup belongs before this operation in that adapter; it is not
  repeated inside the plan. Update-all filters selected kinds, managed and unpinned
  items. Explicit pin rejection precedes unsupported-provider skip and version IO.
- Numeric update --to consumes the owned saved version list; literal --to does not
  read it. Both must select exactly one compatible candidate. Add URL matches first;
  update ambiguity is an error. Request Type is consumed only for Update, after the
  observation, preserving irrelevant-Type and baseline error priorities.
- Root downgrade checks run after all roots. Nonempty --to exempts the ROOT check;
  an Add URL version does not. Dependency search still uses AllowDowngrade separately.
- Add/Update tolerate baseline error keys unless strict; Repair receives no tolerated
  keys. Solver selected nodes must belong to this invocation's Before/materialized
  evidence; no second decoding or acquisition reconstructs the final plan.
- Preserve explicit initial node order and requested order. Changes sort by content
  identity using the established culture profile. Prewrite node intent stays observed
  or transitive; applying an Add promotes requested intent in the later write layer.

SavedProviderChoices owns lazy bounded search/version snapshots through the existing
cache codec, date table and projection primitives. Context, inclusive 24h freshness,
future timestamp behavior, global search choice and numeric row scan priorities are
retained. A concurrently replaced cache does not alter an already loaded snapshot.
No schema restriction or extra compatibility check is invented for saved versions.
Malformed inputs remain typed failures. An out-of-Int32 selector is improved to a
typed Content.InvalidReferenceNumber only after the first Index can be read; empty
lists and malformed first indices retain their earlier priority.

Intentional error improvement: the original Add URL branch can yield null for an
absent version and throw PropertyNotFoundStrict while reading .Count. Native
preparation returns Versions.Incompatible with its actionable version-list hint.
The original raw failure remains in the black-box evidence; no successful behavior
or earlier no-compatible-list/pin priority is changed to reproduce that accident.

Limits bound selectors, caches, acquisition/search graphs and failure details before
joining them. Invalid search/choice policies fail before provider IO. Cancellation
stays terminal, including before export and result publication. Runtime override
presence is captured in ProjectHealthObservation and reused by search, rather than
being lost when passing only a baseline report.

No public mutation, locks, staging, journal, recovery, setup/update or full native
runtime closure is claimed by these plans.

Locked restores, format verification and strict Release builds passed for all three
projects. Core95 and CLI104 Native AOT publications were warning-free; the default
published contracts passed. White-box review independently passed twelve focused
end-to-end Native controls plus default, including unchanged complete fixture trees,
baseline-before-root IO and physical numeric update selectors. Six draft findings
were closed before final freezing. Black-box review passed all seventeen invocations
across eight scenarios, using real original project/context/health/inventory/graph/
search/acquisition functions with controlled transport, and comparing full node67,
changes/requested/reports/operation under the already accepted E39 graph profile.
The one absent-URL-version error improvement is separately recorded above.

The valid original run was content-plan-baseline-20261015-153339; final Native run
content-plan-native-20261015-153545. Earlier characterization runs with manually
constructed Before values were retained, not silently relabeled: real pack-backed
Get-MpProjectState observations fixed the publication-date type in the oracle.
All 156 frozen reference files, five original source files and seventeen fixture
projects remained intact. HTTP was loopback-only, with zero artifact HTTP requests
in this finite resource/local-mod corpus. No real modpack or installation was used.

Reports in the enclosing reorganization workspace:
assessment/review-blackbox/content-plan-v95-final-report.md and
assessment/review-whitebox/capability-69-content-plan.md.
Parent CLI104 protocol15, version and fresh Nu checks passed. The default Native
suite also exercises shared health, acquisition, lookup and resolver contracts;
unchanged public read corpora were not needlessly repeated at this internal cut.

Final SHA256:
- Core95: 22A33B1B666806F24F267EE76D7A93EEE0D3093437F79B733E52DBAB14788E8B
- CLI104: B3C9582E7354AD1BA084B403CCFABBF2A02E17ADAB29D12432566B13AF4B62A3

Provisional94/CLI103 are retained and are not the final acceptance artifacts.
