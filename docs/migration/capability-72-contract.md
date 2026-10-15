# Capability 72: owned native Packwiz refresh

Accepted scoped Native capability, 2026-10-15. Public mutation composition remains later.

RefreshAsync accepts an owned stage, resolved Packwiz observation and the absolute
path to the same packaged Native host that supports its private tool-child entry.
Execute the real tool with ArgumentList=[refresh] and stage working directory;
never invoke a shell or produce a handwritten partial index. Validate actual
post-refresh pack.toml, contained configured index, valid index TOML and the actual
declared index hash before returning a typed receipt. Tool output is captured,
bounded and separated from public machine output.

Resolution preserves configured path (including missing => no fallback), PATH
executable, then managed pinned path. Resolution alone never installs or modifies
profiles/configuration. The existing PATH Packwiz is available for isolated real
refresh tests: D:/Desarrollo/Toolchains/Go/gopath/bin/packwiz.exe, SHA256
7EA4E6668265A5A1C5E1877BDC659DB8C06619D7AA13A248032B41A5B6D95E0C.
Its embedded Go module version is v0.0.0-20260218225342-dfd8b68a4796,
matching the selected source revision; it is NOT byte-identical to the downloadable
managed artifact (7182F0...), and no such artifact hash equivalence is claimed.

The host starts its own Native helper, which blocks on stdin before any tool
launch. Assign that waiting helper to a non-inheritable kill-on-close Windows Job,
then send the bounded private launch payload. The helper's tool inherits the Job;
there is no start-tool-before-ownership race. EOF without a complete payload or
assignment failure launches no tool. Only the parent owns the Job handle.

Bound combined actual stdout/stderr bytes, read both concurrently and return a
bounded private result. Deadline, cancellation and output-limit failures terminate
owned execution and confirm it stopped before releasing the stage. Never fall back
to an unowned process. If termination cannot be confirmed, retain the stage and
report an actionable error; transaction recovery must not guess it safe to delete.

Source-review refinement: a previous preparing owner without a journal no longer
authorizes automatic restart cleanup. It has no durable proof that preparation
closed safely, so recovery preserves it with PreparationInterrupted. This also
protects an uncertain tool stage if writing the retained marker fails; both primary
and marker failures are reported. Preparing work can still be cleaned by the SAME
invocation after its trusted callback finishes safely. Pending/committed journals
keep their accepted70 recovery behavior. This deliberately favors retained evidence
over guessing a pre-journal interrupted stage safe to remove.

Normal builds and supplementary managed contracts pass; process-specific contracts
run explicitly through --verify-native-tools on the published Native host. A managed
dotnet DLL invocation is not a substitute for this child-host acceptance.

Locked restores, format checks, strict Release and Native AOT publications completed
without warnings/errors. Core98 default and --verify-native-tools passed. CLI107
protocol15, version and fresh Nu checks passed.

- Core98: `2F0E31C586FE72F31B14753E683770B48C192795CEAF80E214D709B9984B4057`.
- CLI107: `7C149D2D2572E834B0AE38F8ED040BA8D1EFF2272EC0D5457F72074801E1DFF1`.

White-box independent Native acceptance passed three canaries: incomplete EOF
launches no tool and returns a private typed failure; a root fixture exiting while
its writer inherits output pipes reaches the deadline, then both processes stop;
and cancellation stops a hanging root/writer pair. PIDs were absent and writer logs
stable after cleanup. The exiting-root case is deadline cleanup, not immediate
successful completion. Black-box acceptance passed seven invocations over the six
agreed scenarios, including both timeout and cancellation variants. Real Packwiz
refresh matched complete original root bytes, configured custom index/hash and
output. Nonzero streams, output cap and exit-zero invalid refresh were distinguished.
Both fixture processes were gone, logs stable and handles reopenable after timeout
and cancellation. Original eleven oracle files, four sources and installed Packwiz
remained intact. One initial resolver canary accidentally selected the inspector's
explicit profile; that capture is retained separately and only that canary was
corrected. The real refresh was not repeated.

Reports under the parent checkout:
assessment/review-blackbox/packwiz-refresh-v98-final-report.md and
assessment/review-whitebox/capability-72-packwiz-refresh.md.

Finite acceptance: configured missing priority; real custom-index refresh; nonzero
exit with both streams; output cap; cancellation/deadline and owned descendants;
exit-zero malformed outputs. Synthetic native fixtures are labeled separately
from the real Packwiz execution. No live pack, dependency install or network is
required. Public content command composition, doctor/build and installation remain
later work.

Platform references:
- https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject
- https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_extended_limit_information
- https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_accounting_information
