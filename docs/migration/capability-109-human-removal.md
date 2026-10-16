# Capability109: Nu human removal without PowerShell

Accepted Native AOT143 and frozen package-human-removal-v143. Nu owns human
content remove, preserving plan item names, previous VersionId or installed,
removal reason, compact health, ordered file changes and transaction summaries.
The preview renders before any application, including explicit --yes/-y.
Machine --no-human data and its one-invocation preview/apply policy are unchanged.

The parent adapter prompts only with terminal stdin, using input --reedline.
Enter defaults NO; affirmative y/yes approves. No terminal, EOF, or input failure
does not approve. Dry-run never prompts or applies. Cancellation has an explicit
R3CLI status. A second request submits the reviewed token; backend expectation
checks from108 remain in force. Token v2 adds previous version and compact health
Status/Text, covering all newly displayed approval facts. v1 tokens are deliberately
stale after this change, requiring a new preview.

This refines D04's initial interactive event proposal: paired adapters can show a
complete bounded preview, collect input themselves, then submit an opaque digest.
The core remains machine-only and never interprets terminal EOF or defaults.
Binding full source and intended changes avoids an unbound preview/--yes sequence
without adding a long-lived process, persisted approvals or another stdin protocol.

Nu validates metadata and transaction before rendering or prompting. Missing or
invalid R3CLI/theme stops the first preview before application. If presentation
fails after a committed application, the adapter explicitly states whether the
transaction Applied receipt was true; it does not imply the source was untouched.
The latter guard is source-reviewed, not an injected postcommit renderer test.

Validation: scoped full format, Release/AOT143 zero warnings; all four Nu syntax
checks; protocol15/version; complete fresh-process Nu machine suite; token review
test; existing human read views; doctor/help and lazy-resource regressions PASS.
Parent Test-NativeHumanRemoval.ps1 copies a bridge-free private application and
uses real pinned Go Packwiz, spaces, private configuration/state and empty PATH.
Closed stdin cancels; yes+dry-run preserves bytes; -y applies; no pipeline values.
Real PTY runs additionally verify Enter cancellation then typed yes application,
and Ctrl-D EOF cancellation. Source hashes and parent selection/PWD/PATH asserted.
One broad regression command omitted mandatory PackwizExecutable for the doctor
harness; the corrected doctor/help command passed. No product change followed it.

Black's exactly2 Nu scenarios compare complete whitespace-normalized human text
against frozen108 original observations: nonterminal cancellation and yes cascade.
Both PASS; final file set/non-PSD bytes/PSD semantics match the original operation,
cancelled source bytes/mtime intact. Health is genuinely empty for this fixture.
Internal CLI process count was not instrumented or claimed.
White's exactly2 final Nu canaries PASS: invalid theme with yes preserves all
original source bytes; numeric version_id peer rejected at the presentation guard
before renderer/approval, parent state intact. Absence of a second invocation in
that malformed case is source-flow evidence, not an observed process counter.
Its initial heredoc parser error occurred before any product call and is retained.

Reports: assessment/review-blackbox/human-removal109-final-report.md;
assessment/review-whitebox/capability-109-human-removal-final.md.
CLI143 SHA25663EEE752F716742FB89CBB0E43A4426D02A94965BF17EB2B11CAEA7FDF2F2FDC;
Nu main9ABFCB833A2B92848F8E4104F6DF18A9DA3870BDB084AC4AA655C030E06680FD.
Other human writes still use legacy dispatch. No full independence, benchmark,
installer/update completion, live activation, publication or push claimed.
