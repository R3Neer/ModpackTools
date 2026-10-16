# Capability118: independently reviewed native doctor repair phases

Accepted CLI Native AOT156 and native CoreContracts157. Nu interactive routing is
not implemented by this capability; normal human doctor --fix still uses the
legacy fallback until119. This backend supplies the typed contracts it needs.

The request schema1 gains optional doctor_scope with exact string environment or
project. Scope requires doctor --fix without help. Project review_token requires
Project --yes without dry-run/help. All guards run before diagnostics/acquisition.
Absent scope preserves the public default All repair. Unknown/case-invalid scope
is Core.InvalidRequest. Wrong operation/token scope is Transaction.InvalidReview.

Environment uses a narrow Packwiz observation/report, never full Doctor.Read,
project discovery or selected project resolution. It can install/configure only
the verified managed Packwiz and never repairs a project. Project cannot install
or edit config.psd1; a missing tool fails explicitly. Diagnostic/cache files may
still be written under the established98 cache policy. Do not interpret semantic
configuration preservation as forbidding those independent cache files.

ConfigurationStore.Observe returns physical canonical directory identity and a
hash of the exact bounded parsed PSD1 bytes, with path guards and consistent read
handle. Project review captures it, freezes Packwiz resolution for the operation,
and revalidates it after fresh preview and inside actual preparation. No tool
resolution after those checks silently switches PATH/configured executables.

DoctorProjectReview hashes a source-generated typed facts document through a
CryptoStream without a second large encoded buffer. Contractv1 binds configuration
identity/hash, selected Packwiz observation, strict/downgrade flags, complete
physical project/BeforeFingerprint/SourceChanges expectation, ordered visible
content changes and compact health. This local observation digest is not a
signature or authentication token. Case-insensitive hexadecimal comparison is
accepted. Mismatches use ReviewChanged/ResourceBusy: the reviewed state/policy is
obsolete, so the user must review again. Removal108 keeps its existing category.

The review callback runs after preview including no-op, before deciding to apply,
and again in the actual prepare-output callback. Apply uses the fresh preview's
existing transaction expectation. A noncooperating external editor is not promised
cross-root atomicity; the documented checks detect observed configuration/source
changes. Scope separation prevents a project token from triggering installation
first. Environment publication remains an independent truthful receipt.

## Observed acceptance

Locked CLI/CoreContracts restores, scoped whitespace/full format verification,
Release build, CLI AOT156 and CoreContracts AOT157 passed with zero warnings.
Full default native contracts157 PASS in isolated temp/AppData/config/state.
Protocol15, version, full fresh-process Nu suite and minimal bridge-free doctor/
help/missing-resource checks PASS; default unscoped repair remains unchanged.

Test-NativeDoctorReview.ps1 PASS: Environment with invalid selected ID/no project
changes, wrong Environment token before invalid PSD1 parsing, nonmutating Project
preview, config-edited rejection, lowercase-token faithful application without
configuration/dependency writes, no-op token enforcement and missing-tool Project
isolation. Snapshots compare complete source file hashes/mtimes.
Test-NativeDoctorEnvironment.ps1 PASS twice: default All and --Scoped Environment
both download/verify the production pinned archive into private paths with spaces,
retain unknown config settings, verify installed executable SHA256 and repeat
idempotently, without PowerShell invocation or PATH writes in the product.

White assessment/review-whitebox/capability-118-reviewed-doctor-final.md: exact2
logical scenarios,4CLI requests total. Scope Project with invalid casing rejects
before invalid config IO. No-op preview token then --strict policy change rejects
ReviewChanged. Initial fixture was stale and produced2 legitimate changes, so its
no-op assertion failed; a local Go refresh prepared a truly healthy empty fixture
before repeating only the affected preview/confirm pair. One orchestration syntax
failure executed zero product calls. Raw attempts retained; source/config intact.

Black assessment/review-blackbox/doctor-project-review118-final-report.md:
four calls/two scenarios PASS: faithful preview/application against104 and source-edit
rejection with complete edited-tree preservation. ResourceBusy was an explicit
new contract choice, differing from the initial InvalidOperation expectation.
An overly broad config-directory assertion was narrowed to preserved config.psd1;
new native-build-health/v2 cache entries are permitted by98, not settings changes.
No additional product calls are required to correct those declared assertions.

EXE156 SHA256 BD3E08C0A2DCB3115B80CAA6A89EB71E85E0EB7122A81D102253E61FDA0A85F4.
Core157 SHA256 51E9C14008D2B58BCB7A31D8BD9119B449BB200340E1FF0409D63E2C2BBEACF4.
No installed module/profile/PATH, real project, release or push was changed.

Black final compares the complete doctor/presentation/repair shape after explicit
fixture context normalization (Active=fixture, absent installed Minecraft profile
matches the first104 fixture). Forty-eight original oracle files and ten legacy
inputs remain intact;54 evidence files exclusive-open checked, no pending locks.
All acceptance uses local branch/private fixtures; live Nu human repair activation
is still the next capability.
