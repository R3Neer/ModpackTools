# Complete-branch integration readiness

## Scope and authority

The user clarified that migration documents were to stay out of `main` until the
merge, not permanently. The eventual integration includes the entire branch,
documentation, evaluation tooling and original checkpoint history. Do not
cherry-pick product commits or squash/remove records to achieve integration.
The current instruction authorizes preparation only: no merge or push. It does
not authorize tags, publication, configuration migration or legacy retirement.

## Preparation snapshot (2026-10-16)

- Branch: `codex/hybrid-core`, preparation started at
  `f4f85207faa9d64fe6cecefafaed54c2da838c7b` with a clean worktree.
- Primary checkout: `D:\Juegos\Minecraft\Herramientas\ModpackTools`, clean at
  `025b7f32200484070fe6ec5e5aa1a3ef49c3bfa2` on `main`.
- `main` is an ancestor of the preparation HEAD. There is currently no divergent
  main history; recheck at integration time. No merge or simulated merge was run.
- Only policy/readiness documents and branch instructions change in this
  checkpoint. Product sources and workflows are unchanged. Rebuilding the native
  binaries for this documentation-only change is not required.
- Existing native package protocol (15 cases), version contract and fresh Nu
  routing with empty PATH pass again. Nu checks cover native default routing,
  unknown-command errors and actionable missing-executable diagnostics.
- SHA256 of the accepted ZIP remains
  `D85A63443B3F0EC387B2DFC286CD0E1F4749E4DD8AF53A25ABEF72DFE4C2EFE5`;
  all 17 payload lengths/hashes match `package.json`. The frozen Native AOT
  CoreContracts v192 suite also passes again outside the sandbox, using only
  workspace-owned temporary fixtures. The initial sandbox attempts failed first
  on redirected temporary cache writes, then on local HTTP loopback; the normal
  Windows-context rerun passed. No global TEMP/TMP values were changed.
  See `assessment/native-final/` in the reorganization workspace. Detailed prior
  installer, update, rollback, complete isolated workflow and reviewer results
  remain in [the activation handoff](native-activation-handoff.md).

These are local checks against the existing accepted native artifacts, not a
new build, a migrated-profile acceptance or a GitHub Actions run.

## Gate A: migration handoff and real personal session

Wait for the migration agent's actual destination and consumer manifest. Do not
assume the previously proposed data/profile paths were adopted. Preserve pending
recovery state and plugins; do not install or migrate the legacy module again.

In a newly opened normal Windows Nushell session with its final startup context:

1. Record `$nu.config-path`, `$nu.env-path`, `$nu.data-dir`, `$nu.cache-dir` and
   the effective `MODPACKTOOLS_CONFIG_HOME`/`MODPACKTOOLS_STATE_HOME` if set.
2. Verify the import resolves to the native installation's
   `D:/Juegos/Minecraft/Herramientas/ModpackTools-App/control/Nushell/modpack.nu`;
   confirm command resolution, help and version.
3. Check `modpack config get --no-human`, then `modpack project use vp26` and
   `modpack project status --no-human`. Expected project root:
   `D:/Juegos/Minecraft/VanillaPlus-26.2`, with no dependency on `D:/Minecraft`.
   Selection is the intended session change; do not mutate the real pack.
4. Confirm the existing modal configuration, scripts and plugin registration
   remain preserved. Do not activate plugins during acceptance.
5. Repeat a complete create/read/change/build/diff workflow only on an isolated
   disposable test project, with isolated configuration/state and real Packwiz.
   Compare artifacts and unchanged-original hashes as in the recorded acceptance.
6. Audit executed consumers for old paths; historical documentation paths do not
   imply runtime dependencies. Do not remove originals or legacy installations.

Codex's sandbox can redirect AppData. A sandbox-only result cannot close this
gate: the final ordinary terminal startup context must pass.

## Gate B: local branch and later integrated-tree validation

Before requesting integration, recheck both worktree states, branch identities,
`git diff --check`, ancestry and the complete diff to `main`. Preserve unrelated
edits and all migration records. A changed main or changed runtime/profile
invalidates the corresponding earlier snapshot and requires fresh assessment.

After separate merge authorization, validate the actual resulting tree using
`.github/workflows/ci.yml` as the executable source of the release checks:

- Pinned SDK `10.0.300`; locked restore and warning-free Release Native AOT
  publications for CoreContracts, CLI, launcher and setup; no JIT substitute.
- Native CoreContracts, protocol and version; package with setup; verify the
  generated digest and package contents.
- `Test-NativeSetup.ps1`, `Test-NativeRetirement.ps1` (isolated fixtures only),
  `Test-NativeSelfUpdate.ps1`, `Test-NativeNuInstaller.ps1` and
  `Test-NativeNuOnly.nu` against those newly produced artifacts.
- `Invoke-NativeNuChecks.ps1` with real pinned Packwiz and isolated fixtures.
- Final new-session personal smoke checks from Gate A if installation/context
  changed. Do not replace the user's installed package as a side effect of tests.

Do not mark these post-merge checks passed by citing pre-merge artifacts.

## Gate C: push and publication remain separate

CI runs for pushes/PRs to `main` and manual dispatch. The release workflow follows
successful main CI and may create a tag/release if its metadata release is absent;
an existing release is skipped. Do not rely on a previously observed remote state
as a publication lock. Before any separately authorized push, review current
metadata, remote state and triggers, and ensure publication behavior matches the
user's explicit authorization. No version bump or workflow change is made here.

Hosted CI is still unexecuted for this branch. Public native-asset download is
still untested until an eligible asset exists; local update, rollback and
controlled transport tests do not establish that external publication result.
