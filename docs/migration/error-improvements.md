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
