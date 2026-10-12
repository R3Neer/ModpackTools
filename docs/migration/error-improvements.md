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

## E03: empty project catalog is a valid result

An existing configured root with zero immediate registered projects returns a
successful catalog with `items: []`. The legacy renderer rejects an empty collection
and leaks a PowerShell parameter-binding error, despite valid read-only input.
Separating domain data from presentation removes that erroneous failure. Nested
projects are still excluded, so a nested-only root also returns the empty catalog.
Black-box comparison records this exception explicitly; it must not normalize it
away as a supposedly identical result. Root/configuration errors remain errors.
