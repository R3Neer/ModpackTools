# Capability122: recoverable active control and pinned Nu bootstrap

Productf9d639d implements NativeInstallationControl. The existing journalled
FileTreeTransaction protects only control/current.json and
control/Nushell/modpack.nu. It never copies version binaries into its journal.
Strict bounded JSON tracks current generation, previous build ID and up to128
retired version references. CAS is checked against the copied current state
inside transaction ownership. Source version and control identities are held and
verified; unknown topology and named streams are rejected. Application ownership,
process census, launcher admissions and retirement remain composing boundaries.

The generated Nu bootstrap statically imports an absolute version module and
exports that same version's core path. Raw Nu literals support spaces/apostrophes;
strict UTF-8 rejects unrepresentable paths before writing. The two control files
do not have joint atomic visibility. Independently, each imported module/core
pair is coherent and launcher readers select one current manifest generation.
Existing imports continue using their original version. Selecting a retained
previous version produces a new generation and records the outgoing one for
later retirement. Same-build identity-preserving selection is a no-op even if
the outer ZIP representation has a different trusted digest.

## Acceptance

- Final Core168 SHA256 D8E742B167B79EABC88310EE083EC39269D803475F96F1A5678D098261F97946.
- Final CLI169 SHA256 4F5C8E165D0AE8B866139A26BF46A05F283C13E3CF0B429F951ADC63CDE9C1CA.
- Locked restore, full formatting verification, Release builds and both win-x64
  Native AOT publications pass with zero warnings. CLI169 protocol15/version pass.
- Parent Test-NativeInstallationControl.ps1 on168:8 inspector calls (2 builders,
  initial selection, same-version no-op, injected first-file failure, second
  selection, stale CAS, selecting first version again). All pass. The injected
  failure restores the exact two previous control files through the journal.
- Parent owns one long-lived Nu and one fresh Nu. The old process imports first,
  waits while control switches, then reports first module marker plus first core
  path. Fresh process reports second marker and second core path. The application
  path contains spaces, apostrophe and hash. These marker modules test bootstrap
  pinning, not the complete ModpackTools adapter or executable protocols.
- White168:2 controls/3 calls including initial prerequisite; first-file rollback
  and stale-generation rejection preserve control, release handles and leave no
  pending journal. No payload EXE or Nu was executed by white.
- Black168:2 scenarios/3 core calls plus2 fresh Nu imports. Complete current and
  retired references, both18-file immutable versions, marker/core binding and
  stale rejection pass; bytes/mtimes preserved where expected.

Evidence: assessment/review-whitebox/capability-122-native-control-final.md and
assessment/review-blackbox/native-active-control122-final-report.md outside the
checkout. Fixtures assessment/native-control122/first.zip and second.zip use
CLI156 for three labelled executable roles and minimal valid Nu marker modules.
They are not a functioning installation package. Their external SHA256 values:
68D5B79C1B5F04AB6307DC31E57807A1D1224D14CB229FC3CF51D07C5544F318 and
2BA8902C4FFFB431748CA40FB6CA33D5E76D593D9C2E3F5445E45C9A16DF11D0.

Parent166 exploratory acceptance passed before the strict UTF-8 refinement from
white source review. Final acceptance was repeated on168; neither reviewer used
166/167. Test wait diagnostics were made bounded before final acceptance to avoid
blocking stderr reads if an owned Nu timed out. No product runtime defect or
failed control case occurred. No live install/profile/PATH mutation, payload EXE
execution, HTTP, release, push or relocation was performed.
