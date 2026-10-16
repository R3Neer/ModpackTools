# Capability58: provider candidate availability

Scope: one complete typed provider candidate operation: project/list cache
observations, exact query, derived availability and explicit checked acquisition
reusing57's invocation-owned transport. Candidate-node construction and public
command activation remain outside this operation. Do not split solely because
helpers can compile independently; commit the complete observable operation.

ProviderProject owns canonical ID, title, raw project type, modern environment
strings and deprecated client/server side strings. Raw strings remain available;
side inference is a later composer responsibility. Supported internal kinds are
Mod, ResourcePack and ShaderPack. E43 deliberately supports the current provider
type shader and retains legacy shaderpack. Modpack and unknown types raise
Content.UnsupportedKind before candidate-list access. Current official project
API: https://docs.modrinth.com/api/operations/getproject/.

Project/list observations own immutable models, status, exact cache path, observed
UTC modification time and diagnostic. Reuse56's decoding policy and same-handle
mtime-before-decode, strict24h freshness, hash of exact endpoint, closed date-token
ownership and bounded reads. No JsonElement, syntax tree or open stream escapes.
List limits additionally bound total versions and aggregate collection entries;
quotas cannot reset for each row. Invalid policy fails even for missing input.
Checked lookup uses the client's invocation-wide57 acquisition/decode policies,
including returned-version and aggregate collection budgets before storing.
Offline policies are explicitly named offlineLimits/maximumOfflineVersions;
providing them to checked mode is rejected before requests or writes, rather than
pretending a post-store limit protected acquisition.
Malformed rows do not disappear from a known populated list. No silent fallback
to expired cache. Version identity/project provenance is checked explicitly;
intentionally stricter rejection must be documented against original captures.

Candidate query preserves parameter order and compact escaped JSON:
project/<canonical-id>/version?game_versions=<singleton-array>, then loaders when
applicable. Mod uses the supplied loader; Quilt uses ordered quilt,fabric.
Resourcepack uses minecraft; shader has no loaders. No include_changelog=false,
case normalization, deduplication or endpoint rewriting. Canonical IDs are ASCII
base62; slug resolution remains separate. Endpoint/resource bounds apply before
large intermediate allocations. Caller supplies Minecraft version and loader as
owned values, with explicit bounded string lengths.

Supplied typed project information takes precedence over cache; otherwise project
observation is consulted first. Missing project information yields Unknown before
list access. Availability is derived from project/list observations and selected
versions, not separately writable Known and Status flags. Valid fresh [] means
KnownEmpty; fresh populated valid list means KnownAvailable. Missing/expired list
means Unknown. Preserve provenance even when no candidates are returned.

Original finite characterization37: fresh null, whitespace and zero bytes are
KnownEmpty like []; absent/expired/exact24h are Unknown. Missing/null project info
precedes candidate-list access. Corrupt fresh project/list produces an error;
invalid/null/absent publication dates produce a sorting error. Preserve these
distinctions with actionable domain errors, without treating corruption as empty.
Query values are bounded to256 characters each before escaping, canonical IDs to
256 ASCII base62 characters;57's encoded endpoint quotas still apply. These are
explicit bounds rather than limits claimed to exist in the original.

Ordering uses captured DateTime ticks descending and the original cultural ID
ordering. Retain duplicate occurrences and stable input order for equivalent
keys. Do not substitute normalized UTC instants. Date parsing is consumed here;
unparseable dates must be diagnosed rather than silently becoming MinValue.
Exotic numeric/Boolean publications require an explicit refinement decision;
published API date strings and owned captured DateTime are the primary domain.

E43 refinements: canonical requested project identity and candidate project_id
must match; scalar/single-object candidate lists are rejected instead of relying
on PowerShell pipeline wrapping. Checked project null is rejected as malformed
provider data under E42, while offline fresh project null retains Unknown.
Invalid publication raises Metadata.InvalidCandidateDate/InvalidData with version
ID target instead of a raw SortExpression error. Corrupt cache raises
Metadata.InvalidProviderResponse/InvalidData with path/details. Unsupported kind
retains Content.UnsupportedKind. Equal-date/culturally equivalent-ID rows retain
input order explicitly; duplicate occurrences are not removed. Candidate list
date and identity validation precedes any checked cache replacement. Checked
candidate list stores exactly the query cache, with no invented per-version
cache writes. This operation does not accept a previously memoized arbitrary
PowerShell Context.Domains object; callers reuse owned observation results.

Acceptance: isolated caches with spaces, read-only hashes, mtime exact threshold,
project precedence, exact mod/Quilt/resource/shader queries, all availability
states, array/null/whitespace/malformed observations, identity, ordering, resource
limits and source ownership. Both reviewers, strict builds/locked restores,
warning-free Native AOT publishes and actual published tests are mandatory before
commit. Existing56/57 contracts regress; no live installation or real packs.

## Accepted checkpoint

Product commit f7a5a12. Three locked restores, format verification, strict Release
builds and both warning-free Native AOT publishes passed. Default published Native
contracts passed, including affected57 acquisition behavior and candidate checks.
CLI --version retained its existing schema1 result.

Frozen CoreContracts81 SHA256:
D11A6C34851486764B68900E35A7405C293237069055E0A7F51D2C6C605E8B1C.
Frozen CLI88 SHA256:
52EE732C91D8F1F30B3A3275A53356CC7ECCA824BDF2ED843835EDE79C9D9A28.
Earlier80/87 remain provisional, predating the checked array/null shape guard.

Black-box accepted33/33 controls (25offline,8actualTCP),18/18 evidence checks,
empty stderr and no owned temporary leftovers; original122-file evidence manifest
and three baseline sources remained unchanged. White-box accepted22 incremental
controls (20offline/query/provenance/identity/date/depth,2actualTCP) and default
Native contracts. Aggregate collection and checked-policy ownership were reviewed
in source; the inspector does not expose caller quota options, so no separate
runtime quota-control claim is made for58. Reports are assessment/review-blackbox/
provider-candidates-v81-final-report.md and assessment/review-whitebox/
capability-58-provider-candidates.md. Reviewer harness provisional failures remain
preserved and are distinguished from product mismatches. Neither reviewer found
a remaining product blocker. No public-command activation is claimed.
