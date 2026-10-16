# Capability126: bounded official native release selection

Product6fa06e8 adds the release-channel reader, not the public self-update command.
It reads only the fixed official GitHub latest-release endpoint, with a10s linked
deadline,512KiB body quota,64KiB header quota, no automatic redirects/decompression
and the explicit supported API-version header. Metadata requires canonical stable
vMAJOR.MINOR.PATCH, draft/prerelease false, exact official release URL and a bounded
asset array. Duplicate object fields are rejected; unrelated normal GitHub metadata
fields remain extensible.

The selected asset must uniquely match ModpackTools-Native-win-x64-VERSION.zip,
its exact official tag/download URL, uploaded state, positive <=256MiB size and
sha256:64hex digest. A stable release with only a PowerShell ZIP returns a typed
native-unavailable observation; it never chooses that ZIP or falls back to a
PowerShell installer. GitHub's asset digest and release fields are documented at
https://docs.github.com/en/rest/releases/releases . No public release was created.

## Validation

- Core188 SHA256 94853BBF8BA29AC5B2C3C050D864BE2D359D0E529DA5A2E43577AF5EFB748EED.
- CLI189 SHA256 9E79A4448A5783DE2717767138EB95D0C893874C31C225B5183298D79BA38030.
- Per-project formatting, warning-free Release build and Native AOT publications
  pass. Existing dependency locks are unchanged.
- Parent Test-NativeReleaseChannel.ps1:7 offline native calls cover valid native
  projection, legacy-only release, foreign asset URL, duplicate root field,
  declared oversized metadata,3xx refusal and pre-cancellation. One additional
  actual HTTPS GET passes: official latest4.0.2 has no native asset. No archive
  download, installation or live configuration mutation occurred.
- Black independent2/2 PASS, exactly2 offline calls: legacy4.0.2 returns native
  unavailable with null asset URL/hash/length; foreign native URL returns typed
  SelfUpdate.InvalidAsset. Input metadata and inspector hashes preserved.
- White independent2/2 PASS, exactly2 offline calls: nonhex SHA256 and307 response
  are rejected. Source verifies AllowAutoRedirect=false; injected3xx is not a
  real external redirect/TLS test and exposes no request-follow counter. No
  transport-follow or real archive-install claim is made. Both reviewers use
  private namespaces and no source edits, SDK, network or retries.

Evidence: assessment/review-blackbox/native-release126-final-report.md and
assessment/review-whitebox/capability-126-native-release-final.md.
The reader is infrastructure acceptance. Public command/adapters, actual native
release download/install composition, final fallback removal and end-to-end
workflow acceptance remain required before declaring the migration complete.
