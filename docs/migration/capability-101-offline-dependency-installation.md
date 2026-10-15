# Capability 101: transactional offline dependency installation

Accepted product checkpoint: `9f85299`. Native prepared two-file package plus trusted metadata installs
to an explicit target, revalidating every file and executing --help in the stage
under the existing owned-child process mechanism before any publication. Repeated
equal content returns Installed=false. Corrupt selected files may be repaired;
unrelated target leaves are retained. No automatic config/PATH writes, downloads,
human prompts, live activation or finished doctor --fix claim in this checkpoint.

One file transaction engine owns physical identity/leases, source-bound snapshots,
durable backup/journal, publication and complete rollback/recovery. It is extracted
as FileTreeTransaction with root-path callbacks; ProjectTransaction is a thin
domain adapter that preserves caller context overrides and the same context object
across preparation/verification. Existing journal/lease namespaces, limits, errors
and recovery semantics remain compatible. No synthetic Minecraft project model
is constructed by the package installer. Tree scope retains the existing .git
exclusion, not a general claim to snapshot arbitrary repository metadata.

Canonical source physical identity and canonical future target/state paths must
be disjoint, including aliases/8.3 spellings, before creating a target. Verify
source evidence again at the protected boundary; stage copying uses declared byte
quotas. Smoke invocation must not change the hashed stage. Cancellation/uncertain
process ownership remain terminal. Successful prepared source lifecycle remains
owned by its caller, separate from durable installation recovery state. An empty
new target container may remain after failed preparation; no unverified executable
is published or declared installed. Package/state source bytes stay preserved.

Acceptance: scoped format/Release/Native AOT without warnings. Core127 complete
default regression passes (shared engine); parent actual-tool installation/repeat
and corrupt-license-only repair preserve foreign target sentinel/input bytes.
CLI129 protocol15/version/full fresh-process Nu regression passes with empty PATH.
Black2/2 scenarios compare full installed/prepared bytes against original valid+
repeat and corrupt-target repair;32 original manifest entries/three sources remain
intact and20 exclusive handle checks pass, no publication temporaries. White2/2
controls pass: failure after first published file restores old target set/hashes;
trusted non-executable fixture fails owned smoke before publication. Successful
prepared sources stay caller-owned. No new kill/restart durability claim from
the injected-IO control; previous full recovery contracts passed in core defaults.

Original PS helper did not smoke-test and skipped license integrity when the
executable already matched. Native verifies both selected files and repairs a
license-only change: installation identity includes all shipped package leaves.
These are deliberate stronger guarantees, not silent parity claims. Source review
caught physical alias overlap and missing internal facade forwarding before
compilation; exact old IO wording is preserved.

Core127 SHA-256 C2848B91858C27514BCDB0782ACDFB34A8BCB4E0BE3346243E58F842878D63F4.
CLI129 SHA-256 6BFF7BD3EC165D00BCE4F74B9CCDD141E2D942B88CF36A06BC0B72CA1A2FC23C.
Incomplete CLI128 attempt lost its process session and produced no executable;
cause unknown, never accepted/tested. CLI129 sequential publish has captured
stdout/exit0. External assessment/native-dependency-install127-results.md,
review-blackbox/packwiz-install101-final-report.md and
review-whitebox/capability-101-dependency-install-final.md retain full evidence.
