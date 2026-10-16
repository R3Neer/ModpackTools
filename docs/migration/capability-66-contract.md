# Capability66: bounded dependency solution search

This internal capability implements the complete Search-MpSolution boundary and
its cost/downgrade behavior, without add/update/repair request preparation or IO.
ResolutionInput owns Before, Initial, Roots, captured BaselineKeys, environment,
runtime overrides and prior LastReport. Preparing/reconciling baseline before
root acquisition remains an outer orchestration responsibility. Normal exhausted
search returns Solution=null plus LastReport, not a premature Unresolvable error.
Successful Report and retained failed LastReport are separate immutable values.

IResolutionCandidates supplies an ordered domain with explicit availability and
lazily acquires an owned node for an exact identity/version pair. Invocation memo
freezes both observations and validates returned identity. The engine does not
open caches, acquire archives or write project files. Its future source adapter
must preserve installation facts and candidate evidence behind these contracts.

Preserve first-error DFS, Any/All/Unless target walk, direct-provider prepend and
owner-declared provider fallback, roots and BEFORE pinned/local constraints.
Only absent provider project requirements with KnownEmpty evidence become unknown.
Unknown evidence remains an error. Non-strict tolerates captured baseline keys;
strict unknown-only branches terminate without trying unrelated candidates.
A successful terminal state is costed and ends that branch. Exact equal costs
retain the first solution. Cost prioritizes changed, changed-explicit, added, then
negative publication DateTime ticks of ALL nodes, including disabled ones. Missing
before nodes have no deletion penalty. OrderingDate preserves captured DateTime
ticks, not a guessed UTC conversion. Downgrade uses first matching native mod,
BEFORE Maven scheme, any negative comparison, and date fallback only if no mod
comparison was known. Comparable zero suppresses fallback. Direct comparison API
requires acquisition-validated owned nodes; Solve validates and charges inputs.

Use explicit DFS frames and persistent immutable maps; replacements keep the input
sequence position and new identities append. This defines tie order rather than
inheriting unspecified Hashtable enumeration. Only current path, best, last and
owned memo evidence are retained. No cloned full map per pending sibling.

Identity refinement: canonical ordered, length-framed raw UTF16 identity/version
fields feed SHA256 instead of delimiter concatenation or replacement-prone UTF8.
Candidate versions stay exact; old case-sensitive invariant equality is retained
for current-version skipping and changed-cost calculation. Graph validator4/key2
continues the previously accepted diagnostic identity contract. Assume SHA256
collision resistance; never share visited entries across invocations.

Default10000 states plus positive bounded domains, candidate occurrences/nodes,
retained characters and fed identity bytes. MaximumWork is cumulative across all
states, including graph input characters/structure, actual requirement-node/version
checks, generated identity/message characters and fact versions. Internal optional
callbacks extend the shared graph machinery; public GraphReportBuilder.Build keeps
its old null-callback behavior and local quotas. Check cancellation around state
transitions/acquisition; failure/exhaustion never returns a partially found best.
No measured resolver speedup or public mutation support follows from this design.

Product5310b71. CLI99 SHA256:
3CA7EFAACF86322D77C824138334D5DB8A70E4ECF04959D347B40D54CAA36725.
Core90 SHA256:D52E1854BAE1537BF2584459C6756204813FC585D340756F31744E110DE64FC3.
Locked restore/format/strict Release and warning-free Native AOT pass. Actual
Native default contracts include backtracking, cycle memo, baseline, pins, exact/
over states, downgrade/fallback/Maven, raw UTF16 states, invariant cost and cancel,
plus existing graph/provider/artifact/health regressions. Parent public content
list differential/reference/Nu regression and protocol15/version/Nu session checks
pass. Black-box eight original semantic comparisons plus exact3/over2 states pass,
including full best/last reports under the accepted graph4/key2 profile, first
acquisitions, costs and visited counts;11 baseline files and two sources intact.
White-box16 controls/default pass. Its cumulative-work control permits initial
report/transition with MaximumWork1510/MaximumStates1 (state limit), then fails
MaximumWork with the same budget when another report is permitted. No huge stress
corpus, network, real project mutation or resolver performance claim is implied.
