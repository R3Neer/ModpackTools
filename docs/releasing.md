# Releasing ModpackTools

Stable releases are published only after CI succeeds on main. Preparation on a
branch does not publish anything. A source checkout and a native runtime package
are different artifacts.

## Inputs

`ProductVersion.Current` is the native product version. It must match ModuleVersion
in `ModpackTools.psd1`, which the existing release metadata validator reads.
`docs/releases/X.Y.Z.md` supplies the title and notes. Update both version values
and the notes together when an actual new release is authorized.

## Tested artifact

CI uses the pinned SDK and locked dependencies to publish the CLI, Launcher,
Setup and native contract runner as Windows x64 Native AOT. It runs native
contracts, protocol, installation/update/retirement, Nu-only/bootstrap and full
Nu command checks with pinned real Packwiz. PowerShell is a CI test driver;
it is not included or invoked by the product runtime.

The native Setup `pack` command creates exactly the approved payload and canonical
content manifest. CI checks the returned archive digest and stores the ZIP plus
SHA256SUMS as artifact `native-win-x64-<tested-commit>` after all checks succeed.
The release workflow downloads that exact successful run's artifact, validates
its checksum and expected name, and publishes it without rebuilding.

Native asset name: `ModpackTools-Native-win-x64-X.Y.Z.zip`.
Tags: `vX.Y.Z`. A legacy-only module asset is not a native update candidate.
The native updater verifies official release metadata, digest, size, manifest,
version and executable/integration smoke checks before selecting a new version.

## Publication

Merge only reviewed product changes with an explicitly authorized version and
notes update. After successful main CI, the release workflow checks existing tags
and releases. An existing release is a no-op; an existing tag is verified rather
than moved. It publishes the tested ZIP and SHA256SUMS together.

Never reinterpret a source ZIP or legacy PowerShell module ZIP as a native package.
The legacy scripts/build-release-package.ps1 remains a historical baseline helper;
the native publication path does not call it or either PowerShell installer.
