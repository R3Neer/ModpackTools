# Capability127: selected native release download/update composition

Product74a2ba8 composes an already selected official native release with the
existing verified downloader and installer. Release eligibility and human
confirmation are caller responsibilities, not silently bypassed public policy.
This boundary also supports a deliberately selected same-public-version repair.

Revalidate selected repository/tag/asset URL/name/version/size/digest values;
public records alone confer no authentication. Verify owned root and expected
generation before downloading. Temporary parent is physical and outside the app.
Downloader retains its bounded HTTPS-only redirect/deadline/hash behavior, with
maximum bytes further bounded by selected asset length and optional chunk progress.
Exact transferred length is checked. Installer checks manifest public version
against selected release before executing package smoke and rechecks generation
under admission before immutable publication. Ordinary local setup defaults are
unchanged when those optional expectations are absent.

Downloaded ZIP cleanup verifies parent, default stream, length and SHA256.
Successful publication remains successful with a cleanup diagnostic if evidence
must remain. A failure plus cleanup failure preserves primary Domain Id, message,
exact TextTarget, details and phase data alongside the retained ZIP target; white
source review identified and closed that evidence-loss case. Double-failure
wrapper acceptance is source-only, not a runtime injected-failure claim.

## Acceptance

- Core190 SHA256 3D2165F0B4CC463D8731BDABCCB655DBD8C466CCA7915B186464BFAA6AD9A2E2.
- CLI191 SHA256 E4B1D7C8789F059D66CCD2EBB5BAB3A40A13A7F3B940BF5AC893C130C5510B0E.
- Per-project formatting, warning-free Release and Native AOT publications pass.
- Parent Test-NativeReleaseUpdate.ps1 passes3 inspector calls plus1 Setup187
  prerequisite install using opaque125 ZIPs. A selected4.0.3 with a4.0.2 manifest
  fails VersionMismatch after one injected download with complete root preserved.
  Stale generation fails before download (zero handler calls). Coherent selected
  4.0.2 packageB applies overA, switching its build without rewriting stable bin.
  Process-private TEMP/TMP contain no leftover files after failure or success;
  AppData/config/state/PATH are private, with no User/Machine environment changes.
- Shared Test-NativeDependencyDownload.ps1 regression passes controlled transfer,
  hash rejection and pre-cancellation, without network. Its new progress callback
  defaults null for existing callers.
- White independent2/2 PASS,3 calls including install prerequisite: advertised
  length+1 fails AssetLengthMismatch; wrong syntactically valid digest fails
  SelfUpdate.DownloadFailed. Each has one simulated GET, false retain/published
  flags, all25 installation files preserved and empty private temporary directory.
- Black independent2/2 PASS,3 calls including install prerequisite: wrong selected
  version preserves complete root/control bytes and mtimes; coherent selectedB
  installs with truthful receipt/new generation/previousA and immutableA/bin.
  Both use exact opaque packages and frozen inspector, no source, SDK or network.

These are injected HTTP streaming/composition cases, not a real ModpackTools
release download/TLS test, and do not claim public self-update is connected yet.
The live official release still lacks a native asset; no release was published.
Evidence: assessment/review-whitebox/capability-127-native-release-update-final.md
and assessment/review-blackbox/native-release-update127-final-report.md.
