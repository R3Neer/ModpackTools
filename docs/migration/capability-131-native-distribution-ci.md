# Capability131: native distribution pipeline preparation

Product4404974 replaces the legacy CI/install/package path with four locked
Native AOT publications, native contracts/protocol/version, install/update/
retirement/Nu installer/bootstrap and complete Nu command checks using real pinned
Packwiz. Successful CI retains the tested ZIP and SHA256SUMS. Release downloads
that exact successful run artifact and verifies identity/digest without rebuilding.
The existing release trigger remains main-only; same-repository/non-PR guards
exclude untrusted workflow artifacts. Existing releases remain no-ops.

White review found version literals that would break the next authorized release
and a tag-creation race. Actual-version assertions now derive from validated
metadata/package identity, while synthetic peers retain their intentional values.
Publication creates the tag at the tested SHA if absent, then always fetches and
peels the remote tag and requires that exact SHA before --verify-tag publication.
It never moves an existing tag. Both findings are closed in source review.

Local validation: both YAML files parse using PyYAML6.0.2; all8 extracted CI-only
PowerShell blocks parse using PS7.6.6. Native/release metadata agree on4.0.2.
Core192 default contracts, CLI196 protocol, current real setup/rollback/retirement/
self-update, version-parametrized Nu installer/packaged routing and full Nu suite
with actual Go Packwiz pass. All runtime fixtures are private. No C# changed.

The hosted workflow, remote artifact download and GitHub publication have NOT run.
This is local source/command validation, not a claim that hosted runner toolchains
or GitHub permissions have been exercised. No push, version bump, tag or release.

Evidence: assessment/native-workflows131 and
assessment/review-whitebox/capability-131-native-ci-source-review.md.
Final local package currently at assessment/native-final/
ModpackTools-Native-win-x64-4.0.2.zip, SHA256
D85A63443B3F0EC387B2DFC286CD0E1F4749E4DD8AF53A25ABEF72DFE4C2EFE5,
build0D475640FB32D7E67C46F0EC58815E59304C1F3C15D970AD08A30769A6C1F401.
