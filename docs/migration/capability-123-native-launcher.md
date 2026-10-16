# Capability123: identity-bound native launcher

Product5612172 adds the Native AOT launcher, external cli.nu entry, fresh explicit
installation-root owner/layout and root admission/version pin. This is launch
functionality tested in owned fixture installations, not yet an installer.

Root creation refuses every existing directory, including an existing managed
root. It writes a strict identity-bound installation.json and fixed layout;
initialization failures retain their exact directory. Root admission uses the
physical global lease. PinCurrent verifies the complete active18-file package,
opens its shared empty pin before releasing admission, and retains that pin for
the complete child lifetime. A disposed admission cannot create another pin.
Owner/configuration paths are valid Unicode; existing Nushell is explicit.

Stable bin/modpack.exe resolves one current generation and executes absolute
paths with an argument vector. Machine --no-human internally supplies --json
before the delimiter when absent, preserving the core envelope. Human execution
uses the existing native Nushell with --no-config-file and that version's cli.nu,
which fixes the same version's core path. Child streams/exit are inherited. The
launcher protocol probe returns1. No PowerShell/.NET SDK/Python is invoked.
Session environment changes still belong to importing the Nu module in Nu;
an external executable cannot change its parent's environment.

## Acceptance

- Final Core174 SHA256 BCF41E12598582A75BBBF97BDF42859FF7025D850660D36C40184A8FE00648F2.
- Final Launcher175 SHA256 BEE3EFCB825C5118FDD04340ED886A2099854572EA550F4AD1E777ADE85C4AF6.
- Final CLI176 SHA256 F3461316B6DE39D3CAE21139AFD1ED886819D3A6665D45199087AB1495107CA8.
- Locked restores, formatting/full verification, warning-free Release builds and
  win-x64 Native AOT publications pass. New launcher lock uses the same10.0.8
  compiler/linker packs; no new application package dependency or installation.
- Parent Test-NativeLauncher.ps1 on174/175/176:5 inspector calls (2 fixture
  builders,2 root preparations with live/disposed admission checks, occupied
  root rejection) and5 launcher calls (protocol, machine version, human version,
  altered-bundle rejection and waiting Nu child). Private configuration/state,
  existing absolute Nu, PATH empty. All pass. Version is4.0.2, no ANSI; pin
  exclusive access fails while the owned child is ready and succeeds after exit.
- Final CLI176 protocol15/version pass; cli.nu syntax check passes in Nu0.116.2.
- Black independent170/173:2 scenarios/3 launcher calls plus1 setup prerequisite.
  Exact machine envelope, human version and altered cli.nu rejection preserve
  bundle/control/config. The first launch creates the persistent zero-byte pin
  file; the handle is released, the file intentionally remains for reuse.
- White independent170/173:root occupied preservation and child-lifetime pin
  sharing failure/success pass. Actual total4 inspector calls+3 launcher/Nu calls
  includes two focused isolation attempts with Nu config_dir_not_found warnings;
  their lifetime checks worked. The final focused attempt uses a complete normal
  USERPROFILE context with private AppData, and stderr is empty. All attempts are
  retained. Final disposed-admission guard is source-reviewed and parent174/175
  runtime-covered, not independently re-executed.

Evidence: assessment/review-blackbox/native-launcher123-final-report.md and
assessment/review-whitebox/capability-123-native-launcher-final.md. Opaque real/wait
ZIP fixtures are under assessment/native-launcher123. Three versioned EXE roles
are CLI172 copies and the setup role is not functional. Real fixture uses the
actual product Nu/R3/CLI entry; wait fixture uses a minimal controlled ready/
release Nu script. Stable bin launcher is the actual separately hashed artifact.
This proves launch/admission, not installer/update smoke or distribution licenses.

## Corrections and limits

Restricted restore initially could not query NuGet vulnerability data; restored
and audited in the normal authorized Windows context, without disabling audit.
First cli.nu import named a nonexistent named export; corrected to import the
module's existing exported main, before final syntax/runtime acceptance.
Launcher171 failed the first machine case twice (second attempt exposed the
captured diagnostic): core --no-human also requires --json. Fixed launcher173,
then added the public admission disposed-state guard and accepted final175.
No unrelated code was changed to satisfy either test.

No GC or native setup/update implementation is claimed here. Pins are an
admission primitive, not a substitute for the selected conservative Nu-process
census. Live installation, profiles/plugins/PATH, sources, legacy installation,
real modpacks, external publication and push remain untouched.
