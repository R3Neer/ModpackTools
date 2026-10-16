# Capability 99: native diagnostic doctor

Public scope: doctor without --fix, including --details and selected-project
context, produces the original complete diagnostic envelope and Nu value. Repair
remains a separate checkpoint; native direct --fix explicitly fails, and Nu does
not yet claim it. Machine invocation never asks questions.

Contract: data.doctor contains integer failures/warnings and ordered checks.
Each check has string section/status/label/value/detail and array items. Item
properties retain original Status/Text casing; empty detail is the empty string.
Counts are exactly the fail/warn check counts. Diagnostic failures are successful
reports, not transport failures. Cancellation or uncertain child ownership is
terminal and must not be reported as a harmless warning.

Systems: application/version, writable private configuration probe, Packwiz
resolution/invocation, project root/catalog/session, optional Git/Default Options/
standard Minecraft detection, packaged R3CLI integrity, selected project required
files/editorial categories/index/dependency graph, latest artifact freshness.
One selected health inventory is reused for Default Options and dependency display.
Graph identity and resolver decisions are independent of display-side grouping.

Intentional differences: remove PowerShell requirement; application detail names
the native app; R3CLI reflects the native packaged manifest and actual hashes,
including fail when absent. Native diagnostics never refresh original project
files during artifact comparison; freshness uses accepted98 external strong cache.
Directory/display/manifest budgets and cancellation bound new discovery work.
Optional Minecraft reads only standard ApplicationData, tests use a process-start
private profile. No launcher installation, registry/PATH changes or plugins.

Finite acceptance: three frozen original cases (unconfigured/no project,
healthy/current artifact, conflicts/unknown details/stale artifact), all complete
rows/items and source hashes. Native environment exceptions are explicit above.
Two white-box complementary controls cover invalid configuration/probe cleanup
and actual malformed packaged presentation integrity. Parent tests cover Nu
typed count/detail/status guards and full public protocol/version/Nu regression.
Accepted product checkpoint: `0c9cfcf`. Locked restore, format verification,
Release and Native AOT publish succeed without warnings. Protocol15/version/full
Nu gates pass on CLI124, including selected-project doctor and three malformed
response controls. Black original cases1/2 pass full strict comparison on124;
case3 exposed a real unsupported NonBacktracking/lookahead combination in the
generated display regex (empty output/crash). CLI124 is preserved as failed.
Removing only that option retains generated AOT code and the fixed pattern.
Fresh format/Release/AOT and version checks pass on CLI125; black repeats exactly
case3 and matches all19 ordered checks/items/timestamp, failures4/warnings4 and
unchanged source lengths/bytes/mtimes/config. Prior two unaffected passes reused.
White source delta review accepts the fix; two ownership/package controls passed
on124 and were not falsely attributed to125. No unrelated corpus rerun.

CLI125 SHA-256: 2F0DCD339ED9902680C1EEFC4E003A63AD7FF7197DD1D8FA8652F7DC1134D9EF.
Nu SHA-256: 2A513776E53CFB57E37B3EDF9FEAA8AE8C96E349F61728ADB444D81BF48E4442.
Evidence outside product tree: assessment/native-doctor124-results.md,
review-blackbox/doctor99-final-report.md (and blocked124 report),
review-whitebox/capability-99-doctor-final.md. Three initial source-review fixes:
reject malformed editorial mod entries rather than false Valid, bound aggregate
catalog source lengths, and check detail joining budgets before allocation.
Catalog source quota observes stable file lengths; it is not an atomic snapshot
against uncooperative concurrent writers. No live install/profile changes.
