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
