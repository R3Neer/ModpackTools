# Capability93: complete Packwiz index verification

Read-only typed ProjectIndex.Read report Rows/Issues/Valid; no tools/network/writes.
Read pack and index bounded raw bytes, hash the same index bytes that were parsed.
Verify supported pack-to-index digest and each row's actual file bytes. Resolve row
paths relative to the index parent as Packwiz does, with strict containment and
reparse rejection. Duplicate physical row paths or non-boolean metafile reject.
Require nonempty files and every physical .pw.toml in mods/resourcepacks/shaderpacks
exactly once with metafile=true. Strict bounded traversal includes hidden files
and never silently ignores inaccessible directories. Missing metadata diagnostics
sort ordinally. Per-observation digest memoization and pooled bounded stream reads.

This deliberately closes legacy Test-MpProjectIndex acceptance of empty/omitted
metadata and its root-relative interpretation of subdirectory indexes. Existing
valid/root-hash-stale/row-hash-stale issue text remains. Empty with physical metadata
adds 'Index contains no files' and 'Index omits managed metadata <relative>'.
Limits cover rows/entries/directories/documents/hashed bytes/path text/issues/details;
quota/cancellation returns no accepted partial report. Observation alone is not a
project lease/fingerprint: future build uses an owned transaction stage; doctor/
diff must bind their own source view before claiming current. No public build yet.

Black4 originals: fully valid, stale pack link, stale row and legacy empty-index gap.
White2 complement subdirectory omission safety and bounded traversal/row quota.
Parent defaults cover valid/omission/metafile/duplicate/cancellation cases on
synthetic owned files, with source integrity checked externally by reviewers.

Accepted Core115 SHA256 69927296DCA8A89B052C77EBEC54C32296CF0432B4E4FC344404E77ECF83C03E.
Locked restore, changed-file format verification, strict Release/AOT zero warnings
and native defaults pass. Black4/4 checks literal existing diagnostics and explicit
two-issue empty-index refinement, with source file contents/lengths/timestamps and
original oracles intact. Its first comparison-summary array unrolling mistake was
corrected from the same captured responses without reruns or changed expectations.
White2/2 verifies a coherently hashed subdirectory index still detects omitted root
metadata, and MaximumEntries exhaustion produces typed failure without partial
Valid/Rows/Issues output. No tools/network/write operations in index inspection.
Reports parent assessment/review-blackbox/project-index93-final-report.md and
review-whitebox/capability-93-project-index.md. Public build/diff/doctor remain.
