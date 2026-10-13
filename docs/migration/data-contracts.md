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

Preserve OrdinalIgnoreCase lookup in PsdTable and every programmatic domain model.
At the literal import boundary, additionally reject a duplicate key under
InvariantCultureIgnoreCase within each individual table, matching the actual
Import-PowerShellDataFile validation. Composed/decomposed accents, ligatures and
ignored characters can collide at import even though a runtime Hashtable lookup
would distinguish them. This is a parser guard, not a new domain lookup comparer.
Nested/different tables maintain independent key sets. No Unicode normalization,
key rewriting or deduplication is allowed. Throw PsdParseException on the second
key; existing project/editorial callers map it to their structured invalid-file
errors under E02. Supported noncolliding keys retain spelling and both entries.
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

## Pure Packwiz update-target selection / next checkpoint

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
provider normalization. Several aliases on one row count as one occurrence; separate
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
