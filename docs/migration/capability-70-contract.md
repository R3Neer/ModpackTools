# Capability70: source-bound project transactions and recovery

Accepted scoped Native capability, 2026-10-15. Public mutation activation remains later.

ProjectTransaction.ExecuteAsync owns one complete operation: physical project lease,
pending recovery, protected source/stage snapshots, trusted prepare/verify callbacks,
reviewed expectation, verified backups, pending journal before publication, per-file
commit, complete verification, committed marker and rollback/restart recovery.
This is a shared write boundary, not public content mutation activation.

Ownership uses a root directory handle and Windows volume/file identity, with a
Global named mutex held by one small dedicated owner thread across async awaits.
Release happens on that same thread; an abandoned mutex still grants ownership,
and every invocation performs recovery before preparing new work. The legacy home
file lock additionally coordinates old writers using the SAME state/path namespace;
it does not claim to coordinate old writers using other aliases/configuration homes.
No ACL/ownership changes or weaker lock fallback are used on access failure.

Primary platform references:
- https://learn.microsoft.com/en-us/windows/win32/termserv/kernel-object-namespaces
- https://learn.microsoft.com/en-us/windows/win32/fileio/obtaining-a-handle-to-a-directory
- https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew

stateDirectory is an explicit STABLE recovery namespace, outside the protected root.
It is separate from configuration/cache and must be fixed by the installed host,
not offered as an ordinary per-command override. An embedder changing this directory
must migrate pending state first. Physical mutex ownership spans namespaces, but
cannot discover journals deliberately left in a different directory. No hidden
pointer is added to the project and no source fingerprint exclusion is invented.

Expectation binds physical identity/canonical root, the full SourceFingerprint.All
Before and the exact ordered structural Path/Before/After write set. It does not
authorize replay against a different initial tree with merely the same changed files.
Stage context preserves the caller's values and overrides, rebasing Root/IndexPath;
the operation does not reread a descriptor to silently replace that context.
Callback T is owned result data; temporary stage paths cannot remain usable after
return. Source/stage scans retain .git exclusion and full protected file contents.

Private Native state schema1 lives at stateDirectory/native-transactions/v1/<physicalID>
in GUID32 workspaces. owner.json binds GUID/project identity/status, independently
of journal.json. Preparing/rolledback/completed ownership permits scoped cleanup;
pending ownership without a journal is retained as an actionable failure. A missing
journal alone never proves backups can be deleted. Foreign/unowned directories are
preserved. All owned journal shapes/identities are parsed before workspace effects.

Journal schema1 contains ordered changes, Before fingerprint, created directories
and EXACT reserved publication temporaries for both forward and rollback copies.
Each temporary is adjacent to its target, derives from transaction GUID/change
index/commit-or-rollback phase, and is declared before creation. Recovery deletes
only those declared names after validating every destination and backup. No prefix
scan or broad deletion of other temporaries is allowed. Copy verifies the source
hash before atomic rename; backup integrity is checked before pending publication.

Recovery validates ALL destinations against Before/After and ALL required backups
before restoring any file, rechecks each boundary, skips already-Before files, removes
only listed empty created parents and verifies restored hashes. Recovery has a
separate reserved budget and ignores a spent forward cancellation token. The complete
root preflight follows recovery, so a valid interrupted publication temporary cannot
block recovery by consuming the exact forward/source file budget. Unaffected external
links/data are preserved; a subsequent complete preflight rejects unsafe new work.

Original behavior retained: dry-run recovers earlier pending work and therefore MAY
restore source files, though it never applies the newly proposed write set. A killed
writer is recoverable by a fresh process in the same state namespace. Conflict or
damaged backup retains journal/stage/backups. Native RecoveryFailed keeps both the
primary error and the recovery failure plus journal path, improving the original's
loss of the first cause. Injected IO failures have typed errors rather than raw PS
exceptions. A committed marker is terminal: later cancellation/notification/cleanup
failure yields Applied=true with diagnostics, never a fictional unapplied result.

The user explicitly permitted error improvements. Native Busy identifies the legacy
lock path and underlying IO cause; ConcurrentChange/PlanChanged identify the
protected root. Their IDs/categories remain stable while wording and hints direct
inspection of transaction state before retrying. The reviewed expectation also
binds the complete initial fingerprint and physical identity, which the old
write-set-only comparison did not express. These diagnostic differences are
declared rather than normalized away by the differential harness.
RecoveryConflict puts the affected relative file in Target and its journal path in
Details. InvalidBackup puts the precise damaged/missing backup path in Target,
instead of leaving Target empty and embedding that path in the old message/details.
These two scoped diagnostic refinements retain their original error IDs/categories.

First scope protects file-byte state, not ADS, ACL/owner, directory topology or
attributes as an independent transactional model. File/directory replacements and
case-only staged renames are rejected before pending publication, including dry-run.
All fingerprint comparisons include exact path casing; this is a deliberate stronger
precondition than the legacy case-insensitive tree-difference map. Per-file atomic
replacement does not imply simultaneous visibility of an entire multi-file tree or
a power-loss guarantee beyond the recorded process-interruption tests.

The OS file copy retains existing alternate data streams and file attributes while
staging, backing up and publishing. Stream sizes are bounded before each copy;
the protected Before/After hashes still cover primary file bytes only, so ADS and
attributes have no independent conflict detection. Trusted callbacks control their
own writes: recreating a staged file can remove its alternate streams. The focused
test checks the stream before preparation, changes primary bytes in place and
checks the stream after commit and after rollback. OS-copy cancellation is checked
between file copies; hashing and source scans additionally check between chunks.

Observer is a trusted extension for progress/fault testing. Before pending it may
abort; during publication its failure triggers rollback; after committed its failure
is diagnostic only. Test inspector supports complete declarative fixtures, sidecar
traces and paused OWNED children. Synthesizing a registered partial publication
temporary at JournalWritten is labeled separately from real kill after FileCommitted.

Validation complete. Locked restores, format verification and Release builds
passed for Core, CLI and contracts with zero warnings/errors. Core96/CLI105 Native
AOT publications completed without warnings; Core96 default contracts passed,
including transaction faults, external-edit retention, restart recovery and ADS.
White-box finite Native acceptance passed eight controls (default plus seven
scenarios), including separate real-kill-after-publication and registered synthetic
partial-temporary/restart cases. The latter used MaximumFiles=RecoveryMaximumFiles=1
and preserved the original while removing only its registered temporary. Other
checks covered cross-state physical exclusion, rollback, both-cause diagnostics,
external-edit protection and dry-run. Independent black-box acceptance passed all
16 agreed attempts with complete file bytes, Changes/Applied, independent fingerprints,
owned recovery state, rollback and real process-kill/restart. The 176 frozen original
files and both original sources remained intact. Native stderr was empty and handles
were reopenable after completion. Two deliberate failed-recovery workspaces retained
their evidence; all other completed workspaces were removed.

Reports: assessment/review-blackbox/transaction-v96-final-report.md and
assessment/review-whitebox/capability-70-project-transactions.md, under the parent
reorganization checkout. Provisional harness comparisons are retained separately;
the final comparison reused captured executions rather than replaying the corpus.

- Core96: `58F62D3DDCE4C0219B64885D18D5875AD75D949ABC435A90D514A58886232A01`.
- CLI105: `EAD14425F711C322C207CF9DDD9F35E348B4227368D2DABA66A12EF1BE9548EA`.
- CLI105 protocol15, version and fresh Nushell checks passed. The existing native
  Nu checks run the tested supported commands without PowerShell; they do not yet
  establish complete product/runtime independence.

No installation, real pack, push or release is involved.
