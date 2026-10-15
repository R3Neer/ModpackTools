# Capability90: pure conservative removal planning

Typed graph/environment/runtime overrides and physical content nodes, selectors,
type/cascade/autoremove/strict. No file writes, network, tools or inventory IO.
Reuse graph validator4/key2 with RAW baseline and cumulative graph-work budget.
Resolve aliases once, deduplicate requested IDs retaining order. Repeated graph
passes remove only owners of newly introduced required conflicts when cascading;
otherwise Dependents precedes pin errors. Autoremove requires baseline complete,
builds conservative edges across all Any/All/Unless/optional references (ignores
side/enabled for reachability). Skip entire top-level incompatible/discouraged
requirement trees, not individual descendant kinds. String alias identity stays
separate from non-string manifest keys.

Roots retain explicit, pinned, local and unaffected transitive items. Only affected
unreachable transitive managed items can become unused dependency. Pins are checked
after cascade/autoremove; final raw graph policy enforces strict if requested.
Requested and Changes order are contractual; retained graph is a set keyed by ID,
not legacy Hashtable enumeration order. Stable ordinal tie break for culturally
indistinguishable sorted IDs is an explicit determinism refinement.

Bound graph passes, cumulative work, retained edge count and diagnostics. All
limits fail closed without a partial accepted plan. Cancellation is checked during
work. Immutable results retain original node values; materialization is separate.
Black5 original pure cases (dependent priority, cascade chain, conservative
reachability roots/alternatives, cascade pin, incomplete baseline). White adds
independent resource/cancellation and non-string alias or raw-baseline controls.
Parent covers requested pin and limits as focused defaults. No public removal or
physical deletion is represented as complete by this checkpoint.

Accepted Core113 SHA256 D762FB6A0C49888BA35AF5A119613554AED7C6C58275703BA3BC0784D3D5F5F6.
Strict Release and Native AOT have zero warnings; changed-file format verification
and native default contracts pass. Black5/5 compares complete reachable nodes,
ordered changes/requests, raw reports and all structured error fields. Original
formatted errors are reconstructed from the native separated message/hint/details
using the original formatting rule, without new oracle calls. Node67 fixture
defaults (File=null, Published empty, Category=null) are explicitly projected;
the product planner retains complete original typed nodes without reconstruction.
White2/2 verifies cumulative work exhaustion after baseline and non-string versus
string manifest alias reachability. Parent also covers cancellation, null input
nodes/intent, invariant semantic tags, requested pin and planning quotas.
Source review corrected pre-index input limits and tag comparers before freeze.
MaximumEdges bounds both provider-owner links and dependency edges; physical text
and graph text each have the graph text ceiling. No retained enumeration order
promise. Reports: parent assessment/review-blackbox/removal90-final-report.md and
review-whitebox/capability-90-removal-planning.md. No public removal/deletion claim.
