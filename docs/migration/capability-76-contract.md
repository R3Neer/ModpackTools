# Capability 76: native category assignment/clearing batches

ContentClassificationRequest owns bounded selectors, category and dry-run. Resolve
category before numeric inventory references, both before transaction ownership.
Configuration/cache must be outside the protected root because category-reference
refresh follows the transaction. Clear uses reserved unclassified without an
unnecessary pre-lease metadata read. One stage inventory resolves targets; preserve
legacy culture-aware ID sorting/uniqueness, write editorial PSD once and verify.
Unknown override fields survive clear, which removes only Category and empty rows.
No provider, dependency graph or Packwiz process is involved.

Public assign/clear preserve --dry-run and the recognized-but-forbidden
--clear-assignments priority. Share pre-pending TransactionResponse. Refresh the
category cache after every non-dry operation, including a no-op. Prepare only its
Id/Name/Index data in CategoryList order, without a second inventory for counts.
Cache failure becomes a bounded diagnostic with the actual Applied result; late
cancellation never invents an unapplied failure. Dry-run leaves the cache intact.

Nu dispatches these complete machine commands through the common transaction
validator, preserving parent state and terminal errors. Human presentation and
category create/remove remain separate.

Finite original/native corpus: assign managed/local batch with numeric category,
numeric mod and duplicate alias; clear batch; dry-run; unknown category before bad
inventory reference. Compare complete PSD semantics, public transaction and cache
project/order/Id/Name/freshness. White complements postcommit cache failure and
preservation of unrelated override fields. No real pack or network is needed.

Accepted as product commit e4727f0. Final frozen Native artifacts: Core102
81B8241D0659002382B77DC95336EFADD1247FDB2658A0E5FE805601DDDD4F17;
CLI111 3D399D38A4FAA6F48965C87990317A8C8DC7A2F9FF1EDEE936C6B9BAAD7768FD;
Nu 92E058E3001658A0F51F96079B1D43793BA18AF4615BD088CDC82B5C6D6C95F1.
Strict Release builds, formatting and AOT publication were warning-free. Native
core defaults and content-operation regression, protocol15, version and fresh Nu
checks passed. Provisional Core101/CLI110 are not acceptance artifacts.

Independent black-box4/4 and white-box2 public controls passed. Black used seven
original calls to select four oracles: earlier positive originals exposed the
known legacy nested-empty-array writer defect, so only those originals were
recaptured with nonempty arrays. Native empty-array preservation is separately
covered. No additional matrix was silently substituted. Reports remain outside
product sources under assessment/review-blackbox/category-batch76-final-report.md
and assessment/review-whitebox/capability-76-content-classification.md.

A real Windows DOS 8.3 alias to configuration inside the protected root was
rejected before state creation with unchanged root. Parent reproduction:
assessment/test-classification-alias76.ps1. Physical canonicalization is shared
with content operations. White public controls verified actual postcommit cache
failure preserves Applied=true and clear preserves unrelated nested/empty data.
The explicit supplied clock controls cache CreatedUtc; dry-run never refreshes it.
No live installation, profile, real modpack, Packwiz process or network was used
by the category capability acceptance.
