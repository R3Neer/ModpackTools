# Capability104: native doctor repair

Accepted on CLI137. The machine doctor --fix proposes project repair without
interactive input. Explicit --yes permits environment/project application;
--dry-run always prevails. Human confirmation remains a presentation concern.

Missing Packwiz uses the fixed verified native bootstrap, then the atomic
configuration writer. No PowerShell installer or PATH mutation occurs. Project
repair uses existing ContentOperation.Repair and applies only against the
physical identity, whole-tree fingerprint and changes reviewed in its preview.
Environment publication and project publication have separate receipts:
packwiz_installed, configuration_updated and project_applied. Aggregate applied
is their disjunction; an incomplete later stage cannot erase earlier success.

data.doctor preserves diagnostic shape and adds repair only for --fix, containing
applied/dry_run/preview, those receipts, nullable packwiz_path, change_count,
changes and diagnostics. Causal diagnostics preserve code/path/details and a
retained_evidence flag; wire messages/details have explicit truncation flags.
Nu validates consistency before returning the doctor record.

The host encodes baseline output before repair, reserves capacity/diagnostic
space before mutation, preflights dependency/config receipts and encodes project
changes inside the transaction's prepublication callback. A larger diagnostic
readback falls back to the already validated baseline plus truthful receipt.
No diagnostic readback follows uncertain retained transaction evidence.

Known intentional differences: noninteractive --fix does not implicitly approve;
preview/dry-run preserve originals even though the legacy artifact check may run
Packwiz export over the original after a declined repair. No project-root prompt
or user-PATH modification belongs to this machine operation. Existing optional
PowerShell adapter remains separate; live Nu activation is not authorized.

Finite acceptance: black two fixed legacy originals (stale-index preview declined
and explicit application); parent Nu preview/dry-run/application/repeat with
empty PATH, plus native missing-dependency bootstrap in private configuration;
white complementary prepublication/partial-success controls, source audit.

Evidence: CLI137 SHA256
6B1D76A371F6332C6E53C64B0A33549E00B019B7135A78AF7B4666FD21240671;
Nu SHA256 30666B7FA4FDD12FEF48AB30089AC387B83BCF929F9B19EB120F4085EAC89049.
Scoped format, Release and Native AOT passed without warnings; protocol15,
version and complete fresh-process Nu suite passed137, including three malformed
repair receipts. The actual production download/configuration/repeat integration
passed with empty PATH. Its initial harness invocation lacked --json and was
rejected before repair; the corrected public option pair passed.

Black reused exactly two fixed originals and compared full doctor envelopes,
two index/manifest changes and final applied bytes. Preview preserves source
bytes/mtime intentionally; application matches legacy. Native transaction receipt
is nested in doctor.repair rather than an additional top-level data.transaction.
White's two executed controls passed: invalid PSD1 rejects before acquisition;
yes+dry-run without tool/project leaves configuration and installation unchanged.
Prepublication output callbacks, partial-publication receipt and retained-evidence
ordering were source-reviewed, not fault-injected in this checkpoint.

CLI134's first full Nu run passed before final error-path refinements;135 added
rejected-preview dropping.136 contains a preacceptance diagnostic-array writer
placement mistake noticed by the implementer, corrected before final freeze137.
Do not attribute137 acceptance to those earlier frozen candidates.
Source review prompted explicit config receipts, output preflight/reservation,
bounded rediscovery/cancellation and retention of causal paths/details. No SDK
package/lock changes and no live activation.

External reports: assessment/review-blackbox/doctor-fix104-final-report.md,
assessment/review-whitebox/capability-104-doctor-repair-final.md and
assessment/native-doctor-repair137-results.md.
