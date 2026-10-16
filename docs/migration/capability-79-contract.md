# Capability79: transactional category removal

Resolve bounded category selectors before ownership, lazily load numeric category
references and deduplicate canonical IDs in request order. Recheck definitions on
stage. Reject assignments unless explicitly cleared; retain unrelated fields and
remove only now-empty overrides. Preserve unknown metadata and nested empty data.
Stable ordinal-ignore-case ordering of up to eight CategoryInUse mod IDs improves
the nondeterministic hashtable error detail; other error fields retain parity.
Malformed non-table overrides fail explicitly before writes.

Use the complete transaction and physically external configuration guards. Share
the established metadata-only postcommit cache refresh with classification,
retaining supplied clock, actual Applied and diagnostic-on-cache-failure. No JAR,
provider or Packwiz work. Public CLI/Nu use transaction wire and no legacy retry.
Finite black3 public cases: in-use failure, clear batch with preserved overrides
and cache, numeric dry-run. Parent includes nested-empty preservation and expected
source; white adds two complementary controls after source review.

Source review before freeze found and closed two issues: literal unclassified now
rejects in original selector order before metadata IO; stage assignment projection
is indexed once after the first definition recheck, instead of scanning/projecting
all mods once per category. Error ordering still follows each selected category.
Provisional Core105/CLI113 are not acceptance artifacts.

Accepted product ab596e3. Final Core106 SHA256
CDCCE630ACAEE65207C353FF79CE65C5B6E12B00EA39544A40EEE3FA2D4084D3;
CLI114 647976296C4AA355367EEC957792DCB558A2F811DB2B1E471BA6D9FA861EE5F8;
Nu FED36110668D259A9F41910539661386C7F671078B370654646EC612F5072EF0.
Locked restore, formatting, strict Release and both final AOT publications passed
without warnings. Native default contracts (including original classification
clock/unknown-data regression), protocol15/version/fresh Nu passed. Black3/3 and
white2 passed; reports assessment/review-blackbox/category-remove79-final-report.md
and assessment/review-whitebox/capability-79-category-removal.md.
Black validates full PSD/cache rows/freshness and direct errors/dry-run, with the
batch clear executed via Nu PATH-empty. White verifies reserved rejection before
corrupt PSD IO/no state creation, and actual postcommit cache IO failure retains
Applied=true plus diagnostic. No live config/profile/pack, provider or Packwiz
was involved. Creation and human presentation remain separate capabilities.
