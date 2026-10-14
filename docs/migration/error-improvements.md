# Authorized error improvements

The user explicitly authorized improving errors on 2026-10-12 during implementation.

## E01: consistent public argument type

Legacy domain failure envelopes accidentally unwrap arrays: no arguments becomes
null, one becomes a JSON string, several become an array. Native failures always
emit `arguments` as an array of strings, just like success and protocol failures.
This fixes a transport irregularity and removes special-case JSON handling from
consumers and the C# implementation. Preserve values/order, IDs/categories, target
null and useful diagnostic hints. No change to successful envelopes.

Differential tests may normalize ONLY this declared difference in baseline errors:
null -> [], string -> [string], array unchanged. All other fields/types still
compare exactly until another specific improvement is documented and tested.
The old bridge remains unchanged for unmigrated capabilities during coexistence.

## E02: actionable data-reader failures

Native configuration/project PSD1 failures retain their domain ID, category and
file target. Parser details identify a bounded literal-parser offset instead of
embedding PowerShell interpreter diagnostics. Invalid TOML manifests now fail with
`ModpackTools.Project.InvalidManifest`, `InvalidData`, the manifest path, parser
details and a repair hint; empty manifests also fail explicitly instead of leaking
a parameter-binding failure. The regex-based legacy reader could overlook malformed
unrelated lines. Schema coercion overflow, incompatible field types, inaccessible
files and invalid paths produce `Core.InputReadFailed` with a corrective hint
instead of leaking an interpreter/runtime exception. Exactly one envelope, exit 1
and no stderr remain required. Ordinary supported-data success remains unchanged.

These are specific diagnostic exceptions, not permission to normalize successful
values or discard error targets in differential tests. Invalid descriptor/config
cases compare ID/category/target and diagnostic meaning rather than interpreter
wording. Overflow and malformed TOML test the native failure explicitly.

Managed metadata uses Content.InvalidMetadata / InvalidData with the selected
metadata file target, details and repair hint for read, parse and compatibility
decoder failures. In particular, valid TOML with an unsupported historical
Regex.Unescape sequence (such as uppercase Unicode \U) remains a failure, now
structured; no new successful decoding policy is implied.

## E03: empty project catalog is a valid result

An existing configured root with zero immediate registered projects returns a
successful catalog with `items: []`. The legacy renderer rejects an empty collection
and leaks a PowerShell parameter-binding error, despite valid read-only input.
Separating domain data from presentation removes that erroneous failure. Nested
projects are still excluded, so a nested-only root also returns the empty catalog.
Black-box comparison records this exception explicitly; it must not normalize it
away as a supposedly identical result. Root/configuration errors remain errors.

## E04: empty project selection has a domain diagnostic

`project use ''` returns ModpackTools.Project.InvalidSelection, InvalidArgument,
an empty-string target and a `modpack project use <id>` hint. Legacy leaks a
mandatory-string parameter-binding failure before project resolution. An explicit
domain failure makes this diagnosable and stable without changing session state.
Whitespace-only input remains distinct: it falls back to the active session, as
legacy does. Null argument tokens are protocol failures, not empty selections.

## E05: valid empty TOML basic strings remain data

The legacy mandatory string decoder leaks a parameter-binding failure on valid
empty basic strings. Native reads them as empty strings: name, provider ID and side
apply their existing fallback rules; filename and version ID retain empty rather
than null. This is a field-specific exception, not general null/empty normalization.
It also makes shared project string extraction handle valid empty values directly.
Zero-byte managed metadata remains invalid and produces Content.InvalidMetadata
with a repair hint, like the existing structured empty-project-manifest policy.

## E06: optional JAR display failures retain typed uncertainty

Unreadable/missing fabric.mod.json retains the legacy Name/Side fallback, including
an already applied Name when environment lookup subsequently fails. Add a typed
Read/Missing/Unreadable/Limited status and a nullable diagnostic for later consumers;
no stderr or metadata exception escapes the optional display reader. Missing means
an absent exact entry in an otherwise readable archive; unavailable/corrupt archives
are Unreadable. These extra fields are native observations, not claims of health.

