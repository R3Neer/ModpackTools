# Native 5.0.0 release preparation

The user accepted technical/manual migration tests on D and authorized the full
merge, then 5.0.0 with the title and notes in `docs/releases/5.0.0.md`. Push and
publication are authorized only after correcting long paths and passing tests;
publication must follow successful hosted CI on the exact integrated commit.

## Long paths

Checkpoint `7efbb36` supplies explicit Unicode application names to CreateProcess,
isolated standard-handle inheritance, suspended creation before managed handle
admission, bounded cleanup and the original Job ownership protocol. The Nu bridge
uses an extended executable name at its external-command boundary. Parent cwd,
user arguments, Windows policy and global environment are not changed.

The user explicitly chose existing 8.3 names for long native working directories,
with the original and alternate directory identities checked and pinned. Where
no usable name exists, `Build.WorkingDirectoryTooLong` identifies the affected
directory and requests a shorter native state location. No aliases, junctions or
registry changes are created. This is not universal long-cwd support on every volume.

Independent source review and black-box acceptance used frozen v3 native 4.0.2
binaries before the version-only checkpoint: 11 public operations, two package
preparations and two I/O probes, with 23 final checks. Long launcher, Nu, Packwiz,
project and core paths passed; an unshortenable 534-character cwd failed clearly
without creating the relative marker. Real build produced a verified 3917-byte
MRPack and diff zero. Source/installed originals and frozen hashes stayed intact.
Reports remain under `assessment/review-whitebox/long-path-process-source-review.md`
and `assessment/review-blackbox/native-longpaths-v3-final-report.md` in the
reorganization workspace. Source attribution and earlier failed probes are retained.

## Local 5.0.0 acceptance

Checkpoint `df8eca0` aligns product version, metadata and help, adds approved public
notes, and removes fixed release-version assumptions from relevant acceptance tests.
With pinned SDK, locked cached packages and existing MSVC/SDK, clean Release Native
AOT builds of contracts, CLI, launcher and setup pass without compiler warnings.
Formatting, modified harness syntax and diff checks pass. Online NuGet audit is
not claimed by this offline restore.

Passed against 5.0.0: default native contracts; process ownership/cancellation;
long exe/cwd, no-short-name rejection, quoting, Unicode environment and concurrent
EOF; 15 protocol cases and version; install/update/no-op/rollback; retirement and
resumption; Nu installer, empty-PATH native routing and packaged Nu at >330 characters;
complete real-Packwiz Nu commands; human reads; optional historical PS adapter;
launcher admission and selected-release download/install composition with injected HTTP.

Retirement was rerun after the concurrent Nu command suite finished: its initial
deferral correctly protected the other suite's live sessions. No foreign session
was closed. Local evidence is in `assessment/native-v5-final/`.

Local accepted ZIP SHA256:
`8B8C46569044472B42204D01068CC3856E1AF254B4BFF2AF1F89170A08D092E1`.
This identifies local evidence only. Publication must use the exact ZIP and
SHA256SUMS retained by successful hosted CI, which may have different build hashes.

## Remaining publication gates

Integrate the full branch into main and verify clean status/tree equality. Push
normally, without force or local tag creation. Wait for CI success on that exact
SHA; then let the release workflow verify/create its tag and publish its retained
artifact. Confirm the public tag SHA, title/notes, two assets, checksums and native
version from a downloaded package. No replacement of the personal installed image
or legacy removal is included in this release authorization.
