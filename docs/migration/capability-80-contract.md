# Capability80: complete native category creation contract

Bounded typed ID/name/order; preserve legacy case-insensitive ID regex (uppercase
IDs are accepted despite the historical lowercase wording), reserved and duplicate
errors. Default name uses uppercase invariant for blank/whitespace. Automatic
order is max explicitly supplied Order plus10, ignoring missing Order fields;
no explicit orders means10, including negative values. Invalid definitions fail
before publication; automatic Int32 overflow receives a clear bounded error.

Use the full project transaction, protect source expectation, preserve unknown PSD
and prepare a complete CategoryListView before commit. Read staged metadata back,
project inventory once with that metadata and return a view whose Project is the
original protected project, never a temporary stage. Reuse truthful postcommit
cache refresh with explicit now. Existing PowerShell created directly; native
adds full rollback/output-preparation protection. Public category-list wire and
Nu parser/dispatch follow separately (category creation does not return only a
transaction in the original).

Finite black3: missing/explicit order mixture, uppercase ID with whitespace name,
case-insensitive duplicate error. Compare complete PSD/view/cache. Parent covers
unknown nested empty data and output preparation failure before any publication;
white adds two complementary controls. No provider/Packwiz/real packs.

Preservation refinement: duplicate protection uses the imported PSD dictionary's
culture-aware case-insensitive key equality, so it also rejects a cultural alias
that the original ordinal search could miss and then overwrite through its setter.
This deliberately avoids losing an existing manual definition. Automatic-order
member projection reuses the same supported table/array wrappers as category
reading; missing Order is omitted, incompatible scalar projections fail explicitly.

Accepted product77c240b. Frozen Core107 SHA256
C027414A008C9F7E3EDD1AEECAA89C42325B5464134566C2DF85378C58A5B626.
Locked restore, format, strict Release and AOT pass without warnings. Native
defaults include wrapper negative-order/name fallback, unknown empty data and a
pre-publication output failure. The failure surfaces through the existing typed
Transaction.IoFailed wrapper; the test checks its primary detail and source/cache
preservation. Black3/3 and white2 pass. Reports under assessment/review-blackbox/category-create80-final-report.md
and assessment/review-whitebox/capability-80-category-creation.md.
Black compares full diagnostic view (nine Project fields, all rows), PSD and cache
with CreatedUtc exactly equal to supplied Now; original additional technical
project fields are retained as evidence, not claimed equal to the diagnostic DTO.
White verifies Int32 automatic overflow protects root/cache and explicit order
plus actual cache IO failure returns Applied=true/diagnostic with original root.
No public CLI/Nu acceptance is claimed here; that is capability81.
