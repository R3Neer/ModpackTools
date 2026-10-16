# Capability125: conservative resumable native retirement

Product9dd2821 adds native setup cleanup to retire noncurrent verified versions. It does not
close sessions, remove the active version or recurse over arbitrary contents.
Removing a retired version also removes that version as a rollback target.

After bootstrap/current publication, installation admission captures Nushell PID
and process creation time in identity/generation-bound metadata under pins. A
collector compares a complete current census with that observation; still-live
old sessions defer retirement. Missing/generation-mismatched census falls back
conservatively to waiting for existing Nu sessions to close. An unreadable or
uncertain observation is never interpreted as zero processes. Fresh sessions use
the newly published bootstrap; direct imports of internal retired paths remain
outside the supported lifetime contract.

The same root admission serializes update, rollback, launcher pins and cleanup.
Collector requires exclusive access to the version admission pin, verifies known
hashes/lengths/default streams, physical directory identities and open-image/file
sharing before making a durable journal. Journal binds install/root/build/manifest
and exact tombstone name, and is bounded to128KiB before publication. Root and
pins/version-store directory handles preserve the outer identities. Pending
retirement blocks reselection of that build until recovery completes.

Version becomes an owned .retiring-GUID sibling. Resume permits missing expected
files/directories, while all present contents must still verify; unknown/changed
contents and file/directory ADS retain the tree. Progress reports exact path and
completed file/byte counts. Cancellation between deletes leaves the journal for
resumption. Before deleting the empty root, an empty-tree receipt is flushed;
missing original/tombstone without that durable evidence is not assumed deleted.
Only after deletion does a small journalled control transaction prune the retired
reference. Interrupted metadata finalization can resume without re-deleting an
unverified tree. Stable binaries and the current version are not rewritten.

## Acceptance

- CoreContracts186 SHA256 A88773DA1AF1C779D378D1101ADAEF76E4C18B04419379D69BFB8E50284C3DCC.
- Launcher183 SHA256 597FBE31BD782D176F1A2257749751BDD7C536330A8CE3896504DFC23C17BDC6.
- Setup187 SHA256 EC30D5049460DE67C01B97BD70F33C8CE69D6D56D4F27C1ABC0149521D8E2AAC.
- CLI185 SHA256 498E09A4171D4FB4B30645CFC2BE7BCFA353115A5E35F7989E9B431C51BA0682.
- Formatting, warning-free Release builds and Native AOT publication pass.
- Test-NativeRetirement.ps1 on186/183/187/185 passes:8 setup calls (2 packages,
  install, update, Nu deferral, pin retention, foreign-file retention, resumed
  removal),1 inspector cancellation call and1 owned waiting Nu process. PATH is
  empty, configuration/state/private Nu context are isolated, root includes
  spaces/apostrophe/hash. Nu imports the actual old public bootstrap before the
  update. Only that owned process is released. No foreign process is closed.
- Parent proves actual deletion/resumption rather than only conservative
  deferral: no other Nu session prevented that run. Complete current/stable file
  snapshots survive; interrupted retirement is preserved with exact unknown-file
  reason, then resumes after deleting only the test-injected foreign file.
- White source review caught journal serialization larger than its128KiB reader
  could accept on very long paths. Bound added before any journal/rename/delete;
  replacement metadata is also bounded. No runtime long-path claim is made.
- First184 parent failed on resumed nested-directory identity comparison: manifest
  forward slashes differed from Windows canonical backslashes. Corrected before
  final186/187; strengthened test asserts the actual unknown-file rejection
  reason, preventing an unrelated retention error from passing that case.

Opaque reviewer fixtures: assessment/native-retirement125/first.zip SHA256
D2590986236454D64E58E1032120FA0FFA58D46A2E5C0F8EFD405A515A8B8653,
build146B83DF3571175FB5E0B7C28DC32FE84DF20B4C8E93694869D179D32353B97B;
second.zip SHA256 B19188D3952DFBC39C7C8D667D6E2DF32E2C4C28BAC8C83B9656A0E365DF4157,
buildC88E7BF0CEBC30BAABC45CA4800D7F449C97FC07D4DF8B99DEC61FCDB7994FC3.

- Setup187 lifecycle regression passes (same deterministic package/install/update/
  no-op/rollback matrix as124); CLI185 protocol15/version pass.
- Black independent2/2 PASS:2 cleanup calls through installed stable setup plus
  install/update prerequisites and1 owned Nu. Its Nu waits without importing an
  old module, so it verifies the conservative census policy; actual imported old
  bootstrap lifetime is covered by the parent. Deferral while alive, then removal
  after its own clean exit. Exactly18 old files disappear; current/control refB,
  stable bin, bootstrap and private configuration remain intact. Only control
  generation/retired list and census metadata change as intended.
- White independent2/2 PASS,5 native calls: install/update prerequisites, ADS
  rejection, inspector cancellation prerequisite and wrong-owner journal
  rejection. Directory ADS gives exact Nushell path with stream/bundle/control
  preserved and no journal. Wrong InstallId preserves all44 installation files
  and releases its journal handle. No reruns, unknown retention or foreign
  process interference. Both owned namespaces are removed after inspection.

Evidence: assessment/review-blackbox/native-cleanup125-final-report.md and
assessment/review-whitebox/capability-125-native-retirement-final.md.
No remote updater, final Nu fallback removal or live activation is
claimed by this capability. Full-tree verification is deliberately repeated at
destructive boundaries; no speed claim is inferred from its linear bounded cost.
