# Capability94: bounded MRPack semantic snapshots and comparison

Read pack name/version, sorted dependency records, manifest file semantic hashes
(sorted hashes/downloads/env and invariant Int64 size), and expanded override byte
hashes. Preserve normal snapshot sequence and sort deltas Kind/Path with stable
ordinal tie breaks. ZIP compression, timestamps, property/download/entry order do
not alter semantic comparisons. Typed records, immutable results, no reflection,
no tools/network/extraction/writes. Archive census before BCL allocation, bounded
manifest/expanded bytes/records/text, cancellation, file handle and pooled buffers.
Bounded StreamReader BOM detection preserves UTF8/UTF16/UTF32 manifest encodings.
Fingerprint equality retains original case-sensitive invariant text semantics;
ASCII digest comparisons are unaffected. Parent covers BOMs and canonical Unicode.

Reject duplicate/case-colliding ZIP or semantic identities, unsafe paths, linked
entries and malformed manifests using typed diagnostics. Optional absent env is
an empty set. These explicit safety refinements replace ambiguous legacy errors/
last-wins behavior. Additional regular archive contents outside overrides are
ARCHIVE entry:<full path> records, including client/server-overrides, so content
cannot disappear from freshness comparison simply because legacy ignored it.
No filesystem extraction is implied. Existing normal legacy record keys and
fingerprints stay unchanged; download sorting adds ordinal ties for determinism.

Black3 originals: reordered complete legacy kinds compare zero; full added/removed/
changed deltas; missing manifest typed error. Parent additionally checks extra
archive contents, collisions and cancellation. White complements an unsafe ZIP
path and expanded-data quota. Public export/diff wiring remains separate.

Accepted Core116 SHA256 68741A8019F09FAF5E17566D1E61B3DB22F4BC61DB14A92F20FA5722A9897209.
Locked restore, changed-file format verification, strict Release/AOT zero warnings
and default native contracts pass. White source review caught BOM and culturally
equivalent text regressions before freeze; corrected and tested with UTF8/16/32
BOMs and canonical Unicode. Black3/3 compares complete snapshot sequence/fields/
fingerprints, zero reordered diff, exact 2added/2removed/4changed delta and complete
missing-manifest error. Six ZIP bytes/lengths/timestamps and source oracles intact.
White2/2 rejects unsafe ZIP paths and expanded-byte quota exhaustion without partial
snapshot, and proves ZIP handles released after failures. No extraction/tools/red.
Reports parent assessment/review-blackbox/mrpack94-final-report.md and
review-whitebox/capability-94-mrpack-snapshot.md. Extra ARCHIVE/collision/cancellation
coverage belongs to parent defaults, not the three original comparison cases.