Actual archive-entry/central-directory/decompressed-byte budgets are explicit new
limits. Over budget produces Limited and filename/unknown fallback with diagnostic,
even when the legacy unbounded reader succeeded. Standard JSON coercions, supported
comments/trailing commas and Unicode replacement remain compatible. Unsupported
nonstandard JSON syntax is unreadable with a diagnostic. This does not authorize
general success normalization or use a permissive decoder for request protocol.

## E07: invalid mod overrides have an actionable domain error

Replace actual method/property/null runtime failures while applying an encountered
mod override with Metadata.InvalidOverride / InvalidData, the affected mod ID as
target and a repair hint. Abort without a partial successful result. Retain all
supported table and table-array successes, including optional singleton keys and
multi-table field projections; do not broadly reject every non-table entry.
Falsey entries keep their existing ignored behavior. Overrides for absent mod IDs
are not eagerly validated. No metadata is rewritten by this read-only operation.

## E08: default resource order decoding failures identify the file

Keep empty-file, empty quoted-ID and unsupported escape failures unsuccessful,
replacing parameter-binding/decoder runtime failures with ResourcePack.InvalidDefaultOptions
/ InvalidData, the config/defaultoptions-common.toml path and a repair hint/details.
Do not turn those failures into empty successful orders or phantom builtin IDs.
An unclosed target array keeps its existing domain ID and key target. Missing
file/key and an actual empty array remain ordinary empty successful results.

## E09: invalid active resource name overrides identify their ID

Replace actual method/property/null failures while applying a matching active
ResourcePacks entry with Metadata.InvalidResourceOverride / InvalidData, the active
ID target and a metadata repair hint. Present falsey entries are errors here,
matching legacy; they are not ignored as in mod classification. Preserve supported
empty-table, optional singleton and multi-table Name successes. Unencountered
editorial IDs remain unvalidated. Abort before a partial successful resource view;
there are no filesystem effects.

## E10: deterministic category names in unknown-category diagnostics

Retain Inventory.UnknownCategory ID/category/target and the complete available-key
list. Sort keys by the existing current-culture comparison, with an ordinal tie
break for distinct keys that collate equally (such as composed/decomposed Unicode).
This stabilizes diagnostic wording across dictionary enumeration orders. It changes
only the error's informational list; category membership, row filtering and future
numbered references do not acquire a new comparison or ordering rule here.

## E11: invalid category definitions identify their key

Replace actual category ContainsKey/property/Int32 conversion runtime failures
with Metadata.InvalidCategoryDefinition / InvalidData, the category key target
and a metadata repair hint. Evaluate all definitions even for an empty/mod-free
selection, matching legacy reference preparation. Preserve successful null fields,
array/table Name display, scalar rounding and singleton table wrappers. No repair,
metadata write, omission or successful partial result occurs.

## E12: reproducible ordering for equal category priority and name

Sort category definitions by existing Int32 Order and current-culture Name, then
ordinal Key only for ties. Five fresh PowerShell processes returned five different
key orders for one identical four-category input with equal Order/Name; no stable
legacy numbering can be preserved for that ambiguous input. Deterministic ordering
prevents regenerated references from drifting without content changes. This is a
limited successful-order change, not an error-format change. Ordinary category and
row ordering remains unchanged; keys that collate equally are retained separately.
E09 projection correction: singleton ResourcePacks wrappers retain member enumeration,
including failure when Name contributes no values. Direct-table empty Name remains
successful empty text; singleton wrapped empty Name is an actual StrictMode failure.
Mod binding uses a different initial wrapper collapse and must remain unchanged.
## E13: stable ordering for equal inventory row names

Inventory row Name ordering is current-culture/case-insensitive, preserving input
order among equal names. This explicitly differs from legacy Sort-Object Name,
which is unstable: [one(Name=Same), two(Name=Same), local(Name=local)] becomes
[local, two, one], while -Stable and the native core produce [local, one, two].
Adding/removing an unrelated differently named row must not swap equal-name rows
and their visible reference assignments. The change applies to raw mods/shaders,
inactive resources and Name-sorted reference groups; active resource ID order and
category Order/Name/Key ordering remain governed by their existing contracts.
It preserves every row and original spelling, including equal-name rows with
otherwise different paths, pins, sides or versions. It is an intentional successful
ordering/reference-number difference for ties, not blanket equality of snapshots.

