# Capability68: remote project identification by ID or slug

LookupProjectAsync is a closed provider lookup contract. It accepts the existing
canonical ASCII ID profile or the documented Modrinth slug alphabet/length,
including punctuation and Unicode word characters. It escapes the whole value
once. Empty, dot segments, controls, path/query injection and unsupported URL
inputs fail before HTTP. URL interpretation and numbered search/version choices
belong to request preparation, not this operation.

Slug contract source: https://docs.modrinth.com/api/operations/getproject/.
White-box review caught an initially too narrow ASCII-only draft before freezing.

Returned identity still passes the canonical ProviderProject decoder. The strict
GetProjectAsync profile still requires exact requested/returned identity BEFORE
cache writes. Its memo is separate from ProjectLookup, even for the same URL:
calling both contracts directly can legitimately make two requests. A permissive
lookup cannot bypass or poison strict canonical validation, in either call order.

Lookup persists the complete owned validated project response only under
project/<returned canonical ID>, retaining unknown fields, bounded atomic writer
and observation timestamp. It does not create a slug-keyed cache. This intentional
internal cache-layout refinement differs from the original's request-keyed project
store; query/version evidence contracts remain unchanged. Failure does not fall
back to stale project metadata.

ResolutionAcquisition exposes lookup through its existing serialized cancellation/
disposal lifetime. It seeds typed owned evidence by canonical identity; the first
lookup observation wins for subsequent domains and an acquired domain stays frozen.
Domain acquisition reuses that evidence, so slug->canonical candidate listing needs
two metadata GETs and no redundant canonical project GET. Listing downloads no JARs.
Lookup inputs, response models and requests share existing invocation quotas; the
compatible-domain count is not claimed to bound all aliases requested before the
canonical ID is known.

No public add/update, bound plan, installed-project write, installer/updater or
complete PowerShell independence is claimed.

Accepted product checkpoint fb819a0. Three locked restores, strict Release builds
and format verification passed; Native AOT Core93/CLI102 published without warnings.
Core93 default contracts passed, including punctuation, both memo call orders and
selected raw cache/timestamp checks. Black-box three original comparisons passed,
with exactly two requests, zero artifacts, canonical cache refinement explicit and
all 11 frozen fixture files plus three sources intact. White-box eight TCP/input
cases and an independent default run passed; one harness UTC expectation was fixed
before rerunning all eight, with no product change. Reports live in the enclosing
reorganization workspace:
assessment/review-blackbox/remote-identity-v93-final-report.md and
assessment/review-whitebox/capability-68-project-lookup.md.

Parent CLI102 passed protocol15, version, fresh Nu and complete public content-list
differential/reference checks. Neither installer nor live installation was touched.

SHA256 Core93: 53E713F655170BAE140747BE1498AA5A9FB7F5FC2F5273C159A589402B8C55ED
SHA256 CLI102: A5A959505606B9AE0E1C04F4B823688031EEA3FD174415A1D5FDC0E50A208A48
