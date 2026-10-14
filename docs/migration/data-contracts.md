# Read-only data contracts

## PSD1 literal reader

`PsdReader.Parse(string) -> PsdTable`. Pure parsing, no filesystem, shell or
environment access. Failures are `PsdParseException` with offset, not executed
PowerShell. Caller bounds file bytes before loading; parser separately limits
source to 4 Mi characters, depth to 64 and nodes to 100000.

Supported: string-key tables, identifier or quoted keys, single/double-quoted
strings, doubled quotes, backtick escapes including Unicode scalar escapes,
boolean/null constants, decimal numeric literals including exponent, arrays and
comma expressions, whitespace, line and non-nested block comments. Table keys
compare OrdinalIgnoreCase; duplicate keys rejected. Numbers retain literal text
until typed extraction decides range/conversion. Immutable nodes do not own edits.
Callers retain original text/bytes; reading does not serialize it back to disk.

Explicitly unsupported at this checkpoint: numeric bare keys, casts, expressions,
interpolation, here-strings, unary-comma expressions, hexadecimal/suffixed numeric
literals. Reject these rather than changing their meaning. Add support if an
accepted real configuration/descriptor/editorial file needs it, with independent
fixtures first. Nested block comments are rejected like the PowerShell baseline.
This is not a claim to implement the entire PowerShell data-file language.

Array contract follows statement enumeration: `@(1,@(2,3))` preserves the nested
second array; `@(@(1,2))` enumerates its single statement. Comments cannot change
comma binding or make a trailing comma valid. Source trivia stays available in
the caller's original bytes for future surgical writes.

Native contract host lives only under Tests; `--inspect-psd1 <file>` exposes data
as JSON for independent differential tests. Its diagnostic numeric writer uses
decimal range, narrower than retained numeric literals; an inspector range failure
must not be reported as a parser acceptance/rejection result.

## Project catalog read contracts

`ConfigurationReader.ReadRoot(configDirectory) -> absolute registered root`:
CLI supplies config location; reader does not read global environment. Preserve
missing-root, invalid-file and missing-directory distinctions.

`ProjectReader.Read(projectDirectory) -> ProjectContext`: validate descriptor
schema/identity, extract pack name/version/index/loader fields, resolve index path
inside the root, retain display overrides and output-name fallback. TOML uses
Tomlyn syntax nodes and rejects parse diagnostics; no reflective ToModel.

`ProjectReader.Discover(root) -> ProjectCatalog` enumerates immediate registered directories,
checks duplicate IDs and sorts like legacy. Session/explicit project selection is
separate from discovery; user selection must not alter other project state.

These are logical boundaries; concrete implementations can be small static
functions/types. No interface is required solely to match the diagram.

Project Name is nullable: an absent manifest name remains null; a present null
DisplayName is the explicit empty-string override. Coalescing happens only when
constructing the default output filename. PSD1 numeric extraction uses invariant
PowerShell string conversion, integer/decimal/double literal ranges, rounding and
quoted numeric schema coercion. Diagnostic targets preserve numeric/bool types;
nonfinite doubles become strings, matching legacy JSON.

TOML parsing uses the syntax tree, but this checkpoint deliberately retains the
legacy project field grammar: unquoted bare keys, single-line basic quoted values,
and exact unquoted section headers without inline comments/internal whitespace.
Literal/multiline strings and quoted keys remain unselected for project fields,
as in legacy. Whole-document malformed TOML is E02. This compatibility policy is
separate from the reusable parser API; expanding successful syntax needs an explicit
future contract decision, not an accidental side effect of replacing regexes.

TOML preflight bounds nesting at 64 and structural tokens at 100000 outside
comments/basic/literal/multiline strings, before calling Tomlyn. Byte limits alone
are insufficient: a native probe with 5000 nested arrays terminated Tomlyn with
StackOverflowException. Such inputs now produce Project.InvalidManifest rather
than terminating the process. The scanner only imposes resource limits; Tomlyn
remains the syntax validator. Long bracket-containing strings are not deep arrays.

## Session snapshot / project current

`InvocationContext(string? ActiveProject)` is a session snapshot, not persistent
configuration. `project current` returns `data.active_project` unchanged, including
null, empty, whitespace and stale IDs. It performs no config/discovery validation.
The adapter owns changes to its parent session; a child process cannot change it.

The internal schema-1 request gains optional `context`, an object with exactly the
required `active_project` field (string or null). Absent context reads the direct
host's MODPACKTOOLS_PROJECT environment; present null active_project suppresses
that fallback. Context itself cannot be null. Unknown/duplicate nested fields,
wrong types, NUL and invalid Unicode are protocol errors. Existing byte/depth and
argument limits apply. This additive request field leaves old producers valid.

PS must send its actual script:ActiveProjectId, including null: legacy project use
updates module state without changing the environment. Nu uses the actual process
environment after Nushell's export conversions; it must omit request context.
Experiments found that explicit raw Nu values disagree with the existing bridge
for numbers, booleans, dates, lists and custom ENV_CONVERSIONS, including strings.
Relying on the inherited environment preserves these shell semantics without
reimplementing Nushell serialization or spawning PowerShell. Native current
preserves command spelling and validates forbidden
--project before extra positional arguments, like PS. Help stays with legacy
until native human presentation is implemented. No artificial domain service is
needed just to echo a typed immutable snapshot.

## Project use / selection

`ProjectSelection.EffectiveId(requestedId, activeProject) -> string` computes
selection without reading configuration: nonblank input wins without trimming;
otherwise session is used. Empty use input fails with E04 before fallback.
Missing/whitespace effective selection gives Project.NotSelected before disk IO.
`ProjectSelection.Find(catalog, effectiveId) -> ProjectContext` performs
case-insensitive matching, preserving the descriptor ID in the selected result.
Discovery/duplicate errors precede not-found once a nonblank selection exists.

Use validates forbidden global project then exactly one positional argument.
Single `--` or unknown option-looking token is a literal ID at this boundary,
matching the existing operation. It returns only active_project; no project write.
PS changes module state after validated success. Nu retains its existing
case-sensitive parent update condition: exactly project/use updates environment,
other casing still returns success without changing the parent. Help and errors
leave prior selection intact. A change to that successful quirk is not E04.

PS terminating ErrorRecord targets can retain a positional array even when legacy
JSON unwraps it to null or a scalar. The adapter preserves both representations
for Command.InvalidArguments; this presentation detail does not enter Core.
Successful native response headers must match the parsed request exactly before
any session update. Malformed responses and mismatched exits preserve prior state.

PS can receive typed positional objects rather than lexical strings. Its existing
machine context already canonicalizes public arguments as strings, so the adapter
must retain those string arguments. InvalidArguments targets preserve
original positional value types and legacy cardinality (null/scalar/array in JSON,
array in ErrorRecord). Core stays string-only; no PowerShell object conversion is
introduced into domain logic. NotFound and forbidden-context targets remain strings.
PowerShell's existing boolean comparison makes a true positional value match the
help token; preserve that fallback rather than treating it as a successful ID.

## Managed Packwiz content / first inventory checkpoint

`ManagedContentReader.Read(ProjectContext) -> ImmutableArray<ManagedContentItem>`
reads mods/resourcepacks/shaderpacks metadata, with a closed ContentKind and
immutable id/name/filename/side/version-id/metadata-path/pinned fields. Side remains
open text. No local artifacts, editorial metadata, filtering, numbered references,
health, cache, providers, presentation or writes are implied by this checkpoint.
The native test-only host exposes --inspect-managed <project-root>; public content
list remains legacy until its complete read-only flow is implemented.

