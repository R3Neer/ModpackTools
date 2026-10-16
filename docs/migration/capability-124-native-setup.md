# Capability124: explicit native package installation and local update

Producta88b6ba composes package verification, immutable version publication, real
Nu/R3CLI smoke, stable entry points and journalled control publication. Its pack,
install, update and rollback commands execute without PowerShell, Python or an
installed .NET runtime. Existing Nu0.116.2 or newer is selected explicitly.
Application roots are explicit; configuration, recovery state, prepared packages
and application trees remain separate. No profiles, PATH or plugins are changed.

The native package builder writes the exact17-file payload plus canonical manifest,
with fixed ZIP timestamps and trusted outer SHA256 returned to the caller.
Repeated identical inputs produce identical ZIP bytes. Native role protocols,
core public version, actual R3CLI help and the generated public Nu bootstrap are
checked before activation. Invalid outer SHA/role identity fails before creating
the application root. Prepared trees are cleaned only when ownership/content
remain verified; uncertain trees carry exact retained paths and phase flags.

Stable launcher/setup entries delegate to their pinned versioned executable.
Updates therefore deliver helper fixes without replacing open stable binaries.
Whole-child-lifetime pins survive delegation. Update receipts distinguish root
creation, version publication, stable bootstrap readiness and actual control switch.
Same-build update is a no-op; rollback selects a verified retained version. Public
version remains4.0.2; internal build IDs distinguish these local fixture packages.

Setup connects Ctrl+C to an invocation-owned token; a delegating parent keeps
waiting and retains its pin until the child exits. Runtime Ctrl+C delivery itself
has not been tested by these redirected-process acceptance cases.

## Validation and frozen artifacts

- CoreContracts177 SHA256 B3E151659EA079545859477FD46614272CFA56391DFF3945ADAC37BFFF1F5C1B.
- Launcher178 SHA256 F4BDE9366B7289B35D86B46F1E02D9D448659DC5130F62C0F27C049B74A3B4C0.
- Setup181 SHA256 FC85F2F6E90B5F4FDAE42314A6AFBCD2A890C894179E25F32168F2D1A50C808D.
- CLI180 SHA256 87EE5C3658B80F2F741B936B85A7BB0F6C9EEEAC313620A0E939A1CD15CF68D9.
- Warning-free Release build and Native AOT publications pass. Setup locked
  dependencies use the existing pinned SDK/packs. Final formatting is checked
  independently for Core, Setup, Launcher and the touched inspector source.
- Parent Test-NativeSetup.ps1 passes:9 setup calls (2 initial deterministic packs,
  bad SHA, wrong roles, install, changed-content pack, update, no-op, rollback),
  1 inspector fixture prerequisite and1 installed launcher machine-version call.
  PATH is empty, AppData/configuration/state are private, root includes spaces,
  apostrophe and hash. Old version and stable-bin bytes remain identical after
  update/rollback. Real packaged help/resources and generated bootstrap load.
- Test-NativeLauncher regression passes on177/178/180, including occupied-root
  preservation, altered-package rejection and child-lifetime admission pin.
  CLI180 protocol15 cases and version contract pass.
- Initial restricted pack failed with Access denied in the Windows identity
  context; rerun in the normal authorized test context. Setup179 then exposed a
  smoke assertion expecting the wrong help heading. Actual content help uses
  CONTENT/USAGE, not ModpackTools. Setup181 checks those concrete help markers,
  command syntax and absence of ANSI; the complete acceptance then passes.

Opaque reviewer packages: assessment/native-setup124/first.zip SHA256
9E2406E280FC5F0DCCB497E277EEA2A1A90B342BBFE25024FE009E308482D447,
build6716219318CB9B75417842B99A12A86A063C379274F97D0593F05C1EF505E2F2;
second.zip SHA256 DFD99F95F14B6A1E28711A88F45E303D9184D19B07012F06AEC33687B69B11AD,
build9CC7486DF8107D9EF8D1F44877EF2A1805413545BDE1120B4BBD7536B7102957.

Independent black runtime acceptance passes2 scenarios/4 calls: install,
installed machine version, update and rollback. BuildA/B/A has three generations;
stable bin bytes/mtimes and versionA remain intact. Independent white acceptance
uses2 scenarios/3 setup calls plus1 inspector fixture prerequisite: invalid own
control is rejected with all24 installation files preserved and false mutation
flags. Its false-role fixture uses CoreContracts177 as the payload executable,
which runs its default contracts instead of a role protocol; it times out and
truthfully returns PreparationRetained with exact paths and retain_stage=true.
This differs from the parent's CLI180 false-role fast rejection and normal
cleanup; do not describe the white variant as ProtocolMismatch.
Evidence: assessment/review-blackbox/native-setup124-final-report.md and
assessment/review-whitebox/capability-124-native-setup-final.md. White's owned
fixture namespace was removed at the end; no retained external path was recorded.
Formatting initially caught whitespace in two Core files; only whitespace was
normalized after freezing181, and final per-project verification passes.
White source review has no remaining blockers. Do not interpret this capability
as completed remote self-update, version retirement/GC, removal of the Nu legacy
fallback, or live activation. Those remain separate required capabilities.
