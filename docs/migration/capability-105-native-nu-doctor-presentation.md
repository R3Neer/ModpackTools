# Capability105: packaged native Nu doctor presentation

Accepted. Native human doctor diagnostics use R3CLI's actual
Nu adapter from revision18df65b7eff51bc0c893ec687161654ea378ee7f, package0.5.2.
The build helper exports only immutable Git objects and executes the upstream
build_nushell.py workflow in an owned temporary source tree. Python is build-time
only; Nu runtime has no Python/PowerShell/R3CLI repository dependency. This is a
build helper, not a new Windows administration/migration utility; Python directly
reuses the official Python build and avoids duplicating its resource generation.

The shipped package contains module/resources/license/upstream hashes plus a
schema1 revision/version/hash manifest. Repeated generation was byte-identical.
The parent adapter validates that package before launching presentation. Machine
invocations do not load R3CLI code or themes; malformed/missing presentation
resources affect human rendering and the diagnostic row, not machine transport.

Presentation is loaded lazily by a separate clean Nu child, preserving Nu's
static import rules without importing optional presentation into machine mode.
It uses the existing Nu executable, supplies a data-only request file and passes
literal fields to R3CLI. Human output is captured/forwarded only to stderr;
stdout remains empty. No performance gain is claimed for the extra Nu process;
later end-to-end samples must include its cost.

Only human doctor without --fix/help is switched in this checkpoint. Other human
commands, confirmation and help still require subsequent work. This is not a
claim that the live Nu setup or whole installed tool is PowerShell-independent.
Themes retain product client/host/local roles, support colours and legacy colors;
R3CLI owns palette/status/wrapping/ASCII/NO_COLOR semantics. Native machine doctor
continues to return structured values without renderer data.

Parent isolated package contains no PowerShell bridge: ASCII/color-never human
doctor produces only stderr; explicit always overrides NO_COLOR; machine doctor
survives deleted resources and reports R3CLI unavailable; human doctor then fails
with a package diagnostic rather than invoking legacy fallback. Complete Nu
machine suite passes with the new adapter. Initial local probes caught wrong Nu
import/match/interpolation forms and deprecated case aliases; fixed before freeze.

Freeze138 reuses CLI137 (no C# or dependency-lock changes); new Nu files/packages
are the product delta. No new SDK publish is claimed for this Nu-only checkpoint.
No live activation, installation, plugin registration, push or release.

Black exactly2 calls against frozen138 passed complete semantic human comparison
using existing99 oracles, with empty stdout/no ANSI and original trees unchanged.
White exactly2 complementary controls against139 passed (3 fresh Nu processes):
invalid theme leaves machine usable and rejects human rendering; modified module
with original manifest is rejected before import. Its initial two-process harness
attempt incorrectly compared PATH without establishing Nu's list baseline;
corrected isolated driver explicitly used PATH=[] and passed. No product PATH
mutation was demonstrated by that first assertion.

Review added a 256KiB theme-read quota and output/ancestor/entry reparse rejection
in the build helper. These are source-reviewed; an actual filesystem race is not
claimed prevented by this build-only path check.139 differs from138 only in the
renderer theme quota, plus build helper checks; black's existing valid results
remain attributed to138, parent4 cases and white controls to139. Rebuild139's
package remains byte-identical to the checked upstream artifacts.

139 renderer SHA256098D31753590AEF636667E838BF5285F9FB352EF77FD20FF5204A9F418B019D2;
main D5FB752D1D2F0A352BA8CBCECBAA777C5FD177E638D036596CD342C0C4C1A7B9;
loader3E1C3FAA54BDE982F3B58AF3E88504C487B60E3E64929A76ACA9563B7D239007.
External reports: assessment/review-blackbox/doctor-human105-final-report.md and
assessment/review-whitebox/capability-105-nu-human-doctor-final.md.

Git attributes disable line-ending conversion for the exact hashed native vendor
package. Staged Git blob bytes are checked against working package files; a fresh
checkout must not silently turn the upstream LF module/resources into CRLF.

Correction61a62ca: the initial product checkpoint44520d2 accidentally proceeded
after git diff --cached --check reported the raw CRLF manifest as whitespace.
That command was not guarded correctly; it is not a passing diff-check claim.
The follow-up forces LF in the manifest generator, regenerates its unchanged JSON
fields, reruns the four bridge-free parent cases and verifies exact staged bytes
and a passing guarded diff check before commit. Upstream four file hashes and all
Nu source hashes are unchanged by this build fix; no reviewer rerun is claimed.
