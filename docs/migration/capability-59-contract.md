# Capability59: verified artifacts for native health

Next complete operation after58: obtain a verified artifact from a supplied local
file, artifact cache or explicit checked download, then read loader declarations
from the verified handle. No resolver decisions, pack mutations or public command
activation in this operation. This is an ownership boundary, not a translation of
Get-MpArtifact returning an unchecked path for a later reopen.

Preserve original precedence: a matching local file, then matching cached file,
then download only when explicitly checked with supported HTTP(S) URL and a
declared digest. Missing hash or unsupported algorithm means unavailable, never
verified. Original algorithms sha1/sha256/sha512/md5 remain accepted for compatibility;
digest syntax and length must be validated. Modrinth normally supplies sha1/sha512.
Artifact cache identity remains SHA256 text of algorithm:declaredHash. Case rules
must be captured explicitly rather than silently changing cache identities.

Native result distinguishes verified, unavailable and failure diagnostics. Owned
read-only handle is held from hash verification through archive reading; concurrent
writers/replacement must be excluded. A path alone is insufficient proof. Adapt
existing archive traversal to consume the owned handle without taking ownership
from its caller; existing path API remains supported and independently bounded.
No whole-JAR memory buffering. Preserve nested archive traversal resource quotas
and truthful incomplete/limited metadata instead of declaring unreadable JAR healthy.

Checked download uses invocation-owned bounded HTTP lifetime, explicit timeout and
cancel, observed byte quotas, streamed digest verification and an owned same-dir
temporary. Hash mismatch preserves any old cache. Replacement is individually
atomic; reopen after replacement must verify the handle actually returned. Only
owned temporaries are cleaned. Cleanup failure cannot erase a primary failure.
Links in writable cache ancestry are rejected; caller-local file reads are a
separate read-only boundary. No writes to caller-local project artifacts.

Acceptance uses isolated synthetic JARs, spaces, local/cache precedence, missing
and mismatching digests, supported algorithm/casing cases, malformed/limited
archives, same-handle ownership and injected lock/HTTP/truncation/cancel/timeout
failures. Verify old bytes and owned temporary cleanup. Two incremental independent
reviews plus corrected-source strict builds and actual Native AOT execution are
required before claiming implementation. No external downloads or real packs.

Original-only finite comparison:22cases (20returns,2errors), captured in
assessment/review-blackbox/artifact-baseline-report.md; source/local bytes intact.

Selected policy:512MiB per actual artifact/hash pass,4GiB invocation work counting
network and verification reads separately,1000 HTTP requests,10000 operations,
concurrency4(hard64),60s including queue/hash/transport/metadata. Archive traversal
is synchronous and checks cancellation before/after; the deadline is cooperative,
not a guarantee that metadata parsing stops at exactly60s. Archive traversal
has its separate established metadata/central/nested quotas. Final verification
read work is reserved before cache publication and debited atomically as read;
insufficient total budget preserves old cache. Counts include failing canary bytes.

Original22 observations confirm raw algorithm:hash cache identity and insensitive
digest verification; no trimming/case rewrite of the cache key. E44 declares
malformed digest Content.InvalidHash/InvalidData before IO; missing/unsupported
digest still unavailable. Raw HTTP errors become Content.ArtifactRequestFailed/
ConnectionError, timeout Content.ArtifactTimeout/OperationTimeout, cancellation
Core.OperationCancelled/OperationStopped. Content.HashMismatch retains InvalidData,
null target and URL details. Credentials/fragments are rejected; redirect responses
are not automatically followed, avoiding uncounted transport requests. IO failure
is distinct from unavailable and malformed archive still reports traversal status.

The borrowed FileStream overload leaves caller ownership intact and requires a
readable seekable handle, no concurrent use, with offset reset to0. It does not
itself assert a digest; ArtifactProvider enforces verification and sharing. Result
contains immutable declarations and provenance, with no live handle escaping.
Reopen/verification failure after successful publication must report failure
truthfully; it cannot claim cache rollback or overwrite a concurrent external edit.

Concurrent operations for the same raw cache identity serialize before taking the
global concurrency slot, so a completed first download is reverified/reused by the
second instead of racing its cache replacement. Completed tasks are removed from
inflight disposal tracking; a separate aggregate operation counter bounds input.
Traversal policy validation precedes any IO/store. Writable cache links are checked
before cached reads as well as publication. All non-transient models are owned.

Diagnostic inspector --inspect-artifact <config> <case-json> reads at most1MiB JSON,
with Url/Hash/Algorithm strings, optional LocalPath/Check/Loader(defaultfabric),
CancelAfterMilliseconds and Limits named exactly as ArtifactProviderLimits.
Transport is actual HTTP; acceptance uses loopback only. Success exit0 emits
Available,Source(Local/Cache/Download ornull),Path,Algorithm,Hash,TraversalStatus,
Mods(Id,Version,Side,Nested,EntryChain),Diagnostics(Code,Message) and
Statistics(Requests,NetworkBytes,VerificationBytes). This diagnostic projection is
not the complete public inventory/graph schema. Domain exit1, transport/input2;
output is fully buffered with existing32MiB inspection bound. No public CLI claim.

NetworkBytes means observed application body bytes after HTTP decompression,
excluding headers/wire compression. VerificationBytes means local/cache/final
reopen hash-pass bytes, including mismatching files; it excludes archive parsing
and nested spool IO, which retain their own archive budgets. The4GiB quota applies
to body plus hash passes, not a claimed total physical filesystem IO measurement.

## Accepted checkpoint

Three locked project restores, format verification and strict Release builds
passed, including corrected inspector type-error handling. Both Native AOT
publishes completed without AOT/trimming warnings; default published contracts
passed including four digests, borrowed-handle ownership, reservation failure,
hash mismatch preservation and concurrent matching-identity download reuse.

Frozen CoreContracts82 SHA256:
A8ACC7774A4B0C6E920A561F9B6DF0979AA6543253DE27E4C82B640B88B1112F.
Frozen CLI89 SHA256:
4A27D22C2D54250197D9C368BAF2739DF3911431CC000D69BD9CD741DE11B54D.
Black-box accepted36/36 (26local/cache/policy/bounds,10actualTCP); stderr empty,
no owned temporary leftovers, raw identity10/10, original42-file manifest and
Graph source unchanged. White-box accepted19 incremental controls plus published
default contracts and exact hashes. Reports: assessment/review-blackbox/
artifact-v82-final-report.md and assessment/review-whitebox/
capability-59-verified-artifacts.md. Provisional reviewer harness/expectation
failures remain preserved and distinguished from product defects. No remaining
product blocker; no public health command, install or external download claimed.
