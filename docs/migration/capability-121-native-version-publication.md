# Capability121: immutable version publication

Product70b8778 adds NativeVersionPublication.PublishAsync. Its caller supplies
an already owned, existing version-store physical identity and a verified
prepared package. The store must be disjoint from that preparation. A physical
store lease serializes publishers; source/store handles protect their identities.

Files are copied with CreateNew, bounded streaming hashes, progress, cancellation
and disk flush into an exclusively reserved sibling stage. Complete source and
destination verification precede a same-parent Directory.Move to the build ID.
The receipt is materialized before that move. No read, observer or cancellation
check follows publication. Existing identical versions are verified and reused
without rewriting; changed contents or occupied targets are preserved. Unknown
or changed stage evidence on failure is retained with its exact path. The120
copy/directory/cleanup primitives change visibility to internal only.

This publishes data, not the active version: application protocol smoke,
installation ownership, control generation/bootstrap, launcher and updater remain
subsequent boundaries. No payload runs here. The caller must retain installation
admission ownership when composing this primitive with future activation.

## Acceptance

- Core164 SHA25618828EFDFCE13BB7C498BC492B3F1E999B093DF9617DBB74537ADF0D03835CD5.
- CLI165 SHA25618EC27E26D0FC086E0C4D7BA747D381440383AD8EC1A7795051A2DCD9E7FBA56.
- Locked restore, formatting/full verification, Release builds and win-x64 Native
  AOT publications pass with zero warnings. Both artifacts are frozen separately.
- Parent Test-NativeVersionPublication.ps1:6 calls (fixture builder+5 operations)
  pass publication/full hashes, identical identity+mtime-preserving reuse,
  changed existing version rejection, foreign completed-copy edit/cancel
  retention and target collision with safe own-stage cleanup.
- Package120 regression on164:10 calls pass, including file/directory ADS
  retention. CLI165 protocol15 cases and version pass.
- Black-box164:2 scenarios/3 calls, complete publication/reuse and preservation
  of an existing altered version; source ZIP, sentinels and caller-owned prepared
  trees remain intact. Its three preparations remain as evidence, intentionally
  distinct from transient publisher stages.
- White-box164:2 controls/2 calls, foreign edit/cancel retained and occupied
  target preserved while the verified own publication stage is removed. Prepared
  source remains complete. Source review found no concrete remaining blocker.

Review evidence is under assessment/review-blackbox and assessment/review-whitebox
outside the product checkout. The same120 format fixture is used; this does not
assert working setup/launcher identities. Target collision currently propagates
IOException and the inspection harness reports Core.InputReadFailed/exit2; the
future setup host must provide an actionable public diagnostic. No failed parent
or white attempts occurred. Black final report records its own harness scope.
No live installation, control switch, profiles/PATH changes, HTTP, release or push.
