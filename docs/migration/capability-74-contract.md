# Capability 74: structured Nu content mutations

Route content add/update --no-human to the accepted Native CLI108. Keep human/help
outside the dispatch table until their complete paired presentation is ready. A
native failure never retries the legacy bridge. Preserve parent session state and
unwrap the public transaction record without reconstructing selectors or plans.

Validate transaction states as actual booleans, nonnegative integer change_count
equal to changes length, preview=false, and no applied dry-run. Every file change
has action/path strings and a present nullable-string reason. Optional diagnostics
must be a valid list of code/path/message strings; present null is invalid. Retain
command/suboperation correlation and the existing envelope/exit/error checks.

Finite acceptance: actual empty update in a fresh Nu process with empty PATH and
private configuration/stable state; native add/update domain failures; one valid
nonempty transaction peer response plus six malformed shape controls. Existing Nu
regressions remain applicable. Test peer output is explicitly synthetic and does
not substitute for the nine complete operation comparisons in capability 73.

Only the Nu adapter/harness changes. No C# rebuild, dependency change or live Nu
configuration is needed. Native human/help and installed full closure remain open.

Accepted product checkpoint dc23a93. Final Nu SHA256:
8430C664E7CE9ADEB5FA5449DF24D82887B3132D0A1B9F066B438F5721D2C808.
Source review additionally required applied == (!dry_run && change_count > 0),
rejecting both impossible applied/no-change and unapplied real-change receipts.
Syntax and fresh Nu suite passed with CLI108 and programmable Core99 unchanged.
The new positive peer response exposed a historical harness mistake: CLI10 was
not the programmable peer and CapabilityUnavailable could satisfy generic failure
assertions. Test-NativeNuUse now verifies a good reply before the twelve malformed
ones. Those twelve and the six new malformed transaction controls pass with the
correct Core99 peer; old attribution is not silently treated as valid evidence.

Independent black-box acceptance passed three actual Nu/CLI comparisons with
PATH=[], space paths, private config/state and unchanged parent environment/root.
White-box acceptance passed two complementary peer canaries: complete valid
diagnostics with null/string reasons; impossible Applied=true/count0 rejection.
No builds were repeated because no C# changed. Error-capture corrections in the
black-box harness are preserved separately from product findings.

Reports under parent checkout:
assessment/review-blackbox/nu-content-operation-v74-final-report.md and
assessment/review-whitebox/capability-74-nu-content-mutations.md.
