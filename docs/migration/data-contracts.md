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
