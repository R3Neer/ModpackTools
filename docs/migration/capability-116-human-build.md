# Capability116: native human build receipts

Native AOT153 and opaque package153. Parent integration and both independent reviewers accepted the final artifact.

Build's existing owned BuildResult supplies optional typed presentation: final
PublicProject, compact health items, bounded Packwiz logs, unnumbered mod/shader
rows, and legacy category groups. The adapter does not reread the source tree or
publish selection caches. Resource rows retain the existing machine data field.
Machine data and dry-run shape remain unchanged. Dry-run presents only compact
health and the explicit validation-without-changes notice, plus diagnostics.
Applied builds render logs, mod/resource/shader inventories and the legacy summary
fields; the existing --open native behavior remains unchanged and is not newly
exercised against a real Explorer session here.

All output, row projections and buffer capacity are prepared before publication.
Human output reserves8MiB (max initial56MiB), machine output reserves4MiB (same
previous max initial60MiB), within the existing64MiB protocol cap. Diagnostic
reserialization reuses the owned buffer and precomputed resource/presentation
arrays. Publication remains terminal, without new domain reads or cancellation.
White-box source review caught resource rows being reconstructed during late
reserialization; these are now captured once in PrepareAsync. AOT152 existed but
was neither packaged nor runtime-tested;153 is the corrected native build.

Nu validates presentation counts, types, dry-run absence of committed lists,
category row bounds/coverage and compact health statuses. Progress validates R3CLI
package/theme before work. The success/catch path distinguishes published build
from dry-run. Progress is now generic Building project... rather than parsing the
active project's name again in the adapter. Decimal size/duration formatting uses
Nu's two-decimal conversion and the current locale (es-ES decimal comma verified).
Generic progress is an explicit presentation difference, not a data/performance claim.

Parent integration adds real-Go human dry-run/applied calls to existing machine
build cases, checks no pipeline output, preserved active selection and manifest
preservation in dry-run. This new focused check does not claim raw-log/Explorer
fault injection or postcommit cleanup injection; existing core contracts cover
transaction guarantees separately.

The parent decimal-format probe initially expected invariant 1.20, but the actual
Nu result was correctly localized 1,20. Investigation confirmed0,00 and1,20;
an unnecessary helper draft was discarded without packaging or runtime campaign.
Final source Views is byte-identical to the original153 frozen snapshot.

## Observed checks

Locked restore, scoped whitespace formatting/full format verification and Release
build passed with zero warnings before AOT153; AOT153 publish had zero warnings.
Three touched Nu syntax checks, protocol15/version, full fresh-process integration
and minimal bridge-free human doctor/help/missing-resource regressions PASS.
Full integration performs real-Go build dry-run and publication with a local
Default Options fixture plus active alpha/inactive beta resources, verifies null
human pipeline output and unchanged session, and retains machine shape guards.
Creation in that broader suite allows Mojang metadata access as recorded114.
Build logs are the bounded actual Go output, including its original stage paths
and progress control content, as in the existing BuildResult; no new log sanitizer
or raw-log override is introduced by this UI capability.

EXE153 SHA256 D6E692066A761590359BDFFCABB438CF85161FFBB4D5E28F30A7031F6E82A950.
Main153 SHA256 32EB84751D715AB909600B63B43C4DD4D928368FC71E7299B83E01B6DB539DED.
Views153 SHA256 DCC64C9265F9D0551FD5638398804BADF05088160DE3D820AE46DB00571F8DCA.
Other two Nu files remain identical to115/148.

White assessment/review-whitebox/capability-116-human-build-final.md: exactly2
fresh Nu calls, one peer: invalid theme before native writes and dry peer with
forbidden logs rejects before final success view. Root/config/parent session
intact, hashes before/after equal, no failed test attempts. Source review caught
and verified the fixed prepublication resource projection. No SDK run by reviewer.

Black assessment/review-blackbox/human-build116-final-report.md: exactly2 Nu153
calls passed (dry-readonly and applied). Seven complete MRPack entries match96
by name/length/SHA256, five index rows and pack hash verified, full human view
and preserved source/session/config checks passed. Legacy96 did not retain the
Build.Log property, so no exact96 log equivalence is claimed;95 provides auxiliary
original evidence for Go progress sequences. Under color never the rendered log
has no ESC but retains literal [1A[J remnants, progress repetition and stage/GUID
paths. This visible legacy-tool log quality issue is recorded explicitly, not
misreported as clean output or a new business failure. Sixty original evidence
files, seven legacy source inputs and ten package files remain unchanged.
No further calls, publications, installed updates or Explorer launches occurred.