Comparison harnesses retain original unmodified captures and identify affected
cases explicitly. For those cases only, derive a separate expected result from
the same PowerShell logic with its named row sorts made -Stable in memory. Compare
full ordered trees against that expectation; do not drop metadata paths, numbers,
fields, rows or sort the output after the fact to hide a mismatch. Non-tie cases
must still match the original baseline exactly. No baseline repository is edited.
## E14: reference-cache persistence failures identify the cache path

A positive serialized-byte quota aborts with Inventory.ReferenceCacheWriteLimitExceeded
/ LimitsExceeded, the final cache path and a narrower-view hint. IO, access or
replacement failures become Inventory.ReferenceCacheWriteFailed / WriteError with
path, details and a rerun/permission hint. Preserve the old complete cache and source
pack; do not return a successful inventory with an unusable newly written cache.
Temporary cleanup is limited to this invocation's same-directory file. Cache
replacement does not stand in for the later durable source-transaction protocol.

## E15: malformed reference fields fail at their actual validation stage

Replace actual StrictMode property and scalar conversion runtime exceptions with
Inventory.InvalidReferenceCache / InvalidData, the absolute cache path, details
and the existing modpack content list regeneration hint. Keep all existing domain
errors and their validation priority. An unused missing Source or Selector does
not fail a valid lookup. Supported successful legacy coercions and nonstandard
JSON forms are preserved; E15 does not authorize silently narrowing the reader.
Positive byte/depth/occurrence/normalized-output/token/node limits protect decoding/lookup and abort with
Inventory.ReferenceCacheReadLimitExceeded / LimitsExceeded / cache path. Invalid
policy fails before IO. This changes errors and introduces explicit resource bounds,
without repairing, writing, omitting rows or returning partial success.
Known JSON/read InvalidReferenceCache diagnostics remain unchanged; added details
identify formerly raw field/cast failures. Resource limits can precede semantic
schema/expiry checks. The normalization quota counts UTF-16 code units before
stream delivery, preventing unbounded normalized output. Separately cap lexical
token length before parsing and temporary JSON value/container allocation count.

## E16: deterministic ambiguity details for content selectors

Retain Content.AmbiguousSelector, category/selector target, wording/hint and the
current-culture-ignore-case sorted unique matching labels. Use ordinal tie order
to choose a stable representative for distinct spellings that collate equally.
This affects only an existing failure's informational list. All physical candidate
occurrences still count toward ambiguity; no candidate is removed before matching,
no successful selector changes comparison and no pack effects occur.

## E17: deterministic classification ambiguity details

Preserve Metadata.AmbiguousMod, category, selector target, wording and hint.
Matching IDs remain sorted and unique using current-culture-ignore-case comparison;
ordinal ties select a deterministic representative. All original mod occurrences
still determine ambiguity. No successful matching, local-mod acceptance, target
order or classification state changes. This is limited to informational error ties.

## E18: resource selector metadata failures become domain errors

Convert actual raw method/member failures while building editorial-only candidates
to ResourcePack.InvalidSelectorMetadata/InvalidData, metadata-key target, details and
matching-entry repair hint. Preserve candidate construction before query and the
empty-candidate DefaultId failure before Name access. Do not validate skipped entries
or change earlier full-inventory errors. This does not turn former failures into
successful selections.

## E19: deterministic editorial resource candidate ordering

Order editorial-only keys ordinally after unchanged active/inactive candidates.
This stabilizes ambiguity detail ordering and first malformed metadata-key diagnostics.
Additionally, valid imported keys can be distinct under their captured current culture
yet equivalent under invariant membership (Turkish I+soft-hyphen versus i). Select the
first ordinal key in that equivalence group, an explicit deterministic successful
representative change from legacy Hashtable iteration. This is not limited to errors.
Retain original captures and targeted expectations, and verify insertion-order reversal.
Keep incremental invariant DefaultId membership and ordinal alias matching distinct.
Retain every duplicate physical occurrence and label.

## E20: malformed category cache fields fail at their actual consumption stage

