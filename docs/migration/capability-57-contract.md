# Capability57: checked provider version acquisition

One invocation-owned ModrinthVersionClient offers checked single and batch version
acquisition. No project/candidate/download/health/public command claims. Own one
HttpClient, cancellation lifetime, SemaphoreSlim and per-exact-endpoint lazy task
map. Test handler/base URI injection is explicit; default HTTPS Modrinth v2 URI.
IDs must be nonempty ASCII base62, maximum256 characters, so endpoint generation
cannot escape provider authority. Batch drops null/empty, sorts/deduplicates using
the original culture-insensitive ordering; response order and last duplicate cache
write win are preserved. Missing returned IDs remain missing. Empty request skips HTTP.

Batch GET versions?ids=<escaped compact JSON array>. Validate whole response before
stores. Persist each returned version at metadata/hash(version/<returned-id>).json,
not a batch endpoint cache. Single GET version/<id> uses the same cache identity.
Returned identities must match a requested ID exactly; E42 rejects identity drift.
Batch stores are individually atomic, not a cross-file transaction: a later store
failure can leave earlier completed entries. Returned typed versions own their
data; internal decoded JSON exists only through validation/store and is disposed.
Original38 captures after relocation confirm actual PS7 HTTP200 malformed JSON can
be returned as a string and overwrite a valid cache. It also accepts mismatched
single identities/unrequested batch rows and can store a valid prefix before a
malformed later row. E42 deliberately rejects those outcomes before any store;
these are safety refinements, not claims of old/new byte-equivalent failure behavior.

E42: each exact endpoint is observed once per invocation, including failed tasks;
dedup avoids repeated physical requests. There is one invocation cancellation token
and a linked request deadline covering queue/body/validation/store, default60s.
No retries, verified fallback to stale cache, hidden network calls or service.
Count all input occurrences before filtering, including duplicates/null/empty.
Capture the culture comparer once when constructing the invocation client.
Default physical requests1000, concurrency4, actual decompressed bytes16Mi/request
and64Mi/invocation, input IDs10000, endpoint text1Mi/invocation, returned versions
10000/invocation. Policies positive upfront. Atomic global debit precedes append;
charge failed/cancelled request work too. Decode56 quotas remain independent.
Per-endpoint encoded text is limited32768 characters. Byte quota is checked before
append; EOF probes and concurrent in-flight reads may observe a bounded canary
beyond the quota. Statistics count every returned byte, including failed reads;
exhaustion poisons further acquisition and never refunds work. No raw payload is
retained by a completed endpoint task.

Use ResponseHeadersRead and disk spool to avoid retaining raw bodies in task results.
Validate bounded typed payload before replacing old cache. Cache56 semantics/date
origins survive bounded canonical JSON store (no byte-parity claim with PowerShell).
Set explicit injected mtime on owned temp before close/atomic move; clean only owned
temporaries, retain primary failure. Refuse real linked cache ancestors/destinations;
Cleanup failures are exposed in an immutable client Diagnostics snapshot; they
never erase the primary error or pretend a completed cache replacement rolled back.
ordinary cloud reparse files without link target remain supported. Each terminal
path releases response/content/body/file/semaphore. Async disposal cancels and waits
for started tasks before disposing owned client/gates. Handler ownership belongs to
this client when supplied; caller must not share that handler with another client.

HTTP error: Versions.RequestFailed/ConnectionError. Deadline:
Versions.RequestTimeout/OperationTimeout. Invocation cancellation:
Core.OperationCancelled/OperationStopped. Malformed/identity response:
Metadata.InvalidProviderResponse/InvalidData. Cache store failure:
Metadata.ProviderCacheWriteFailed/WriteError. Resource exhaustion:
Metadata.ProviderRequestLimit/LimitsExceeded. No malformed response replaces a good
cache, and no acquisition error becomes a verified/healthy empty model.

Acceptance: local fake handler and actual loopback HTTP, success/cache56 roundtrip,
dates/scalars/batch duplicate/missing ID, zero request, exact endpoint dedup, queue/
body timeout and cancellation, actual/aggregate bytes and quotas, bounded concurrency,
malformed/identity payload and locked store preserving old bytes, owned temp cleanup,
mtime before replacement and client/handle release. No external provider calls,
real cache/project writes, installation or source relocation.

[Official single version](https://docs.modrinth.com/api/operations/getversion/)
and [batch](https://docs.modrinth.com/api/operations/getversions/) specify the
supported API shapes; original helpers remain comparison oracles.

Workspace paths verified after user-reported relocation: primary repository
D:/Juegos/Minecraft/Herramientas/ModpackTools (main025b7f3), linked worktree remains
C:/Users/Usuario/Desktop/ReorganizacionPC/work/ModpackTools. Legacy installation
D:/Desarrollo/Toolchains/PowerShell/Modules/ModpackTools; actual Nu import points
there. Do not replay relocation or overwrite the other agent's live changes.

Inspector --inspect-provider-acquisition config-directory case.json optionally
--base-uri http://127.0.0.1:<port>/v2/ uses actual loopback HTTP; otherwise injected
handler with Responses object keyed by exact endpoint, Body string and optional
Status/DelayMilliseconds. Case Now ISO, Operations array of {Id} or {Ids}, optional
Parallel/CancelAfterMilliseconds/Limits (request policy property names). Results
in input order contain Allowed plus typed Versions or domain diagnostic fields.
Requests/Bytes count actual transport and observed application bytes. PeakHeaders
counts only in-memory handler header phases, not whole body/store concurrency.
One complete bounded32Mi JSON result, exit0 even when individual operations fail;
top-level domain1/transport2. Each store can be checked with offline56 inspector.
Nested Limits.Decode supports the six56 policy properties. Invalid policy remains
Metadata.InvalidProviderCachePolicy at construction; consumed decoder limits are
Metadata.ProviderRequestLimit with target Decode. Input IDs budget is aggregate
across calls, including single IDs and null/empty/duplicate batch entries.

## Accepted checkpoint

Product commit d0795a2 implements this capability. Three locked project restores,
format verification and strict Release builds passed; both win-x64 Native AOT
publishes completed without trimming/AOT warnings. Default published contracts,
15 protocol checks and 38 current-project/environment checks passed.

Frozen CoreContracts v79 SHA256:
3EEA4D35C7E3CE92FEF03F8FFFDC45C8C2E78D8273ECE89AAED701D578EE0961.
Frozen CLI v86 SHA256:
E632F2072F836A6FC7AF32A67E67008C9823E1E187C6D1323BA5B2BA40D11F4C.

Independent black-box acceptance passed 47 acquisition/transport controls and
22 offline-cache roundtrips (69 native calls), with empty stderr and verified
fixture/source integrity. White-box acceptance passed 60 focused controls plus
default native contracts, including real gzip and no-redirect HTTP behavior.
Reports remain outside shipped product files in assessment/review-blackbox/
provider-acquisition-v79-final-report.md and assessment/review-whitebox/
capability-57-provider-acquisition.md. Original behavioral captures and provisional
test failures remain preserved. E42 is intentional: malformed or unrelated
provider data cannot replace a good cache. No live installation or external
provider operation was performed. This checkpoint adds internal acquisition;
it does not yet activate additional public CLI commands.
