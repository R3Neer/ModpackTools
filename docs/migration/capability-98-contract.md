# Capability 98: strongly identified artifact freshness

The internal service reports missing/current/stale/unknown. It compares independently
validated candidate exports through97 without writing original project bytes.
Unknown/cancelled/limited work never becomes current; unknown and missing are not
cached. Cancellation remains a terminal cancelled operation.

Version2 cache identity includes held physical root identity, full content hashes
outside dist, selected baseline filename/UTC timestamp and full archive SHA256.
A hit requires the same complete identity before and after reading cached data;
a miss rechecks selection and content before and immediately before atomic cache
publication. Cache data is bounded, typed, shape-checked and separate from legacy
files under external configuration/native-build-health/v2. An unchanged strong
hit requires no Packwiz process. Time/size alone never establish freshness.

SourceTree.ReadForHealth skips the root dist directory before traversing or opening
its payload. Full SourceTree.Read and transaction protection retain their complete
scope, including dist. This implements D06 without weakening rollback identity.

Distribution documents additionally require game minecraft and formatVersion1;
summary is retained and verified against pack description. Version2 freshness also
compares summary, preventing an invalid-header or changed-description archive from
being called current. Legacy semantic diff projection remains unchanged. The
typed internal BuiltUtc consistently represents UTC and the inspector emits O
format on both first observation and cache hit; legacy cache changed its string
representation after DateTime deserialization. FromCache is inspection evidence.

Accepted product checkpoint663992d. Locked restore, zero-warning Release, format
verification and Native AOT Core120 passed. SHA256
FE9FF41EA5253EACFB95D05D9385581FEB24D07C1E6C65194B65985E4C18ABF6.
Native default contracts, real-Packwiz build regression, real freshness cases and
structured cancellation with unchanged sources passed. Parent freshness verifies
current miss/hit without tool, description-only staleness and a new selected
baseline invalidating the cache even though dist is outside the source hash.

Black-box4 scenarios/5 calls compare complete status, strong repeat hit, timestamp
representation and legacy-cache/source preservation. White-box2 controls/6 calls
prove same-length/same-mtime source edits invalidate current and health can skip
a dist junction while full source reading rejects it. Exclusive handles and
artifact hashes are verified; no blockers, HTTP or personal changes.

Source review replaced legacy cultural Health fingerprint framing with All on the
filtered snapshot. The new dist filter uses ordinal path comparison, with a test
that a Unicode culturally equivalent physical name is not excluded. Provisional
Core118 exposed a real OperationStopped-category conflation: normal tool startup
failure now means unknown, while exact cancellation and uncertain child ownership
remain terminal. Core119 was not accepted/executed; Core120 has both corrections.
The provisional failure and final reports are preserved separately outside product:
assessment/review-blackbox/build-status98-final-report.md and
assessment/review-whitebox/capability-98-freshness-final.md. Parent cancellation
harness initially checked the wrong JSON field; correcting its assertion confirmed
the already structured cancellation, and final Core120 was checked again.