Map actual raw missing-member/cast failures to Metadata.InvalidCategoryCache,
InvalidData, absolute cache path, details and modpack category list hint. Preserve
the distinct known read-invalid diagnostic without added details, timestamp/project/
index/Id/reserved/metadata order, null-versus-empty Categories, ignored SchemaVersion
and successful legacy coercions. Reader resource bounds use
Metadata.CategoryCacheReadLimitExceeded/LimitsExceeded; invalid policy is a separate
InvalidArgument diagnostic before IO. No fallback, cache rewrite or eager Id checks.

## E21: malformed category-list editorial assignments

Wrap actual ContainsKey/member-projection failures while counting editorial Mods
entries as Metadata.InvalidCategoryAssignment/InvalidData, editorial mod-key target,
details and a repair hint for the matching Mods entry. Validate this count stage only
when at least one category is defined, before Name/Order and physical inventory IO.
Do not apply ModClassification's falsey-entry bypass, silently discard orphan entries,
or substitute physical counts. Existing metadata/definition/inventory errors retain
their stage and established diagnostics.

## E22: deterministic category-list diagnostics and cultural ties

Inspect editorial assignment keys and definition keys ordinally for a reproducible
first malformed-entry target where legacy Hashtable enumeration was unspecified.
Keep successful category-list sorting by Order, cultural Name and cultural Id;
use ordinal Id only as the final cultural tie-break. This is separate from E12's
inventory-reference category ordering. No ordinary z/ä order change, category
deduplication or change to appended unclassified rows is authorized by this rule.

## E23: category-reference cache write diagnostics

Metadata.InvalidCategoryCacheWritePolicy/InvalidArgument identifies a nonpositive
byte quota before configuration effects. Metadata.CategoryCacheWriteLimitExceeded/
LimitsExceeded identifies the absolute cache path when actual serialized UTF8 bytes
exceed quota. Metadata.CategoryCacheWriteFailed/WriteError reports IO/permission/
replacement failures, absolute path, details and a configuration-access/category-list
retry hint. Preserve previous bytes and clean only the owned temporary. When cleanup
also fails, retain the primary diagnostic and add cleanup context. These regenerable
cache errors do not imply durable project transactions or crash recovery.

## E24: source changes retain culturally equivalent distinct paths

Use OrdinalIgnoreCase path uniqueness and captured cultural ordering plus ordinal
ties. Legacy Sort-Object -Unique can discard an ordinally distinct Unicode path,
causing a real change to disappear. Emit every actual change instead of reproducing
that safety defect. Preserve missing/null equivalence, hash case-sensitive comparison,
ordinary path casing and full Path/Before/After values. This is an explicit successful
result correction required for complete external-edit detection, not an error-only
formatting change.
Also enumerate actual dictionary keys: a source key named Keys shadows PowerShell's
Hashtable .Keys property and can hide changes entirely. Preserve it as a real path,
with all other keys, instead of reproducing that dynamic-member failure.

## E25: deterministic fingerprint ties and a distinct full-source identity

Keep all paths in the legacy health fingerprint and stabilize cultural sort ties
ordinally. Old tied-order cache hashes may change; regeneration is appropriate.
Use actual key enumeration when a path named Keys shadowed the old Hashtable member;
old incomplete fingerprints in that case change and are regenerated as well.
Retain legacy root-dist filtering/culture, exact v3 payload and UTF8 fallback.
Use a separately framed culture-independent full-tree fingerprint for future bound
plans, with root identity separate. Do not substitute a filtered health identity
for transaction source protection or claim the new full identity matches old hashes.

## E26: bounded, fail-closed source reads

Source.InvalidTreeReadPolicy/InvalidArgument identifies invalid positive quotas
before IO. Source.TreeReadLimitExceeded/LimitsExceeded identifies the absolute root
with the exceeded limit/context. Source.TreeReadFailed identifies actual failed IO
with absolute offending path/details and a source-access repair hint; missing paths
use ObjectNotFound, other IO uses ReadError. No partial map or null digest is returned.
Original Transaction.LinkedPath behavior remains separate and unchanged.

## E27: source path collisions cannot erase protected files

Transaction.PathCollision/InvalidOperation rejects actual ordinal-ignore-case aliases
among retained files/directories on case-sensitive filesystems, with relative-path
target and the existing spelling in details. Legacy hash-table overwrites can lose
one path; copying into a case-insensitive stage can also merge distinct directories.
Fail before returning a usable protected snapshot. Cultural equivalence alone is
not a collision: distinct accent/ligature paths remain protected and compared by E24.

