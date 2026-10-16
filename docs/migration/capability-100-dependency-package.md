# Capability 100: prepare a verified dependency package

Internal scope: trusted caller release metadata plus an offline ZIP produce a
new unactivated staging directory containing only packwiz.exe and its license,
with exact SHA-256/length evidence for both. This is preparation, not installation:
no final managed path/configuration/PATH changes and no child executable invoked.
Downloading, publication/recovery and doctor --fix are separate capabilities.
Shipping metadata pins the same Packwiz archive/executable/revision as
dependencies.psd1. Test-only inspector can supply its fixture's trusted hashes;
no shipping command accepts a package-provided hash as an authority.

Archive hash is verified while its original handle excludes write/delete sharing.
Existing ArchivePreflight bounds actual central directory bytes/count before BCL
ZIP object allocation. Entry paths, duplicates, links, lengths and expanded-byte
budgets are validated before materializing selected leaves; safe unrelated files
are ignored as in the original. Selected output uses CreateNew, bounded copying,
incremental hashing and disk flush. Reserve a new GUID directory exclusively.
Wrong input hash creates no stage; wrong executable hash cleans the owned stage.
Cancellation is terminal. Cleanup never recursively deletes unknown entries;
unverifiable partial/changed files retain the stage with its exact path and a
terminal RetainStage diagnostic. Caller owns successful stage lifecycle and must
revalidate its file evidence before later publication. No installer claims yet.

Finite acceptance: black valid local archive (real tool bytes, private synthetic
license, unrelated entry ignored) and wrong archive hash, compared against original
Install-MpManagedPackwiz using its supported DependencyManifestOverride/private
config. Original publishes into its private managed directory and has a repeated
idempotent fast path; native100 only prepares an unactivated directory. Those are
explicit scope differences, not proof of native installation/idempotence. White
two complementary cases: trusted archive containing wrong executable hash and
unsafe ZIP path. Parent focused preparation test preserves source hashes.

Accepted product checkpoint: `b1d4222`. Locked restore, scoped format verification,
Release and Native AOT publish pass without warnings. Core126 SHA-256:
95E6411D49FE12DCA9606E312056265C0513525EAB85E6CDCF0A33D12204B55A.
Parent offline valid/mismatch test passes with existing real Packwiz bytes, private
synthetic license and paths with spaces; payload never invoked. Black2/2 scenarios
pass against frozen originals, with correct file hashes/lengths and unchanged
input mtimes/bytes; target/hint/details error wording exceptions explicit (native
names original input, details currently null rather than redundant expected hash).
White2/2 controls pass for executable mismatch/verified cleanup and unsafe path
before staging; archives/foreign sentinels preserved, handles released. Source-only
review of partial IO retained stage is not a claim of dynamic fault injection.
Evidence external assessment/native-dependency-package126-results.md,
review-blackbox/packwiz-package100-final-report.md and
review-whitebox/capability-100-dependency-package-final.md. Black harness parser
setup failure before any native call preserved in its report; final2calls passed.
No existing public command dispatch changed and no unrelated corpus rerun.
No source/repository/profile relocation,
activation, live installation, executable invocation, external network or push.
