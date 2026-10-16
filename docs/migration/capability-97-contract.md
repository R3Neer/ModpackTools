# Capability 97: complete native project diff

Diff selects the latest regular MRPack by last-write UTC, excluding temporary
.modpacktools- names. Equal times use an explicit ordinal filename tie break.
The baseline is read from an owned source copy before invoking tools. One native
refresh and one export produce a complete independently validated candidate using
the shared artifact contracts95. Indexed payloads, descriptor and editorial data
must remain frozen across export. All archive reads, traversal, logs and source
transactions remain bounded. No graph health policy or provider API lookup is
introduced into this read-only command. Artifact reference verification may use
the existing verified artifact provider when the manifest omits a declared hash.

The existing transaction runs dry: diff never publishes refreshed pack/index bytes
to the original. Legacy diff can rewrite these files during export; avoiding that
side effect is an explicit safety correction. Original fingerprints and current
artifact bytes are still verified under project ownership. Uncertain process
termination retains the owned stage through the shared marker.

The core returns typed empty/one/many record collections. The public adapter
preserves the released projection: empty groups are {kind:null,path:null}, a single
change is an object and multiple changes form an array. This legacy renderer-hook
peculiarity is confined to the JSON boundary, not translated into domain logic.
Nu validates these shapes and the true total while preserving legacy unwrapping.
Public success bytes are prepared before transaction completion; cleanup diagnostics
remain bounded structured data. No personal installation or configuration changes.

Accepted product checkpoint fb37800: locked restore, zero-warning Release,
format verification and win-x64 Native AOT CLI123 passed. SHA256
917C4517085BFC36D27C790AF1DEC07FE7CB8FC20FAB86F6BCC2631EF87D785A.
Final Nu SHA256 5F400F1E82D0E7DB316FFA789ABF27402EA1F205BD4D9C989DF42E83DF8335FC.
Protocol15/version/full fresh Nu actual build/diff with empty PATH passed.
The first Nu run exposed null/default helper misuse in ghost recognition; direct
column and null checks corrected it before the final passing full Nu run.

Black-box3/3 compares complete unchanged scalar projection, a full three-group
delta through Nu and BaselineNotFound. Latest mtime and temporary exclusion are
covered; native preserves every original length/hash/mtime including pack/index.
White-box2/2 proves corrupt baseline and tool startup failure leave all sources
intact, release exclusive file handles and leave no pending journal. No HTTP or
personal changes. Reports outside product tree:
assessment/review-blackbox/public-diff97-final-report.md and
assessment/review-whitebox/capability-97-public-diff.md.
