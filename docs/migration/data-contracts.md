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
