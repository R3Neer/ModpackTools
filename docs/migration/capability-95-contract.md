# Capability 95: transactional complete build

This internal capability prepares, exports, independently validates and publishes
an MRPack through the existing source transaction. Public CLI/Nushell wiring is
a separate checkpoint. No installed product or personal shell is changed.

Checked project health is evaluated before tools run. Strict policy rejects
unknown results. No-refresh requires an already verified complete index. Otherwise
refresh runs in the owned stage. Name, version, dependencies, indexed payloads,
editorial metadata and descriptor are frozen before one native Packwiz export.
The resulting index and source declarations must still represent those inputs.

Every plain override must match its source SHA-256. Every managed artifact must
have the declared hash, install destination, side and optional state; manifest
downloads must match the declaration. Known provider hashes and sizes are also
checked. Embedded files are hashed in one streaming pass. For manifest hashes
that do not include the declared algorithm, the existing artifact provider obtains
a verified reference and the reopened bytes are checked again against that declared
hash in the same pass as their MRPack hashes. Missing and extra files are rejected.
Ambiguous plain/managed destinations and metadata outside health coverage are
rejected before exporting. Readers remain bounded and cancellation-aware.

One export plus independent input coverage replaces the legacy second export.
Packwiz can skip a failed download or override and still produce an archive; two
identical incomplete exports do not establish completeness. Such an archive now
fails with Build.ArtifactMismatch and is never published. This is an intentional
integrity correction, not a claim of measured performance improvement.

Dry-run executes in the disposable stage and returns a semantic snapshot without
publishing files. Output preparation failure preserves all original bytes. Keep-old
preserves the existing output and uses the legacy timestamp pattern; a timestamp
collision fails explicitly instead of overwriting another preserved build.
Uncertain child termination uses the shared ModpackTools.RetainStage marker and
preserves evidence. No cleanup may unlink an output still owned by a child.

Accepted product checkpoint: eb1c570. Existing SDK10.0.300 locked restore,
format verification, zero-warning Release and win-x64 Native AOT passed.
Core117 SHA256 FA4ED4387619E02DC29E501F705FEF4E68DED0D7EBC52C2CF2C015048026AEDA.
Native default contracts and real-Packwiz dry-run/output-failure/apply/keep-old
contracts passed. Black-box4/4 verifies complete seven-record actual archive,
stale no-refresh, source-preserving dry-run and the intentional omitted-file
rejection. White-box2/2 verifies output failure after export validation and strict
Unknown rejection before any tool. Both reviewers report no blockers.

Parent evidence: assessment/native-build117-results.md. Independent reports:
assessment/review-blackbox/build95-final-report.md and
assessment/review-whitebox/capability-95-build-operation.md (outside product tree).
Black-box checked original override bytes, all frozen oracles and source integrity;
duration, progress and temporary GUID log fields are explicitly volatile. No
network, personal configuration, active modpack or installed package was modified.
The initial sandbox default-run atomic-cache failure and initial missing-namespace
compile error are retained in parent evidence, distinct from final passing results.
