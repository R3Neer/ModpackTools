# Capability56: offline provider version observation

Read one content-addressed metadata cache entry for exact `version/<id>`;
SHA256 replacement-UTF8 endpoint text determines metadata/<hash>.json. Explicit
configuration directory and clock; no network or writes. Timestamp from the opened
file handle must be strictly newer than now minus24hours. Expired data is not
decoded. One observed file instance, not an atomic snapshot of the whole cache.

Return immutable observation status Fresh/Missing/Expired/Empty/Unreadable with
typed Version only for Fresh. API object and singleton root-array are supported;
null/empty input or empty root-array is Empty, never a known empty candidate domain.
Directory entry behaves as Missing, as old File.Exists. Access/parse/schema errors
are Unreadable with a diagnostic. Resource exhaustion is a hard typed error,
never unavailable or healthy. Existing bounded legacy token decoder handles
syntax/date provenance internally; no generic JsonElement enters the domain.

ProviderVersion owns Id/ProjectId/Name/VersionNumber, publication text and nullable
typed DateTime/instant, immutable files(filename/url/primary/sha1/sha512/nullable Int64 size), dependencies
(projectId/versionId/type/fileName), loaders and game versions. ID must be present;
null maps to empty and scalar numeric/Boolean casts retain proven original text.
Optional helper-read fields default empty; ID lookup is case-insensitive.
Publication dates retain legacy display and DateTime kind/ticks plus instant;
unparsed publication text is not rejected eagerly by health lookup. Future resolver
date consumption must still report a failure rather than treating it as oldest.
File/dependency order is retained; no file selection or dependency filtering here.

E41: multi-object root, complex identifier fields and malformed file/dependency
collections are Unreadable rather than dynamic member enumeration or object casts.
Declared size preserves full Int64 (including zero); fractional/complex size is
Unreadable. Actual future downloaded bytes must be counted and hashed independently
of the declared size. Primary typed dates are invalid, never guessed Booleans.
Retain original characterizations outside the supported typed API domain. This
explicit malformed-metadata diagnostic refinement follows permission to improve
errors; it does not discard valid API scalar successes or turn missing into Fresh.

Positive limits validated upfront: bytes16Mi, depth64(max1024), normalized output16Mi,
token4Mi, decoded nodes100000, aggregate collection entries100000. Limits count actual
delivered bytes and all decoded fields, including discarded unknown fields; typed
collection limits span files/dependencies/loaders/game versions. No partial model
escapes. Future multi-version/project health reader must add invocation-wide quotas;
this one-entry checkpoint is not whole-project health or HTTP cache acquisition.

API shape checked against [official version specification](https://docs.modrinth.com/api/operations/getversion/).
Original cache and ConvertTo-MpNode observations remain the behavior oracle.

Inspection --inspect-provider-version-cache config-directory version-id now-ISO
with optional pairs: --max-cache-bytes, --max-cache-depth,
--max-cache-normalized-chars, --max-cache-token-chars, --max-cache-nodes,
--max-cache-collection-entries. Fully buffered response bound32MiB;
statuses return0, quota/policy domain errors1, malformed arguments2.
No source-generated serializer/reflection is needed for the explicit typed writer.

Final host78 SHA25696D068AB744D9A01C08CF5D100B5202EC9ECE99968D7C624DED2B264B5CF1234;
CLI85 SHA256FCFB3D2E46F307AD86A912250BD4F661B3F2604C6597A8C1984806FCE74D4B6B.
Three-project locked restore and corrected-source format/strict Release checks,
both Native AOT publishes without warnings, parent default/15 protocol pass.
Blind59 covers44 full observations plus15 limits; white43 plus default covers
original/API/E41/expiry/size/date/limits and released handles. The primary comparer
source finding was corrected using InvariantCultureIgnoreCase before78/85;
provisional77/84 remain unaccepted. Original47 and supplemental scalar/primary
captures were preserved. No network, user-cache write or installation occurred.
Assessment reports: review-blackbox/provider-cache-v78-final-report.md and
review-whitebox/capability-56-provider-version-cache.md under workspace assessment.