## E28: numeric version-comparison failures become contextual diagnostics

Version.InvalidNumericComparison/InvalidData replaces actual consumed numeric parse
failures, with Left/Right argument target, parser details and a declared-version/
scheme repair hint. Preserve equality shortcuts, General lazy component access and
Maven left-then-right eager parsing. Do not validate unused later General components
or silently convert former failures into unknown/healthy comparisons.

## E29: version-comparison resource policies and limits

Version.InvalidComparisonPolicy/InvalidArgument identifies nonpositive quotas or a
Maven MaximumDepth outside1..64 before comparison. Version.ComparisonLimitExceeded/
LimitsExceeded identifies Left/Right and the consumed text/numeric/item/depth quota
in details, with an explicit-quota/input-reduction hint. Whole-text resource bounds
precede equality; numeric bounds only run for consumed numbers. Guard Maven item
and depth allocation before it happens, retaining a hard recursion bound even when
other policies are increased. No partial integer or invented unknown result.

## E30: bounded typed range evaluation and declaration decoding

Version.InvalidRangePolicy/InvalidArgument identifies invalid range quotas or depth
outside1..64. Version.RangeLimitExceeded/LimitsExceeded identifies visited text,
node,depth or comparison work with the exceeded property in details. Separate
Version.InvalidRangeDeclarationPolicy and RangeDeclarationLimitExceeded identify
data-lowering quotas before allocation, including inactive input. No partial range
or result escapes. Evaluation guards preserve semantic short circuits; declaration
quotas are an explicit input-resource prelude. Repair hints identify the relevant
input/policy. Malformed or unsupported semantic expressions still return unknown.

## E31: actual numeric range-bound failures

Version.InvalidRangeBound/InvalidData contextualizes visited wildcard BigInteger or
initial caret/tilde Int32 conversion failures, using the bound as target, parser
details and a range-editing hint. Preserve Int32.MaxValue increment promotion and
do not validate unused conditions. Consumed ordering errors retain E28/E29 rather
than being silently mapped to unknown or wrapped as a different failure.

## E32: typed declaration/alias enumeration protects actual Keys entries

Fabric dependency groups enumerate their actual ordered map entries. A requirement
named Keys must not shadow the map's key collection and erase requirements or invent
Target='*'/Range=null. Preserve the captured defective original separately; explicitly
test the complete corrected dependencies. Future graph alias indexing also enumerates
actual alias entries: a string alias Keys must not become its version text as a new
alias with null version. Root alias contents themselves remain faithful. These are
scoped safety refinements, not claims of exact legacy equality for Keys fixtures.

## E33: bounded root manifest input and construction

Metadata.InvalidDeclarationPolicy identifies invalid positive quotas/hard depth
overrides before parsing. Actual selected input/token/node/output/recursion exhaustion
produces an incomplete root diagnostic identifying the quota; no healthy declaration
or silently dropped requirement is returned. Preserve any root mod already inserted
before a late failure. All data/requirement budgets guard owned allocation/descent;
increased policies do not remove hard recursion bounds. Archive byte quotas remain a
separate future effect, and no child-unavailable claim is inferred from root parsing.

## E34: contextual root failures preserve partial observations

Actual selected decoding/member/cast failures become structured ManifestIncomplete
diagnostics with manifest name, operation/field context, details and repair guidance.
Preserve warning stage/order and already inserted mods/nested intents; never turn
failure into complete/healthy. Unknown-schema and no-supported-root diagnostics keep
their existing useful messages. This improves formerly raw localized member errors
without changing which manifest branch was selected or retrying a different loader.

## E36: root archive reads have explicit physical resource boundaries

Validate archive/declaration policies before file IO, and check the existing actual
central census before BCL entry indexing. Preserve declared4MiB metadata limits and
add actual decompressed-byte, aggregate-byte and decoded-text guards. Resource
exhaustion returns Limited with incomplete declarations and exact entry/quota context;
never silently drop a mod or infer healthy metadata. Global preflight changes failure
priority for structurally limited archives explicitly. Ordinary root reads retain
legacy entry-read/decode order, first duplicates, encodings and partial mod prefixes.
Every owned handle closes on all outcomes. Child traversal remains a separate effect.

