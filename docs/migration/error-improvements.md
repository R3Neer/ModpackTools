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
Validated imported metadata rejects culturally equivalent duplicate keys, so no
successful imported identity changes. Keep incremental invariant DefaultId membership
and ordinal alias matching distinct. Synthetic unvalidated metadata tables are not
an alternative input grammar. Retain every duplicate physical occurrence and label.