Preserve duplicate IDs and discovery order. Enumerate matched files in each folder
by CurrentCultureIgnoreCase, then recurse depth-first in provider directory order.
Skip Hidden, include System-only, skip directory links while allowing cloud reparse
points without a link target. A live traversal probe confirmed that direct .NET
recursive enumeration uses a different traversal order. The pinned
[PowerShell provider source](https://raw.githubusercontent.com/PowerShell/PowerShell/v7.6.6/src/System.Management.Automation/namespaces/FileSystemProvider.cs)
confirms its sorting, recursion and hidden/link policies.

Validate each TOML once through bounded syntax nodes. Extract published fields
through cached lexical section windows and independent per-field matchers, rather
than AST values: apparent fields/headers inside valid multiline strings remain
observable in the legacy contract. Keep the historical Regex.Unescape decoder;
unsupported escapes produce structured failures rather than new successful values.
All these matchers use NonBacktracking to avoid quadratic blank-line searches.
The [official regex documentation](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-source-generators)
describes the cached NonBacktracking instance: no runtime code compilation is used.
Exact unquoted section headers
identify update.modrinth/update.curseforge; quoted or array-table headers remain
unselected. Modrinth string mod-id wins, then nonempty CurseForge string project-id,
then packwiz: plus lowercase normalized metadata-relative path. Numeric provider
IDs remain unselected. Missing/empty name uses metadata filename stem; side is
lowercased or unknown. Only Modrinth update.version supplies VersionId. Pin is a
root lowercase true with an unindented pin key. Filename/version null versus empty
remain distinct under E05.

Existing per-file limits apply. Aggregate reader budget: 64 MiB actual metadata
bytes, 100000 matched files and 100000 visited directories per invocation. Exceeding
the budget produces Content.ReadLimitExceeded with LimitsExceeded/path/hint before
unbounded collection or parsing. Enumeration failures follow legacy skip behavior;
selected-file read/parse failures are structured Content.InvalidMetadata. No mtime
shortcut or weakened transaction fingerprint is introduced.

## Local resource/shader ZIP discovery / next checkpoint

`LocalZipReader.Read(ProjectContext, ImmutableArray<ManagedContentItem>,
LocalZipReadLimits?) -> ImmutableArray<LocalContentItem>` produces immutable
id/kind/name/filename/side records, without fabricating managed version/pin fields.
Only resourcepacks and shaderpacks are included here; JAR metadata is a subsequent
capability. Enumerate immediate *.zip files, skip Hidden, include System-only and
retain provider filename ordering. Do not open ZIPs: invalid archive contents do
not change filename-based discovery. Missing directories produce empty results.

Exclude filenames known to managed items of the matching kind using the legacy
case-insensitive string comparison. Preserve its use of Filename alone even when
the corresponding metadata resides in a subdirectory; do not silently correct
path association. IDs use local:<directory>/<lowercase filename>, names use the
filename without extension. Resource side is client; shader side is unknown.
Test projection adds source=local, metadata_path=null and category=null, matching
the existing raw getter. There is no public command dispatch yet.

The default matched-file budget is 100000 across both directories, consumed before
known-file exclusion. Exceeding it gives Content.ReadLimitExceeded / LimitsExceeded
with file path and a hint. Enumeration failures retain legacy skip behavior. No
archive expansion, filesystem mutation, editorial overrides or health is implied.

The managed reader also offers a closed ContentKind overload sharing its existing
implementation. The local-ZIP inspector reads managed resources and shaders only;
unrelated malformed mod metadata does not affect this boundary. Separate per-kind
inspection calls each have their own managed budget; the all-kinds production read
retains a single aggregate budget. LocalZipReader receives observations directly
and never invokes a managed reader itself.

## Single-JAR display metadata / next checkpoint

`JarDisplayReader.Read(path, ArchiveReadLimits?) -> ArtifactDisplayMetadata`
returns immutable Name/Side plus a closed ReadStatus (Read, Missing, Unreadable,
Limited) and nullable Diagnostic. This capability reads only display metadata;
it does not discover local mods, evaluate dependencies, execute mods or compute
health. The test-only host is --inspect-jar-display <path>, projecting
name/side/status/diagnostic. Optional limits are --max-entry-bytes,
--max-central-bytes and --max-entries. Ordinary display results, including optional
metadata failures, exit zero with one JSON object and no stderr.

Read only the exact ordinal fabric.mod.json entry; duplicate entry names select
the first one, matching ZipArchive.GetEntry. Keep filename-stem/unknown fallbacks
for missing or unreadable archives/manifests. Preserve the getter's StrictMode
ordering: missing name prevents environment inspection; an applied name survives
a later missing-environment failure. Present null or empty environment means both;
client/server/* and arbitrary text retain the existing side mapping.

Preserve supported legacy JSON display coercions at an isolated typed projection
boundary: case-insensitive fields, exact duplicate-key last values, case-conflict
or empty-key failures, scalar/array/object stringification, ISO date conversion,
comments/trailing commas, root-array projection and BOM/invalid-byte behavior.
Business records remain strings/enums; no PowerShell runtime or dynamic objects
enter Core. JSON parser depth is bounded at 1024, retaining the captured unrelated
64/65/128-depth fixtures. Unsupported nonstandard JSON syntax remains unreadable
with a diagnostic, not an unannounced new interpretation.

Defaults: 4 MiB actual decompressed metadata, 64 MiB actual central-directory header
and variable-field bytes, 100000 entries. Use a small structural preflight over the
same FileStream before BCL archive materialization, with overflow-safe offsets and
EOCD/ZIP64 interpretation matching the BCL candidate. Validate actual header spans
and counts; declared sizes alone are insufficient. Use BCL descompression, never a
replacement ZIP extractor, and never expand unrelated assets. An exceeded read
budget returns Limited plus fallback/diagnostic; unreadable inputs retain an
explicit status rather than being reported as proven healthy. Large assets alone
do not impose an arbitrary archive-size failure.

Primary references: [PKWARE ZIP specification](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT),
[pinned .NET 10.0.8 archive implementation](https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/System.IO.Compression/src/System/IO/Compression/ZipArchive.cs),
and [Fabric display fields](https://docs.fabricmc.net/develop/loader/fabric-mod-json).

## Local JAR discovery / next checkpoint

`LocalJarReader.Read(ProjectContext, ImmutableArray<ManagedContentItem>,
LocalJarReadLimits?) -> ImmutableArray<LocalModObservation>` composes local content
records with typed archive read status/diagnostic. Observe *.jar in mods recursively,
preserving the existing DFS/culture ordering, Hidden/System/link policies and
explicit selected-root behavior. Do not consult managed resources/shaders at this
boundary. The test host --inspect-local-jars <project-root> projects eight existing
raw local fields plus status/diagnostic; there is no public inventory dispatch.

Build a set of physical paths relative to mods from each managed Mod item's
metadata parent and Filename. Use Path.Join rather than child-root replacement,
retaining Join-Path's leading-slash/drive-looking child semantics. Normalize path
separators and use InvariantCultureIgnoreCase for exclusion. Duplicate basenames
in different directories remain distinct. Null/empty filenames retain their
existing association behavior; do not invent missing filenames. Each candidate
that remains gets local:mods/<lowercase normalized relative path> and a relative
Filename, with Name/Side from the bounded JAR display reader. Preserve uncertainty
in a separate observation instead of fabricating managed version/pin fields.

Default limits: 100000 matched files and 100000 visited directories, counted before
known-file exclusion, and 64 MiB actual decompressed metadata across candidate
JARs. Per-archive limits still apply and retain Limited observations. An aggregate
limit aborts with Content.ReadLimitExceeded / LimitsExceeded and a path/hint before
any partial successful result. A byte observer at the archive IO boundary accounts
for actual reads; it is not a renderer callback or a second ZIP implementation.
The inspector's managed-mod read has its own existing budget; a future complete
inventory invocation must explicitly compose its budgets. No archive assets are
expanded, hashes weakened, caches persisted or files written by this checkpoint.

Archive and aggregate policy values must be positive and are validated before the
local reader returns for a missing/empty/fully-managed mods directory. Invalid
policy cannot become a success because no candidate happens to trigger archive IO.

## Editorial PSD1 metadata reader / next checkpoint

`EditorialMetadataReader.Read(ProjectContext) -> EditorialMetadata` returns an
immutable normalized PsdTable Data plus typed table views Categories, Mods,
ResourcePacks and optional Content. Missing metadata.psd1 supplies three empty
tables without creating a file. Missing required sections are added in memory;
unknown keys and nested values remain intact. This reader does not apply overrides,
classify mods, sort categories, render or write metadata. Existing literal PSD1
grammar/limits apply; no evaluator or new PowerShell process is introduced.

Preserve validation priority: Categories, Mods, ResourcePacks table shape;
ContentSchemaVersion support; Content table shape. Invalid PSD1 gives
Metadata.InvalidFile / InvalidData with path/details/hint. Invalid required section
gives Metadata.InvalidSection with the section name target. Unsupported intent
schema and invalid Content retain their respective domain IDs and null targets.
Parser/runtime wording can differ under E02; ordinary values cannot be normalized
away. Case-insensitive table membership is OrdinalIgnoreCase like literal PS
hashtables, preserving original key spelling in the normalized source.

Intent schema compatibility is type-sensitive. Numeric 1, true and string '1'
are supported; string '1.0' is not. For array schema values the legacy inequality
filters out values equal to one, then tests the filtered collection's truthiness.
Retain that observable behavior for empty/falsey singleton survivors and reject
two survivors even when both are falsey. Implement this on typed data nodes,
not by casting every schema to an integer or executing expressions.

The test-only host --inspect-editorial <project-root> emits the normalized table
as JSON, or a structured domain failure; no public command routing changes yet.

## PSD1 numeric conversion correction / next checkpoint

Literal nodes remain unchanged. At numeric extraction, overflowing integer tokens
use the legacy BigInteger-to-Double conversion rather than floating-token parsing.
Trim sign and leading zeros with a linear scan; more than 309 significant integer
digits are signed infinity, so never construct a giant BigInteger. At most 309
digits enter that conversion. Floating literals retain Double.Parse semantics.
Integer literals whose conversion is nonfinite are rejected by the data parser,
matching the legacy import failure; explicit floating exponents can still produce
infinity. A bounded conversion must not turn a non-importable PSD1 into success.
Scalar display strings retain exact Decimal integers and otherwise use invariant
G15, matching PowerShell string casts. Typed diagnostic targets retain numeric
values separately from display text. Test-only JSON keeps its existing R-format
transport; JSON rendering must not substitute display strings for finite numbers.
Validate native project catalog display/targets, signed extremes, leading zeros
and large integer work bounds before committing this shared correction.

## Mod editorial classification / next checkpoint

`ModClassification.Apply(ImmutableArray<InventoryItem>, EditorialMetadata)` is a
pure immutable transformation: no filesystem, renderer, cache or shell dependency.
InventoryItem carries physical identity/kind/name/filename/side/source, nullable
version/metadata path/pin/category/invalid category. Managed pins are true/false;
local pins are absent (null), with public defaulting deferred to its mapper.
Non-mod rows remain unchanged. Duplicate IDs each receive their matching override;
input order, all other fields and the metadata source remain unchanged.

For present Name/Side keys, cast the value with the typed PSD1 compatibility
projection; present null becomes empty and still overrides. Category membership
is OrdinalIgnoreCase, preserving requested spelling. Missing or invalid categories
become unclassified; InvalidCategory distinguishes absent from an invalid empty
request. Tables stringify as System.Collections.Hashtable, arrays join positions
with spaces, nested arrays stringify as System.Object[], and numbers use the
completed bounded display conversion. Never trim or drop null positions.

Retain successful table-array entries: singleton table wrappers support optional
keys; multiple immediate tables require every Name/Side/Category key. Project each
field group by flattening array values once, retaining null sentinels and omitting
empty-array positions. This reproduces successful legacy member enumeration at a
typed boundary without executing methods from metadata. Mixed/nested entries and
missing keys that fail the existing implementation receive E07 diagnostics.
Only overrides for IDs actually encountered are inspected; unrelated malformed
entries remain untouched. Test-only --inspect-classified-mods <root> composes
editorial read, managed mods, local discovery and pure classification, preserving
raw discovery order and field presence. It does not switch public content list.

## Default resource order reader / next checkpoint

`ResourcePackOrderReader.Read(ProjectContext, ResourceOrderReadLimits?)` returns
immutable resource ID strings from config/defaultoptions-common.toml in reversed
stored order, preserving duplicates, spelling and Unicode. Missing file, missing
exact-case key and an empty array return an empty result. This is a bounded lexical
reader matching the existing algorithm, not whole-document TOML validation: first
defaultResourcePacks occurrence wins; quotes in comments can be selected; single
quotes are ignored; nested arrays are flattened by string matching; brackets in
comments/single quotes affect the span scanner. Preserve these supported successes
instead of silently changing configuration interpretation.

The span scanner counts brackets outside double-quoted strings and skips one
escaped character inside strings. The string matcher keeps non-Singleline dot
semantics and the existing Regex.Unescape decoder. NonBacktracking matching avoids
adversarial whitespace rescanning. Unclosed array retains ResourcePack.UnclosedTomlArray
/ InvalidData with defaultResourcePacks target. E08 covers existing empty-input,
empty-quoted-ID and unsupported-decoder failures with a path/hint.

Use the existing bounded text reader and a positive default 100000 matched-ID
budget, checked before adding each result. Exceeding it gives Content.ReadLimitExceeded
/ LimitsExceeded with the configuration path and no partial successful result.
Validate policy before missing/empty early returns. Test-only
--inspect-resource-order <root> [--max-items n] emits the ID array; no project write,
inventory sorting, activation change or public command switch occurs here.

## Pure resource activation / next checkpoint

`ResourcePackActivation.Compose(ImmutableArray<InventoryItem> resources,
ImmutableArray<string> activeIds, EditorialMetadata)` returns ResourcePackView with
immutable ActiveResource and inactive physical rows. ActiveResource carries Id,
Name, nullable Filename, Packwiz/Local/Builtin/Missing source and one-based Priority;
kind=resourcepack/enabled=true are implied in its mapper. Physical records remain
unchanged and retain their source-specific absent fields.

Preserve ordered/duplicate IDs. Recognize file/ with OrdinalIgnoreCase and retain
its substring spelling, including empty Filename for file/ alone. Nonempty filenames
match the first physical row using InvariantCultureIgnoreCase; otherwise source is
Missing, while non-file and empty-file tokens use Builtin. Fallback Name is physical
Name, nonempty Filename or the full ID. Index physical filenames first-wins and
use a set of nonempty active filenames for inactive exclusion, avoiding repeated
full scans. Sort inactive rows stably by CurrentCultureIgnoreCase Name; do not
deduplicate physical rows or apply active name overrides to them.

Only matching active IDs consult ResourcePacks editorial entries. Name key presence
overrides even with null/empty; absent Name keeps the fallback. Unlike mod overrides,
present falsey entries fail legacy and receive E09. Retain empty tables and singleton
table wrappers, multi-table Name projections with every key present, array flattening,
null positions and singleton collapse. Share a small internal typed display/field
projection with mod classification; do not create another dynamic evaluator.
Test-only --inspect-resource-view <root> composes managed resource/local ZIP reads,
editorial metadata and order, emitting raw resources/active/inactive arrays. It does
not read mods/shaders, alter activation or switch public content list.

## Complete raw inventory snapshot / next checkpoint

`InventoryReader.Read(ProjectContext, InventoryReadLimits?) -> RawInventorySnapshot`
composes immutable Project/EditorialMetadata/Mods/Resources/ResourcePackView/Shaders
and local JAR observations. Preserve the legacy read/error priority: editorial,
managed mods, local JARs, mod classification, managed resources, local resource
ZIPs, managed shaders, local shader ZIPs, configured order, activation, final sorts.
Mods/shaders sort stably by CurrentCultureIgnoreCase Name. Resources and JAR
observations keep discovery order; the resource view never mutates physical rows.

One managed byte/file/directory budget spans all three kind reads; one local ZIP
matched-file budget spans resources/shaders. Local JAR aggregate/decompression and
per-archive policies remain separate, with actual bytes including failed JSON.
Validate every supplied policy before inventory IO, including absent/excluded
kinds. Internal budget-sharing overloads belong to readers; no public callbacks,
renderer or shell dependencies. Archive observations retain original physical
Item identity; future health consumers must not align them to sorted/classified
Mods by index or by ID alone because duplicate IDs are retained.

Test-only --inspect-inventory <root> emits project (eight public fields plus
index_file/index_path), normalized metadata, raw mods/resources/active/inactive/
shaders and native archive observations separately. The latter are read-status
evidence, not a complete health result. Optional managed/local/ZIP/order quota
flags support exact/over aggregate checks. No filters, references, persistent
cache, public command switch or project mutation is part of this checkpoint.

## Pure inventory selection / next checkpoint

`InventorySelector.Select(RawInventorySnapshot, RawInventoryQuery) -> InventorySelection`
returns immutable project/metadata views, selected mods/active/inactive/shader rows,
included ContentKind array, ordered filter labels and total matches. Query fields
are request strings: Type/State default to all, optional Category/Side/Source/Match
are absent by default. Explicit null Type/State binds to empty and fails, matching
the PowerShell string parameter boundary. Validate inside Select after snapshot
construction; public command integration must retain read-before-filter priority.

Preserve aliases and comparison semantics: type switch, side/source/state choice
membership and row category/side/source comparisons use InvariantCultureIgnoreCase.
Host maps to server; its label is host. Metadata category lookup is OrdinalIgnoreCase,
with cultural unclassified exemption. Match is an OrdinalIgnoreCase substring over
Name + space + Id + space + Filename (null becomes empty), not regex or wildcard.
Do not trim input. Empty optional filters are absent; whitespace is present.

Validation priority is type, side, source, state, mod/type conflict, mod/state
conflict, state/type conflict, unknown category. Retain domain IDs, categories,
targets and hints. Mod-only filters force type mod; state forces resourcepack;
source alone does not force a type. Preserve group ordering and labels in the
existing type/category/side/source/state/search order, including search= for the
public --match flag and raw lowercased source/state spellings. Included types are
always a typed collection, matching the public mapper's array normalization.

Test-only --inspect-selected-inventory <root> [--type/--category/--side/--source/
--state/--match value] emits raw groups, included_types, filters and total_matches.
It does not assign references, cache data, inspect health or change public dispatch.

## Ordered category definitions / next checkpoint

`CategoryDefinitions.Read(EditorialMetadata) -> ImmutableArray<CategoryDefinition>`
is pure and produces Key, Name and Int32 Order for every category, even when a
selected inventory contains no mods. Name defaults to Key.ToUpperInvariant only
when its field is absent; a present null becomes empty, arrays use the existing
editorial display projection and tables retain their PowerShell display spelling.
Order defaults to 1000 only when absent. Present null becomes zero, boolean maps
to 0/1, scalar strings/numbers preserve the existing bounded Int32 conversion,
including midpoint-to-even rounding. Arrays/tables as Order on a direct table are failures, including singleton arrays.

Category entries reuse the typed table-field projection with no truthy guard.
Supported singleton table wrappers retain their behavior. Multi-table Name fields
can project successfully, while projected arrays as Order fail as PowerShell does.
All category definitions are evaluated before sorting and irrespective of selected
kind. Actual method/property/cast failures receive E11 InvalidCategoryDefinition,
with the category key and metadata repair hint; unused malformed definitions are
not silently skipped at this stage. This differs from lazy matching mod overrides.

Sort by Order and CurrentCultureIgnoreCase Name. E12 adds an ordinal Key tie-break
only when both preceding comparisons tie: legacy Hashtable enumeration made this
order vary between fresh processes. Preserve all definitions; no deduplication by
linguistic key comparison and no implicit reserved-category rewrite. This ordering
will be shared by reference assignment and presentation rather than reimplemented
by adapters. No files/cache, timestamps, reference numbers or public dispatch are
changed in this checkpoint. Test-only --inspect-categories <root> emits definitions.
Member-projection clarification: categories and active ResourcePacks perform direct
entry lookup, so even a singleton array of tables enumerates each requested field
once, omits empty array contributions and collapses a single produced value.
Explicit null counts as one produced value. No produced values fails StrictMode;
it does not become a successful null. Therefore wrapped Order=@(1) succeeds as 1,
while direct-table Order=@(1) fails; wrapped empty Name/Order fails, whereas direct
Name=@() displays empty. Mod override binding first collapses a singleton table
wrapper and retains direct-table field behavior. Keep these two input boundaries
explicit instead of changing the shared literal model or unwrapping every array.
## PSD1 import duplicate-key validation / next correction

Correction from independent resource-target review: file-imported tables capture
CurrentCultureIgnoreCase for lookup and import-value duplicate rejection. The older
ordinal-lookup/invariant-import-guard assumption confused evaluated runtime literals
with Import-PowerShellDataFile's safe-value visitor. Preserve the comparers explicitly
supplied to programmatic PsdTable; only PsdReader-imported tables change. Root/nested/
array-contained tables share the culture captured at the start of that parse, even
after the caller changes culture. No Unicode normalization or key rewriting.
Separately retain OrdinalIgnoreCase lexical duplicate rejection: Turkish I/i is a
parser error even though the captured culture would distinguish those lookup keys;
I/dotless-i is an import-value duplicate only in Turkish. Independent key scopes,
original key spelling, bounded grammar and E02 caller errors remain. Validate
project sections/fields, editorial keys/fields, classification/category membership
and active resource override lookup. Primary source: [PowerShell7.6.6 SafeValues
VisitHashtable](https://raw.githubusercontent.com/PowerShell/PowerShell/v7.6.6/src/System.Management.Automation/engine/parser/SafeValues.cs).
Native rejection assertions and full differential editorial cases are required.
## Pure inventory reference assignment / next checkpoint

`InventoryReferences.Assign(InventorySelection) -> NumberedInventorySelection`
returns the unchanged Selection, ordered CategoryDefinitions, typed numbered
physical/active wrappers and immutable InventoryReference rows. References contain
one-based Index, Id, ContentKind, Name, non-null Filename, typed InventorySource and
Selector. Physical row numbers are nullable because programmatic unknown/null
categories receive no reference in legacy; active rows are always numbered.

Read every category definition after selection, even for an empty/mod-free view.
Reference sequence is category definitions in their established order, mods matching
each key by InvariantCultureIgnoreCase sorted stably by current-culture Name, then
an additional similarly sorted unclassified group, active resource order, inactive
Name order and shader Name order. Preserve duplicate row occurrences and explicitly
defined unclassified categories: those rows are referenced again in the additional
group. Do not deduplicate by ID, record value equality or filename. Group/index
mods once by cultural category and sort each group once instead of rescanning every
mod for every definition. Null categories remain unreferenced rather than failing.

Each occurrence produces a cache reference row. Visible row number is the last
assignment to that original object identity, matching Add-Member -Force; equal-valued
distinct objects keep distinct assignments. Use ReferenceEqualityComparer internally,
never expose arbitrary object keys at the public typed boundary. Numbered wrappers
retain original immutable item instances and original selected group ordering.
For resourcepack rows use nonempty Filename as Selector; all other rows use Id.
Null Filename becomes empty only in reference-cache projection, not in original rows.

No configuration resolution, timestamp, fingerprint, cache write, health inspection
or public dispatch occurs. Test-only --inspect-numbered-inventory <root> accepts the
same selection flags and emits numbered raw groups plus reference rows. Actual
persistent-cache validation/application is a separate capability and acceptance gate.
Native programmatic identity controls must distinguish equal records from repeated
identical objects and preserve the unclassified duplication, null category and
empty filename cases in addition to full file-based differential fixtures.
Inventory Name-sort clarification: the native stable Name ordering described above
is deliberate E13, rather than an assertion that legacy Sort-Object is stable.
Distinct-name ordering remains identical; equal-name rows retain their incoming
order and therefore may have different reference numbers from the unstable legacy
result. The pure assignment identity/occurrence rules themselves are unchanged.
## Atomic inventory reference-cache persistence / next checkpoint

`InventoryReferenceCache.Write(configurationDirectory, NumberedInventorySelection,
DateTimeOffset createdUtc, ReferenceCacheWriteLimits?) -> ReferenceCacheWriteResult`
serializes the existing version-1 cache at configurationDirectory/last-inventory.json.
The explicit timestamp makes persistence deterministic in tests; the caller provides
current UTC in production. Write SchemaVersion=1, CreatedUtc as UTC DateTime O text
with Z and seven fractional digits, ProjectId and Results array. Each result has
Index, Id, Kind, Name, Filename, Source and Selector with existing PascalCase names;
kind/source are the established lowercase strings. Preserve reference occurrence
order, empty arrays, original text and empty Filename. Emit UTF-8 without BOM.
JSON whitespace is not part of the private cache format; compare parsed full data.

Resolve the configuration directory to an absolute path. Validate a positive default
64 MiB serialized-byte budget before creating directories or temporary files. Use
a unique temporary file in the same directory and stream JSON through a byte-budget
wrapper; count actual output bytes before delivering each write. Close/flush all
handles before a single overwrite move. The old cache remains untouched until the
complete new representation is written; remove only this invocation's temporary
file on failure. Return success only after replacement. Concurrent writers are
last-completed-writer wins, with no mixed/truncated successful file. This cache is
regenerable and does not claim a durable project transaction or crash journal.

E14 identifies quota and IO/write failures at the cache path with a rerun/repair
hint. No partial success, source-pack mutation, health evaluation or public dispatch
change. Test-only --inspect-write-reference-cache <root> <configuration-directory>
accepts selection flags, optional --created-utc timestamp and --max-cache-bytes.
Return path and reference count only; inspect the actual resulting file separately.
Test fresh/replacement/empty/Unicode/duplicate caches against the legacy cache reader,
exact/over budgets, invalid policy before effects, locked destination preservation,
cleanup and handle release. Cache decoding/number resolution is a separate checkpoint.

## Bounded reference-cache read and pure numeric lookup / completed checkpoint

`ReferenceCacheReader.Read(configurationDirectory, ReferenceCacheReadLimits?) ->
CachedInventorySnapshot?` owns an extensible JSON value, its absolute source path
and deferred typed field conversions. Default limits are 64 MiB actual file bytes,
1024 JSON depth, one million result occurrences and 64 Mi UTF-16 code units of
normalized JSON. Validate positive policy before
IO. Missing files/directories and empty input return null. Decode replacement UTF-8
with BOM detection, matching the existing reader rather than the strict request
protocol. No cache, project or configuration write occurs.
Whitespace/BOM-only input is empty. Byte/depth/normalized-output bounds are a
resource prelude, not legacy semantic checks. Results quota is checked during Read
only if Results can be projected; a missing/member failure remains deferred for
Resolve, while a quota can precede schema/expiry errors. Check normalized output
before stream writes, including quoted-token control-character expansion. The byte
budget is not a peak-memory claim: decoding, normalization and the owned JSON
representation coexist. The small-workload memory gate still requires measurement.

Validate the complete decoded tree for empty/case-conflicting property names;
exact duplicate properties keep their last value and last-occurrence position. Preserve comments, trailing
commas, single-quoted strings, unquoted property names, undefined/null, nonfinite
numbers and the legacy hexadecimal/octal/leading-dot numeric forms demonstrated
by independent probes. Statically pinned Newtonsoft token decoding feeds explicitly
constructed standard-library JSON nodes and then System.Text.Json; no PowerShell,
Newtonsoft JToken graph or reflection serializer is embedded. Nonfinite JSON values
have their legacy observable string serialization, with compatible scalar casts.
Retain unknown fields, original arrays/objects and date auto-conversion in returned
data and consumer string projections. Do not narrow caches to generated writer DTOs.
Keep typed date-token metadata, including Microsoft/ISO/constructor forms and
local/UTC/unspecified kinds. Preserve original string tokens literally; no second
permissive date parser guesses their type. Comment-only input is null. Unknown
constructors reproduce legacy omission, preserving duplicate replacement and
case/empty-key validation. Root constructors remain invalid as in the baseline.

Additional decoder bounds are four Mi lexical UTF-16 units per token and one
million allocated JSON values/containers (property names and end tokens excluded).
Check token length while reading, before the library builds an oversized string;
check value count before allocation. Inspector --max-cache-token-chars and
--max-cache-nodes expose these positive limits. Normalized output is ASCII JSON,
so counted UTF-8 bytes equal normalized characters. Check output before stream
writes; a single pending token is separately bounded. Read through a byte-budget
stream, avoiding extra input byte arrays and whole UTF-16 normalization strings.

`ReferenceCacheResolver.Resolve(snapshot, selector, projectId, nowUtc,
allowedKinds, requirePackwiz) -> CachedReference?` is pure. The caller provides
clock/project/kinds; the default allowed kinds are mod/resourcepack/shaderpack,
while an explicit empty/null collection accepts none. `ResolveFromDirectory`
recognizes the selector before reading and then composes these two operations.
The recognition pattern is the original ^[1-9][0-9]*$: one final LF is accepted,
CRLF and leading zeros are not. Nonnumeric selectors return null without cache IO.

Preserve validation order: falsey cache; schema Int32 conversion; Results null;
ProjectId whitespace; timestamp age; ordinal-ignore-case project match; every
Index conversion and then selector Int32 conversion; exactly one occurrence;
invariant-ignore-case Kind; Source only when Packwiz is required. Future dates
remain valid and exactly 24 hours is inclusive. Defer field conversion failures
until their stage; do not require unused row Id/Name/Filename/Selector/Source.
Empty Results with an overflowing selector is out-of-range without integer cast.
Range wording uses occurrence count, not greatest Index. Duplicate matches fail.

Compatibility accessors expose typed conversions computed only when needed and
retain missing/null/invalid distinctions. Cache visited header projections, not
six eager slots per unvisited row. Snapshot owns one cloned JSON root; row views
share it while scanning, and only the selected row is cloned for the returned
CachedReference, copying only its date metadata and avoiding retention of a large
snapshot by later consumers.
It offers string-field access for consumers and full extensible row data. JSON
stays at this extensible boundary, not the domain's general inventory models.

E15 maps actual member/cast failures to InvalidReferenceCache/InvalidData/path with
details and a regeneration hint, retaining their phase. Byte/depth/row budget
failures are explicit ReferenceCacheReadLimitExceeded/LimitsExceeded/path.
Test-only inspectors separately expose read data and resolved full rows; support
fixed clock, allowed kinds, Packwiz requirement and budgets. Native gates compare
the frozen 123-case baseline and separate nonstandard/encoding probes, including
priorities, full values, unchanged source bytes and handle release.
Inspector --max-cache-normalized-chars controls the explicit normalization quota;
--string-field on a resolved reference exercises later consumer string projection.
Known malformed JSON/read domain errors preserve their complete legacy diagnostic;
E15 details are added only to formerly raw field/cast failures.
Text-to-Int32 compatibility is isolated from decoding. Preserve radix word width,
explicit signs, type suffixes, KB through PB multipliers and midpoint rounding.
Unsigned hexadecimal system-converter fallback differs from signed/binary literal
parsing. Do not strip suffixes or leading zeroes before choosing signed width.
The closed scanner evaluates no expressions and accumulates complements of negative
words to avoid allocating arbitrary-size BigIntegers for a final Int32 result.

## Shared quoted numeric fields / completed correction

Reuse the reviewed text-to-Int32 compatibility at DataScalars.Integer(PsdString),
with a private nonrecursive invariant integer/double fallback. Category Order and
project descriptor SchemaVersion consume it. Preserve all other PsdValue branches,
literal parser grammar, array/member projection and diagnostic mappings. Successful
category definitions retain Key/Name/Order; actual failures remain E11 by category
key. Project UnsupportedSchema retains the original string as its diagnostic target.
Editorial metadata schema import follows its separate existing rules.
Validate the independent 130-case category baseline, project schema successes and
invalid/unsupported priorities, cache conversion regressions and warning-free AOT.

Scalar compatibility references: installed PowerShell 7.6.6 probes are the oracle;
[LanguagePrimitives conversion fallback](https://github.com/PowerShell/PowerShell/blob/v7.6.6/src/System.Management.Automation/engine/LanguagePrimitives.cs),
[tokenizer suffix/multiplier rules](https://github.com/PowerShell/PowerShell/blob/v7.6.6/src/System.Management.Automation/engine/parser/tokenizer.cs), and
[Utils binary sign-bit conventions](https://github.com/PowerShell/PowerShell/blob/v7.6.6/src/System.Management.Automation/engine/Utils.cs)
explain the separately tested literal and system-converter paths. These source rules
are implemented as a bounded scalar boundary, without embedding the PS parser.

## Pure Packwiz update-target selection / completed checkpoint

`UpdateTargetSelector.Resolve(RawInventorySnapshot, ImmutableArray<string> selectors,
string? type = "all") -> ImmutableArray<InventoryItem>` consumes an existing owned
snapshot and returns its original physical rows, preserving caller selector order.
The operation performs no IO, cache lookup, provider request, update or file write.
A composing inspector reads project/raw inventory before validating Type, matching
Resolve-ModpackUpdateSelectors failure priority. Normalize Type through the existing
inventory aliases and complete InvalidType diagnostic. Empty/default selector input
is a caller grammar error; supported nonempty strings are literal selectors here.
Numeric inventory references remain a separate preceding resolution capability.

Candidates are raw mods, physical ResourcePacks and shaders in that order, filtered
by normalized Type. Resource candidates are neither ActiveResources nor inactive
presentation rows; builtin/missing/default IDs and active-only names are excluded.
Retain mod editorial names already present in raw inventory. Match nonempty Name,
Id, Filename or metadata filename stem (remove a final .pw.toml case-insensitively)
using OrdinalIgnoreCase equality, without trimming, substring/glob matching or
provider normalization. Preserve the legacy regex $ anchor before one final LF,
retaining that LF in the stem; CRLF and multiple final LFs do not consume the suffix.
Several aliases on one row count as one occurrence; separate
rows with the same ID, path or object identity still produce ambiguity.
Only null/empty aliases are omitted; whitespace remains a literal alias. The pure
operation does not normalize paths or validate their filesystem existence.

For each selector: no occurrence -> Content.NotFound; several ->
Content.AmbiguousSelector; exactly one non-Packwiz or missing metadata path ->
Content.LocalNotUpdatable. Retain complete IDs/categories/targets/messages/hints and
matching-ID details. Validate the whole request before returning any array. Dedup
successful selections by MetadataPath with InvariantCultureIgnoreCase, retaining
first selected row and caller order, rather than deduping by content ID or record
value. Check LocalNotUpdatable for every selector before path dedup, so a later
invalid target cannot be hidden by a previous selection. Do not collapse ambiguous
candidates before matching. No category Order,
reference numbering, health or extra unused-field validation is introduced.

E16 only stabilizes the ambiguous diagnostic list: current-culture-ignore-case
sorting/uniqueness remains, but ordinal ties select a deterministic representative.
All physical occurrences still determine ambiguity and successful result ordering.
Test-only --inspect-update-targets <root> <selectors-json-array> [--type value]
returns full common physical rows with existing lowercase inspector fields, a
buffered complete result, domain exit1 or grammar exit2. Bound the inspector to the
request contract's1024 selectors; reject null/empty strings and empty/default arrays
before project IO as grammar errors. Core callers supply validated immutable input.
Compare aliases/cross-kind ambiguity/local rejection/dedup/error priority and full
rows against original PS, keeping intentional diagnostic ties separate. Validate
programmatic repeated/equal-row identity and culture distinctions natively.

## Cached selector text / completed infrastructure checkpoint

`ReferenceCacheResolver.ResolveSelector(snapshot, selector, projectId, nowUtc, policy)
-> string?` and the equivalent directory facade expose the concrete Selector string
needed by later batch consumers. Null means a nonnumeric input exclusively; a saved
null/empty Selector produces an actual empty string. A missing Selector is E15 only
when consumed. Preserve every existing cache/schema/timestamp/project/index/range/
kind/source validation and priority before reading Selector, including scanning all
Index fields after a valid first selector cast and skipping that cast for empty rows.

Share the existing selection logic through an internal borrowed CacheRowView. Public
full-row Resolve still clones selected data and retains only selected date metadata.
The text API performs the same one-level returned-array unwrap and StringField
projection using snapshot dates, but emits only an independent string; no borrowed
JSON/view/date map escapes and no full-row copy or unrelated-date scan is needed.
All complete decoder validation remains; unused fields are not newly validated.
The inspector's exact --string-field Selector path exercises this API. Other fields
and full-row paths retain ownership and behavior. Differential consumer/date cases
and the complete existing cache corpus guard the extraction before any batch wiring.

## Batch selector composition / completed checkpoint

`BatchSelectorResolver.Resolve(ImmutableArray<string> selectors,
CachedInventorySnapshot? snapshot, string projectId, DateTimeOffset nowUtc,
ReferenceLookupPolicy? policy = null) -> ImmutableArray<string>` is pure. Its directory
facade reads one owned cache snapshot lazily at the first numeric selector. Default
policy allows only mod, without requiring Packwiz; explicit null/empty AllowedKinds
remain distinct from an omitted policy. Clock, snapshot and policy are fixed for
one invocation. All-literal and empty batches neither read cache nor validate its
read limits. Repeated identical numeric inputs may reuse their resolved text inside
this invocation. No process-wide memoization, eager numeric index or inventory IO.

Inputs are validated immutable arrays with zero through1024 nonempty strings;
default arrays, null/empty elements and larger arrays are caller grammar errors
before cache IO. Empty helper batch succeeds[]; whitespace is a valid literal.
The legacy helper also accepts omitted/null root Selectors as empty; typed callers
represent that absence explicitly as an empty array. Test JSON transport requires
an actual array, rather than implicitly converting malformed root values. This
transport decision is not a change to public command grammar.

Resolve each numeric selector with the existing complete lookup/cast priorities;
retain literal selectors directly. Deduplicate the resulting strings using
OrdinalIgnoreCase, preserving first spelling and order. A stored null/empty Selector
maps to a valid empty output string; it must never fall back to the numeric input.
Buffer the entire result before returning or writing, so a failure emits no partial
selection. Existing domain errors and E15 remain. Legacy ['1',null] with no cache
fails on reference1 before null binding; the new transport validates all argument
elements first, an explicit grammar priority change, not E15.

The fixed per-invocation cache is an intentional concurrency refinement: legacy
rereads for every number and can mix cache generations during a concurrent write.
One batch now resolves against one generation; the next invocation sees replacements.
This does not bind a mutation plan or replace strong source fingerprint checks.
Independent baseline69 cases plus three separately retained prelude probes establish
casting/order/dedup/failure behavior. Test-only --inspect-batch-selectors
<configuration-directory> <project-id> <selectors-json-array> supports --now-utc,
--allowed-kinds, --require-packwiz and the existing six reader-limit options.

## Classification target selection / completed checkpoint

`ModClassificationSelector.Resolve(RawInventorySnapshot, ImmutableArray<string>
selectors) -> ImmutableArray<InventoryItem>` selects only original snapshot Mods.
Match Name, Id, Filename and metadata .pw.toml stem with OrdinalIgnoreCase, retaining
the verified one-final-LF suffix rule. No cache lookup, type choice, category Order
validation, Packwiz restriction, classification write or IO. Local mods are valid.
Multiple aliases of one occurrence count once; repeated/equal physical occurrences
remain ambiguous. Repeated successful selectors return repeated original references
in request order. ID dedup/sorting is a separate composing caller responsibility.

Reuse one internal occurrence-aware physical alias index with update-target selection;
do not share error policy or update-only metadata/source checks. Missing ->
Metadata.ModNotFound/ObjectNotFound; ambiguous -> Metadata.AmbiguousMod/InvalidArgument;
retain project/selector target, message and hint, and matching IDs. E17 stabilizes
only diagnostic current-culture-ignore-case sorted-unique ID ties using ordinal
tie order, as E16 already does for update ambiguity. Success comparison is unchanged.
Test-only --inspect-classification-targets <root> <selectors-json-array> accepts
one through1024 nonempty strings before IO, then reads project/raw inventory before
resolution. Return full physical rows, buffered result or complete structured error.
Compare baseline helper calls in order on frozen files; snapshot reuse deliberately
follows the existing per-invocation architecture, not repeated inventory discovery.

## Resource activation target selection / completed checkpoint

`ResourcePackSelector.Resolve(RawInventorySnapshot, ImmutableArray<string> selectors)
-> ImmutableArray<ResourcePackTarget>` is pure. Target fields are DefaultId, Id,
Name, nullable Filename and Active. Build active candidates in priority order,
inactive presentation candidates in their existing order, then editorial-only keys.
Inactive DefaultId is file/ plus Filename; null Filename remains null but yields
file/. Include builtin/missing active rows and metadata-only definitions. Neither
physical resource rows nor metadata stems are automatically additional aliases.
Match nonempty DefaultId/Id/Name/Filename with OrdinalIgnoreCase; retain every physical
occurrence, original target fields and repeated request outputs. A per-call alias
index avoids repeated scans. No cache/DefaultOptions check/mutation/IO in this layer.

Skip represented editorial keys using InvariantCultureIgnoreCase DefaultId membership,
updating membership after each addition. Snapshot metadata must have passed the PSD1
import uniqueness contract. Imported culture and invariant membership can nevertheless
differ: Turkish I+soft-hyphen and i are valid distinct imported keys but invariant
equivalent. E19 sorts editorial-only keys ordinally for deterministic ambiguity details,
first-invalid-key diagnostics and the representative of such equivalence groups,
retaining active/inactive ordering. This last case deliberately stabilizes a formerly
Hashtable-enumeration-dependent successful identity; it is not an errors-only delta.
Preserve original captures and compare that explicit representative separately.
Empty metadata keys and empty names remain valid outputs where selectable
by a nonempty alias. Missing Name defaults to the metadata key; existing bounded
member projection and display coercions retain null/array/table behavior.

Build/validate all candidates before any query. Preserve raw failure with empty
active/inactive candidates plus any metadata key: E18 diagnoses unavailable DefaultId
membership before reading Name, rather than inventing a new success. Other malformed
editorial entries fail E18 even if not queried. ResourcePack.InvalidSelectorMetadata/
InvalidData targets the metadata key, with details and repair hint. Existing full
inventory E09 failures retain priority before this layer. Empty view with no metadata
remains ResourcePack.NotFound. NotFound/AmbiguousSelector retain complete original
errors/hints/targets and all duplicate matching labels. Result buffers before return.
Test-only --inspect-resource-targets <root> <selectors-json-array> validates1..1024
nonempty strings before full inventory IO and writes full original PascalCase five
fields for comparison. Numeric inputs are literal here; batch references precede it.

## Default Options status and prerequisite / completed checkpoint

`DefaultOptionsDetector.Find(ImmutableArray<InventoryItem> mods) -> InventoryItem?`
returns the original first row whose Id equals modrinth:WEg59z5b or Name equals
Default Options with OrdinalIgnoreCase, or whose nonempty Filename matches the legacy
case-insensitive pattern ^defaultoptions(?:-|\.|_).*\.jar$. No trimming, Source gate,
physical JAR existence check, category validation or IO. Empty mods yields null;
default arrays are invalid caller input. Bare defaultoptions.jar does not match.
One terminal LF follows the regex dollar-anchor rule; CRLF/double LF do not match.
Use a per-invocation, lazily constructed nonbacktracking BCL matcher with current
culture, avoiding a generated invariant/static first-culture matcher. Compare fresh
PowerShell processes for Turkish cases; the old PS regex cache retains its first
compilation culture after an in-session culture switch, a separately retained
historical behavior rather than the new pure contract.

`Read(RawInventorySnapshot) -> DefaultOptionsState` derives Installed from original
Mod, ConfigPresent from one File.Exists observation of config/defaultoptions-common.toml,
and Ready from their conjunction. Directory/missing config is not a leaf. Observe
the configuration once per invocation; legacy tests it twice when installed, so
coherent status during a race is an explicit refinement, not a stress-test claim.
`AssertInstalled(DefaultOptionsState, projectId)` returns that state unchanged or
raises the original ResourcePack.DefaultOptionsRequired/NotInstalled/projectId
diagnostic and install hint. It does not require Ready/ConfigPresent. No writes.
Test-only --inspect-default-options <root> [--assert-installed true|false] [--culture name] validates
grammar before full project/inventory IO, then writes Pascal Installed, ConfigPresent,
Ready, Mod (complete common lowercase physical fields or null), and ConfigPath.

## Category cache and ID resolution / completed checkpoint

`CategorySelector.Resolve(EditorialMetadata metadata, CachedCategorySnapshot? cache,
string selector, string projectId, DateTimeOffset nowUtc, bool allowUnclassified=false)
-> string` is pure. The directory facade takes project root/ID and
configuration directory explicitly, with no descriptor/pack/full-inventory prelude.
Reserved literal unclassified checks before metadata/cache IO: allow returns canonical
unclassified; forbid preserves original selector target. Other selectors require
complete metadata-section/schema validation before any numeric cache read. Literal
selection scans category keys with OrdinalIgnoreCase, retaining original key spelling;
Name/Order/definition shape are not consulted. Empty input strings are caller grammar
errors before IO; cached null/empty Id becomes UnknownCategory with empty target,
including a defined empty key because legacy selected-key truthiness rejects it.

Numeric recognition reuses the exact reference regex. Cache has no SchemaVersion
check; falsey cache -> CategoryCacheNotFound; timestamp -> expiry; ProjectId ->
project mismatch; then every Index cast, selector cast after first valid Index,
exactly one occurrence, selected Id string cast, carried reserved handling, literal
metadata-key resolution. Empty Categories[] skips selector cast and gives
EmptyCategoryCache; Categories=null enumerates a null row and fails at Index.
Missing/unused fields remain deferred, all Index casts still execute after a match.
Preserve complete existing Metadata errors/targets/hints and24h-inclusive/future time.
E20 replaces actual raw member/cast failures with Metadata.InvalidCategoryCache,
InvalidData/cache-path/details/category-list hint; known malformed read errors retain
their original message and null details. No write/repair/partial success.

Extract one internal bounded legacy cache-file decoder with owned root/date metadata
and injected error factories, retaining all six existing ReferenceCacheReadLimits
defaults and token/date grammar. Inventory remains a distinct wrapper with unchanged
schema/Results/null/error behavior and must pass its405 blind controls. Category wrapper
uses Categories occurrence quota, defers unavailable-field failures until actual use,
and produces Metadata.CategoryCacheReadLimitExceeded/LimitsExceeded and a separate
invalid-policy diagnostic. Explicit resource limits may precede semantic validation.
Reader alone exposes full extensible JSON; resolver emits an independent string,
never cloned selected rows or borrowed JSON/date metadata. Caller clock is fixed.
Test-only --inspect-read-category-cache <configuration-directory> and
--inspect-category-id <project-root> <project-id> <configuration-directory> <selector>
support --now-utc, --allow-unclassified and the existing six reader-limit options.
Own and independent comparisons cover full read values, exact returned strings,
validation priorities/consumer casts/quota boundaries/unchanged fixtures/closed handles.

## Category list projection / completed checkpoint

`CategoryList.Prepare(EditorialMetadata) -> ImmutableArray<EditorialCategory>`
prepares immutable Id/Name/Order/ModCount rows before any physical inventory IO.
When definitions are nonempty, inspect every editorial Mods entry once, project its
Category through the established member-enumeration/string rules, and accumulate
one count per editorial entry using OrdinalIgnoreCase. Missing Category is ignored;
scalar/null/unsupported entries still fail even if unused by physical inventory.
With no definitions, do not inspect editorial assignments in this stage. Count
validation precedes Name/Order validation. Orphan assignments count; duplicated
physical IDs do not increase editorial counts. Wrapped/multi-table fields retain
their original coercions and failures. E21 supplies a contextual assignment error.

Extract definition Name/Order with existing bounded conversion and E11 diagnostics,
but sort category-list rows by Order, current-culture-ignore-case Name, then
current-culture-ignore-case Id. Do not reuse the inventory-reference category sort:
its ordinal Id rule changes ordinary category-list ordering (same Name/Order,
IDs z and ä). E22 adds ordinal ties and deterministic invalid-entry diagnostics;
it does not replace the cultural successful ordering.

`CategoryList.Compose(ProjectContext, ImmutableArray<EditorialCategory>,
ImmutableArray<InventoryItem> mods) -> CategoryListView` is pure. Always append a
canonical unclassified row with null Order, IsUnclassified=true, and a physical
occurrence count using invariant-ignore-case equality. Keep a separately defined
unclassified category, including its independent editorial count. Number all rows
one-based after sorting/appending; DefinedCategoryCount excludes only the appended
row. Defined rows omit IsUnclassified in the comparison representation.

The filesystem facade reads metadata, prepares editorial rows, reads the complete
inventory with that same validated immutable metadata, then composes the view.
Thus editorial count/definition errors precede physical inventory failures. Using
one metadata snapshot instead of legacy's second read is an explicit coherent
observation refinement under concurrent edits, not a transaction guarantee. Keep
the original public InventoryReader.Read behavior and add only an internal supplied-
metadata path. No health/provider/cache persistence/rendering belongs to this slice.
Test-only --inspect-category-view <root> [--culture name] emits full PascalCase rows,
DefinedCategoryCount and a documented common project projection. Compare original
successes, E21/E11 failures, cultural sorting, duplicate unclassified rows, editorial
versus physical counts, no-definition bypass and earlier-error priorities.

The common Project comparison keys are Id, Root, DisplayName, DisplayVersion,
OutputName, MinecraftVersion, Loader, LoaderVersion and IndexFile. Keep full original
Project captures separately; do not claim serialization of its private descriptor/
pack properties. Defined category rows have five fields; the appended row has six.

## Atomic category-reference cache persistence / completed checkpoint

`CategoryReferenceCache.Write(configurationDirectory, CategoryListView,
DateTimeOffset createdUtc, ReferenceCacheWriteLimits?) -> ReferenceCacheWriteResult`
consumes the completed typed view, without re-reading metadata/inventory. At
last-categories.json write SchemaVersion=1, UTC DateTime O CreatedUtc (Z/seven digits),
ProjectId and Categories array; each occurrence contains only Index (the supplied
ReferenceNumber), Id and Name. Preserve text, row order, duplicate IDs/unclassified,
and supplied reference numbers. Do not renumber, sort or serialize Order/count/flags.
Initialized empty programmatic arrays are valid and produce Categories=[]; default
arrays are caller errors before effects. The production view normally contains the
appended unclassified row. JSON whitespace is not part of this private cache format.

Use the existing positive default64MiB actual serialized-byte quota. Validate policy
and initialized input before effects, resolve absolute directory/path, create only
an invocation-owned CreateNew same-directory temporary, stream UTF8 without BOM and
close writer/stream before overwrite replacement. Preserve the old cache on quota,
locked-destination and IO failures; clean only the temporary owned by this invocation.
Concurrent writes are last-completed-whole-file wins, not a project transaction.
E23 identifies category write policy, quota and IO failures with contextual path.
Extract the existing inventory writer's internal atomic bounded JSON-file mechanics
with injected closed error factories. Retain its validation,16Ki pending flush sites,
byte accounting, replacement/cleanup and diagnostics; run its27 blind writer controls
plus own atomic-failure suite. No stronger durability/peak-memory claim is introduced.

Test-only --inspect-write-category-cache <root> <configuration-directory>
[--created-utc timestamp] [--max-cache-bytes n] reads a category view and returns
path/count after successful persistence. Validate inspector grammar before project
IO; writer policy is validated after this read-only view prelude, before config
effects. Compare actual full files to the legacy writer at a fixed clock and have
the old reader/resolver consume every category number with AllowUnclassified.
Cover fresh/replacement/Unicode/duplicates/empty snapshots, exact/over budgets,
invalid policy without directories, locked replacement, unrelated temporaries,
exclusive handle reopening, reader compatibility and unchanged source fixtures.
The diagnostic-only --empty-categories true flag substitutes an initialized empty
typed view after the normal read-only prelude, to compare the separate programmatic
empty-array writer contract. This is a test-host producer, not public CLI behavior.

## Strong source snapshots, changes and fingerprints / completed checkpoint

This is one protected-source infrastructure unit: an owned file-hash snapshot,
complete change detection and identities derived from that snapshot. It does not
apply changes, acquire transaction ownership, journal, recover or evaluate providers.

`SourceTree.Read(root, SourceTreeReadLimits?) -> SourceTreeSnapshot` owns an immutable
OrdinalIgnoreCase relative-path to uppercase SHA256 map plus absolute Root. Include
hidden/system files, arbitrary nested files and dist. Skip every entry named .git
using InvariantCultureIgnoreCase, before inspecting its link or counting quotas;
this includes the legacy ignorable-character spelling. Check a root link before any
entry exclusions. Reject actual root/file/directory links and junctions with the
original Transaction.LinkedPath/InvalidOperation/null-target diagnostic. Readable
cloud placeholders with no link target and ordinary hardlinks remain regular inputs.
An existing file root produces the legacy '.' key; a regular file root named .git
produces an empty map, while a directory root named .git still enumerates its children.
Empty directories have no file-hash entry. No timestamp/length shortcut is allowed.

Stream each file in64KiB chunks under FileShare.Read; count actual bytes before
hashing each chunk and close every handle on every outcome. Positive default limits
are100,000 files,100,000 directories,1TiB actual aggregate bytes and8Mi UTF16 code
units of aggregate full paths. Count included root/directories/files after .git
exclusion; root directories count once, file roots count only as files. Full-path
characters include each retained root/directory/file, bounding pending directory
paths as well as snapshot keys. Validate policy before root IO. E26 provides policy,
quota and fail-closed IO diagnostics; never return a partial/null-hash snapshot.
E27 rejects actual ordinal-ignore-case path collisions, including directories on a
case-sensitive filesystem, rather than collapsing protected paths. This preserves
ordinary Windows cases and explicitly strengthens previously lossy edge cases.
The scan is a sequence of observations, not an atomic multi-file filesystem snapshot.

`SourceTreeChanges.Compare(before, after, culture?) -> ImmutableArray<SourceChange>`
compares owned snapshots; a separate pure immutable-map overload accepts nullable
hash strings for legacy helper comparisons. Require initialized inputs. Build the
path union with OrdinalIgnoreCase uniqueness, retaining the first before spelling,
then sort with captured cultural-ignore-case order and ordinal ties. Compare hashes
ordinally/case-sensitively; missing and null remain equivalent as in the helper.
Emit full PascalCase Path/Before/After, including nulls. E24 retains all culturally
equivalent but ordinally distinct paths; the old Sort-Object -Unique can hide a real
change, so reproducing it would violate external-edit/rollback requirements.
Enumerate actual typed keys: a legacy Hashtable entry named Keys shadows .Keys and
can also hide real changes. E24 explicitly corrects that dynamic-member collision.

`SourceFingerprint.Health(snapshot, culture?)` preserves the legacy v3 text formula:
filter only root dist/* using captured current-culture-ignore-case prefix semantics,
sort remaining paths culturally then ordinally, join path=UPPERHASH with LF/no final
LF, append |validator=3, and return lowercase SHA256 of replacement-fallback UTF8.
Keep Turkish and ignorable-character matching. E25 stabilizes cultural sort ties,
which may invalidate an old health cache but must never omit a path. Retain actual
paths named Keys rather than using the legacy shadowed member, so those old
incomplete fingerprints change under E25 as well. This slice
hashes the complete tree first; future health-only scans can avoid dist byte reads
while still checking links, and cannot substitute for a protected full snapshot.

`SourceFingerprint.All(snapshot)` is a new culture-independent plan identity,
including dist and all retained paths sorted ordinally. Its canonical binary payload
starts with ASCII ModpackTools/source-snapshot/v1 plus NUL; each entry has a four-byte
big-endian UTF16-unit path length, raw two-byte big-endian UTF16 code units and the
32 digest bytes. This length framing preserves exact code units without ambiguous
separators or replacement fallback. Bind project/root identity separately: identical
source trees at different staging roots must compare equally. `TextHash.Compute`
preserves the existing lowercase SHA256/replacement-UTF8 primitive, including empty
text and malformed surrogate controls; use streaming encoding/incremental hashing.

Test-only tree inspection exposes full paths/hashes and both fingerprints; changes
inspection consumes explicit immutable hash maps; text-hash inspection consumes
UTF16-unit arrays so malformed surrogate tests do not depend on JSON string decoding.
Modes are --inspect-source-tree <root> [--culture name] [--max-tree-files n]
[--max-tree-directories n] [--max-tree-bytes n] [--max-tree-path-chars n],
--inspect-source-changes <before-json-object> <after-json-object> [--culture name],
and --inspect-text-hash <utf16-units-json-array>. Tree output has root, files,
health_fingerprint, source_fingerprint and root_hash; root_hash preserves
TextHash.Compute(snapshot.Root.ToLowerInvariant()), the legacy health-cache address.
Changes maps accept unique ordinal-ignore-case keys and null/string values;
invalid/null objects are grammar failures before operations. Diagnostic JSON input
is bounded to1Mi characters/depth16. Root identity is not part of All's content frame.
Compare actual old tree/link/hash cases, hidden/system/Git/dist/root-file behavior,
same-length/same-mtime content edits, ordinary changes and explicit E24/E25 cases.
Exercise all exact/over resource limits, policy prelude, IO failure without partial
results, closed handles, unchanged fixtures and full versus health fingerprint scope.

## General and Maven version ordering / next checkpoint

`VersionOrdering.Compare(string? left, string? right, VersionScheme scheme=General,
VersionComparisonLimits? limits=null) -> int?` is pure. Null strings bind to empty,
matching the original string parameters. Preserve the complete signed integer,
including CompareOrdinal results such as -25/32/133; null means ordering is unknown.
Validate the scheme and positive policy, bound each input's text, then use the legacy
case-sensitive INVARIANT CULTURAL equality shortcut, not ordinal equality. Canonical
accents, ignorable characters and Arabic/ASCII digit aliases can therefore compare
equal even when their text is unsupported or numeric parsing would otherwise fail.
Do not trim, normalize or impose a standards-compliant SemVer/System.Version parser.

General syntax is the exact existing numeric-component/prerelease/build pattern,
including Unicode decimal-digit recognition, an explicitly present empty prerelease,
empty prerelease components, arbitrary build metadata and the single-final-LF anchor.
Unsupported distinct text returns null. Compare numeric components lazily left then
right at each index, padding missing components with zero and stopping immediately
on a difference. Preserve arbitrary-precision integers, release above prerelease,
numeric prerelease below textual, ordinal textual comparison and component-count
ties. Do not eagerly parse a later unused malformed numeric component. E28 maps
actual required numeric parse failures; it does not turn them into unknown/success.

Maven reuses the existing Private/MavenVersion.cs algorithm through nullable/style
adaptation and explicit resource guards. Preserve invariant lowercasing, BigInteger
numbers, qualifier aliases/order/unknown raw ordinal keys, numeric/text transitions,
nested lists, zero/trailing-empty normalization and full signed results. After the
wrapper equality shortcut, parse all left then all right before comparison; Maven
must still fail on a late invalid numeric component that General can skip.
No runtime PowerShell/Add-Type, CLR version parser or new package is introduced.

Default limits are16Ki UTF16 units per input,4096 digits per consumed number,
4096 constructed Maven items per input including the root list, and Maven depth64
including root. MaximumDepth is hard-bounded to1..64 even with explicit overrides,
protecting recursive comparison; all other limits must be positive. Whole-text quota
precedes equality, an explicit resource prelude. Numeric limits apply only when a
number is actually consumed; item/depth guards precede allocation. E29 covers policy
and quota failures. No candidate lookup, range matching, graph resolution or IO.

Test-only --inspect-compare-version <left-json-string-or-null>
<right-json-string-or-null> [--maven bool] [--culture name]
[--max-version-chars n] [--max-version-digits n] [--max-version-items n]
[--max-version-depth n] emits the complete integer or null, buffering failures.
Compare original Generic/Maven results, equality aliases, nonunit signed values,
null binding, qualifier aliases, zero/nested lists, newline/build semantics and lazy
versus eager failures. Test exact/over text/numeric/item/depth bounds, invalid-policy
prelude and hard depth override rejection. Ranges are a separate later capability.

## Typed version ranges / next checkpoint

VersionRange is a closed immutable Text/Any/All/Unknown value. Text may be null;
Any/All own initialized ImmutableArray children. Evaluate(version, range, scheme,
policy) returns bool?: true satisfied, false unsatisfied, null unknown. No loader,
provider, filesystem or JSON object enters the evaluator. A separate data lowerer
reads bounded JsonElement declarations into owned range values, retaining original
root-array OR semantics and object-member getter/PowerShell truthiness semantics.
Unsupported scalar/object declarations become Unknown, never match-all.

Any accumulates unknown until a true wins; initialized empty Any is false. All
accumulates unknown until a false wins; initialized empty All is true. Both preserve
visited order and skip later exceptions after a decisive result. Object declarations
activate any before all only when the getter-returned value is truthy. Empty/null,
false,zero,empty string/array and recursively falsey singleton arrays do not activate
it. Multi-element arrays are truthy even when their elements are false/null. The
getter unwraps a singleton property array once before branch truthiness/iteration;
root range arrays are not globally flattened. Lower inactive objects to Unknown.

Text trims only the range. Null/empty/star matches all. General || is Any including
empty alternatives; whitespace-separated conditions stop immediately on unknown
or false (distinct from All). Preserve exact operator/wildcard/caret/tilde grammars,
opaque equality fallback, arbitrary-precision wildcard increment, initial Int32
caret/tilde component conversion and promotion on increment of Int32.MaxValue.
Numeric conversions occur only in visited conditions, retaining actual failures.

Maven bare versions remain recommendations/match-all. Regex interval coverage and
normalized comma-joined text are checked completely before bounds. Single-bound
bracket shape and comma counts are checked per visited interval: an earlier match
can skip a later malformed interval. Exact singleton contents are not trimmed;
two-ended bounds are trimmed, evaluated lower then upper, even if lower fails its
predicate. Preserve endpoint inclusion, unbounded endpoints and interval unions.

Range policy defaults:16Ki UTF16 units per visited text,4096 visited nodes,
10000 ordering calls,depth64. Positive policies and depth1..64 are validated before
evaluation; comparison policies are also validated once, while comparison text/
numeric/item quotas apply only when comparison is consumed. Range node/depth/text/
call guards run before the respective work; General expansions consume the same
scope. Declaration decoding independently bounds4096 nodes/depth64 before allocation,
including inactive members; this explicit input-resource prelude does not parse
semantic text/numbers or replace visited evaluation priorities. E30/E31 cover new
range policies/limits and actual bound conversion failures. Existing E28/E29 apply
to consumed version comparisons. No invented healthy result on failure.

Wildcard/initial caret/tilde bound conversion also checks the comparison policy's
MaximumNumericDigits before numeric allocation, using E30 with that property as
target; it applies only to the actually converted bound component. Evaluation text
guard precedes Trim. A match-all range need not consume or validate version text.
Diagnostic --inspect-version-range <version-json-string-or-null> <range-json>
accepts --maven/--culture, --max-range-chars/nodes/comparisons/depth,
--max-declaration-nodes/depth and the four --max-version-* comparison flags.
Diagnostic JSON is bounded to1Mi characters per argument/depth16 version/depth128
declaration; domain lowering then imposes depth64. Closed flag grammar precedes
declaration decoding, then evaluation. Emits bool/null or a buffered structured error.
Declaration Read accepts an explicit immutable member StringComparer, default
Ordinal for original ConvertFrom-Json -AsHashtable case-sensitive ordered tables.
Literal PS hashtables have a different comparer; diagnostic
--member-case-insensitive true models that declared input origin. ANY is unknown
under the JSON default and matches any only with the appropriate source comparer.
This does not change the comparator used for versions or the Any/All truth table.

## Fabric/Quilt root declarations / next checkpoint

Pure root parsing takes already-read Fabric/Quilt texts plus the declared loader.
It returns initialized owned Mods, root diagnostics and nested-path intents, with
root completeness explicit. No ZIP entries, child streams, downloads or providers
are opened here. Completeness concerns only this root parse; declared children do
not establish whole-artifact verification. Forge/TOML and archive traversal follow
separately. Loader comparison retains invariant case-insensitive matching; preserve
the original spelling for diagnostics. Truthy Quilt text takes precedence for Quilt,
and malformed selected Quilt never falls back to Fabric. Ignored Fabric contents
are not semantically decoded when Quilt wins. Exact archive names/read-byte preludes
belong to the future archive facade.

An owned private OrderedManifestValue data tree preserves String, Int64, BigInteger,
Double including nonfinite values, Boolean, DateTime, Null, Array and ordered map.
Use the pinned Newtonsoft token reader, without JToken/serializer/reflection or PS.
JSON maps use Ordinal names, allow empty and case-distinct keys, and move an exact
duplicate to its last occurrence. Strict dot access, optional index access and
Get-MpPropertyValue-style member retrieval remain distinct boundary operations.
Root pipeline singleton/array and property enumeration behavior must be characterized
from original helpers. No data tree or token-reader reference escapes into domain DTOs.
Do not reuse PSCustomObject/cache object projections for OrderedHashtable casts.

String casts occur only at the operator that needs them: null becomes empty, booleans
True/False, floating numbers G15, dates invariant PS date text, ordered objects their
runtime type name, and arrays join one level with spaces/type names for nested arrays.
Dates/nonfinite numbers remain typed for optional comparisons, aliases and ranges.
Date range leaves lower to Unknown, never canonical ISO Text. Nonfinite numeric range
leaves also remain Unknown; quoted ordinary NaN/Infinity stay strings. Shared date
constructor mechanics, if extracted, retain the seven-argument guard and require the
complete existing cache regression; the cache decoder's default profile is unchanged.

Requirements are immutable records with Target, compiled VersionRange, retained
source display, Kind, Side, Scope, Maven scheme, Source, SuggestedVersionId, Any,
All and Unless. These fields can coexist; do not replace Quilt requirement records
with an exclusive operator union. Preserve Fabric group order/mappings and Quilt
depends/breaks order, optional equality with true (not truthiness), required recursive
children, any/all getter behavior and unless conversion. Retain unknown side/kind
strings for later interpretation. No dependency satisfaction is evaluated during parse.

Alias entries own closed String/Int64/BigInteger/Double/Boolean/Date/opaque-array/
opaque-object key variants and string versions. Preserve literal-table String key
OrdinalIgnoreCase equality, last value and first key spelling. Numeric kinds stay
distinct and array/object keys preserve decoded object identity, not structural
equality or stringification. Only String keys may enter the future string-target
alias index. Nested intents preserve source order, duplicates and falsey/cast behavior;
do not add unavailable-child warnings without attempting a child read.

Preserve stage prefixes and warning order: Fabric schema checking precedes root
insertion; Quilt schema checking follows root insertion/nested assignment, so a late
missing schema can retain the root mod plus an incomplete diagnostic. Selected decode
failure retains no root mod. No supported root yields the original loader warning.
E32 explicitly repairs adapted Keys shadowing; E33 bounds reads/conversion and E34
contextualizes actual incomplete-root failures without hiding retained prefixes.

Initial parser limits:4Mi UTF16 units per input text/token,100000 decoded nodes,
JSON depth64,100000 constructed requirements,requirement depth64,100000 alias slots
and100000 nested intents. Positive policies/depth1..64 are validated before work;
whole text guards are explicit input preludes. Shared requirement budgets cover all
Any/All/Unless children, and output/recursion guards precede allocations/descent.
Keep General/Maven and existing cache cases as regressions. Native comparison must
cover complete root fields/order, nonstring alias lookup distinction, ISO/float/map
casts, selected-manifest precedence, partial prefixes, nested intents without child
effects, exact/over quotas and separately acknowledged E32 cases. Original70 blind
archives and focused white oracles remain unchanged; there is no Native47 acceptance yet.