## E37: bounded nested traversal and contextual resource disposition

Replace recursive child MemoryStreams with same-handle, exclusively owned temporary
disk spools deleted on close. Validate public policies before source IO. Guard actual
child bytes and invocation copy/metadata/census/attempt/emitted budgets before writes,
entry indexing or declaration construction. A global exhaustion preserves the prefix
and stops all remaining work with one reserved TraversalLimit diagnostic and exact
entry-chain context. Zero internal remaining allowances are valid; suppressed child
NoSupportedManifest warnings consume none. A zero-cost empty census is valid at an
exact shared limit. No final RAM/performance gate is claimed from these policies.

Local child census rejection behaves like a child ZIP construction failure: stop
remaining siblings of its parent, allow ancestors to continue. Local metadata or
declaration rejection is a returned child root failure, so its parent's siblings
continue. Shared census exhaustion is distinct and stops the whole invocation.
Missing/declared-oversized entries still warn and continue. Normal depth17 checks
still follow child copy/archive construction. Copy/open/spool failures have typed
NestedReadFailed diagnostics instead of localized raw exception wrappers, preserving
the original failure stage, mod prefix and sibling disposition. No source extraction
or filesystem normalization of ZIP entry names occurs.

## E38: owned requirement contexts and bounded tri-state evaluation

Context ownership validates positive quotas and counts consumed rows, keys, version
occurrences and UTF16 text before owned insertion. Repeated empty entries cannot
bypass row accounting. Reject duplicate project IDs with Content.DuplicateIdentity;
concatenate repeated mod-version occurrences. Freeze maps and arrays, using ordinal
case-insensitive identity independently of invariant tag/version comparisons.

Evaluation validates positive visited-node/version-check policies and hard depth64,
including range/comparison policies before short circuits. Actual exhaustion fails
with Compatibility.ContextLimitExceeded or Compatibility.RequirementLimitExceeded,
identifying its quota; never convert exhaustion or consumed range errors to healthy
or unknown. Empty/null inspected mod versions still consume checks and retain
unknown semantics. Project constraints have an explicit text/null domain; non-text
shapes return unknown instead of attempting arbitrary boxed PowerShell casts.
Public provider requirements all use text wildcards, so retained non-text helper
observations do not claim changed public behavior. Nullable project-version helper
cases remain outside node construction, which assigns non-null string version IDs.
Unless/Any/All, negative kinds, runtime absences and actual failure priorities remain
the original rules. Full side filtering, graph issues/IDs, health and provider
availability are separate capabilities, not inferred from this pure evaluation.

## E39: constraint evidence keeps diagnostic identities truthful

Original JSON and issue hashes collide for numeric NaN/Infinity and their quoted
text forms even though evaluation differs. Display also merges different unsupported
maps, numeric kinds and dates. Preserve full owned wire profiles plus a type-framed
fingerprint, never use display or legacy wire hash as validator identity. Bind this
evidence to its semantic range in a closed VersionConstraint. Bound and memo its
retained wire allocation; exhaustion remains an incomplete declaration with prefix.

Future graph keys use schema2/validator4 and isolated health/validator-4 caches;
old baseline keys require recalculation, not cross-version suppression. A verified
legacy hash may assist order but must not deduplicate distinct modern constraints.
Preserve ordinary public wire values, including enum-name versus numeric profiles;
correct old identity collisions and depth truncation explicitly in the future graph
contract. This checkpoint does not implement or claim accepted graph reports/caches.

## E40: bounded pure side graph composition

Separate owned side-specific graph composition from descriptor/ZIP/provider/cache IO.
Validate positive graph policies first and charge complete input occurrences before
composition, including disabled nodes and unused alternatives. Bound aggregate work,
facts and emitted occurrences; actual exhaustion fails with GraphLimitExceeded and
an exact quota target, never a healthy/partial report. Duplicate typed node IDs fail
before building contexts. Preserve consumed evaluator failures as failures.
The graph uses actual alias entries under E32 and schema2 diagnostic identities
under E39. Input occurrence order defines duplicate-owner selection, replacing an
unspecified hashtable enumeration tie; verified shallow legacy hashes can preserve
ordinary issue ordering without becoming identity or baseline membership keys.
Native acceptance and public graph/cache wiring remain pending this contract.
