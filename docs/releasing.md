# Releasing ModpackTools

Stable releases are published by GitHub Actions after the `CI` workflow succeeds on `main`.

## Release inputs

A release is defined by two repository files:

- `ModpackTools.psd1` supplies the stable semantic version through `ModuleVersion`.
- `docs/releases/<version>.md` supplies the GitHub release title and body. Its first line must be an H1 containing the version, for example `# ModpackTools 3.4.0 — Global search and clearer CLI feedback`.

The release workflow treats these files as the release source of truth. Do not create a manual stable release with a version that differs from the manifest.

## Normal release flow

1. Implement and validate the release changes on a branch.
2. Update user-facing documentation so it describes the resulting behaviour rather than the previous release.
3. Bump `ModuleVersion` and add `docs/releases/<version>.md`.
4. Merge the reviewed branch into `main`.
5. Let `CI` complete on the merged `main` commit.
6. After successful CI, `.github/workflows/release.yml` builds and verifies `ModpackTools-<version>.zip`, then creates the `v<version>` tag and GitHub release from that exact tested commit.

No stable release is published from a failed CI run.

## Release archive

The installable ZIP contains the same runtime distribution expected by the installer and self-updater:

- `docs/`, `Private/`, `Public/`, and `Nushell/`;
- both installer entry points;
- the module manifest and root module;
- README, licence, theme, and pinned dependency metadata.

The workflow expands the generated archive again, validates the packaged module manifest, checks the version, and requires exactly one `Install-ModpackTools.ps1` before publishing it.

## Existing tags and releases

If the release already exists, the workflow exits without publishing a duplicate. If the version tag exists but its release does not, the workflow reuses the verified tag instead of moving it.

A later successful `main` CI run with an unchanged released `ModuleVersion` is therefore a no-op for publishing.

## Self-update contract

Stable release tags use `vX.Y.Z` and the installable asset must be named `ModpackTools-X.Y.Z.zip`. `Private/SelfUpdate.ps1` validates both conventions, the GitHub release location, the asset SHA-256 reported by GitHub, the packaged module identity, and the packaged version before replacing an installation.
