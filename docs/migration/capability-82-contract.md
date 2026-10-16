# Capability82: native configuration writes

Typed root/Packwiz setting and bounded value; capture and hash the same bounded
raw configuration bytes, preserve BOM decoding and unknown PSD fields. Validate
existing config before setting value; root must exist, Packwiz must be an existing
file unless auto removes only PackwizPath. Never execute a configured file.

Canonical-directory namespace ownership is stable across initial directory
creation. Pin the directory, validate complete bounded UTF8 PSD and prepare output
before publication, own an exact CreateNew temporary with flush-to-disk, compare
original hash before replacement, atomically publish or replace. Initial creation
never overwrites a competing first writer. Observed external edits fail without replacing
those bytes. Cancellation is checked before publication; publication is terminal.
Cleanup tracks only the exact owned temporary and attaches secondary failure to
primary errors. No directory creation for invalid values. No tree copy or project
transaction is used for this single configuration file. Existing metadata is
retained by Windows replacement; ADS behavior requires an executed Windows check.

Black3 compares root-with-spaces preservation, auto removal, invalid root.
Parent covers output failure and external-edit CAS; white reviews native ownership
and two complementary controls. Public config parser/wire and Nu follow separately.

Concurrency boundary: native writers serialize by canonical namespace. The final
rehash rejects external edits observed before replacement. It cannot guarantee
atomic compare-and-swap against an uncooperative writer in the interval between
closing that hash handle and replacement; no stronger guarantee is claimed.
Invalid UTF8 maps to Configuration.InvalidFile before value validation. Busy has
a configuration-specific ResourceBusy error with the exact configuration path.
The test-only inspector may pause preparation with owned marker/release files,
with a30second limit, to verify cross-process ownership deterministically.

Accepted product7c1ea75. Frozen Core108 SHA256
615FE6C9FFEE314842B2C87407223A82A2595650F00E7DB87BF02AEED674CF6D.
Locked restore, format, strict Release/AOT zero warnings and Native defaults pass.
Black3/3 verifies complete values/physical config paths/published hashes/PSD/error
wire and unchanged sentinels. White two controls across three Native calls pass:
existing Packwiz path writes without execution and preserves unknown data plus
actual Windows ADS; deterministic cross-process Busy leaves original bytes/no temp,
then releasing first writer publishes its own Root. ADS preservation is established
on the tested Windows filesystem, not promised across all filesystems.
Reports assessment/review-blackbox/config-write82-final-report.md and
assessment/review-whitebox/capability-82-configuration-write.md.
Parent separately verifies pre-publication output failure, observed external edit,
invalid UTF8 before value validation, empty data and auto removal. No public
CLI/Nu, live configuration, install or network was involved in capability82.
