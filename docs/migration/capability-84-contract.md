# Capability84: transactional registration of an existing Packwiz project

Internal typed operation: catalog root, existing direct-child path, ID, optional
explicit display name/version/output filename, stable transaction state. Preserve
validation priority: ID/path, root membership, registration conflicts, manifest,
index, required technical fields, duplicate catalog ID, explicit overrides.
No Packwiz/provider execution. Registration preserves every technical file and
existing README/gitignore; creates project descriptor, empty editorial tables and
missing README/gitignore. Result project paths and created-file paths use original
root, never stage paths. Prepare caller output before publication.

Use physical catalog ownership then existing project transaction ownership, in
that order, to serialize duplicate IDs across distinct children. Reject links and
physical aliases escaping the catalog. Recheck registration conflicts in stage.
All failures before commit preserve source; transaction recovery handles partial
publication. No personal configured root, live installation or real packs.

Intentional documentation refinement: newly generated README describes session
selection as shell-specific and doctor without a PowerShell prerequisite. Existing
README bytes remain unchanged. Markdown code fences use text rather than suggesting
PowerShell is needed. PSD serialization may differ while data remains equivalent.
Three black-box cases: preserve existing docs, create missing docs, nonempty .modpack
conflict. White review adds complementary safety/output-failure controls.

White source review correction: conflict diagnostics enumerate at most 33 entries, report sorted first 32 plus omission marker. Small conflicts retain original exact details. Stage manifest text must match preflight and its index must still exist; otherwise typed RegistrationChanged precedes publication. Existing README/gitignore directories also count as existing, as in original.

Accepted Core110 SHA256 57C0AD0E9A0B9E67A5357A9A3841B4995CFEC5E66CF56BAFEA9B43EBB00A9AAD. Strict Release build and AOT zero warnings, format verification and actual Native default suite passed. Black-box3/3 complete original project/created files/PSD data/bytes and declared README delta; white2/2 output failure and duplicate sibling. Reports in parent assessment/review-blackbox/registration84-final-report.md and review-whitebox/capability-84-project-registration.md. Public CLI/Nu registration remains next capability.
