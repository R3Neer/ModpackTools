# Nushell adapter

ModpackTools keeps PowerShell as its canonical engine and exposes a thin Nushell
adapter from `Nushell/modpack.nu`. The PowerShell installer imports this module into
the user's `config.nu` when Nushell is available on PATH. The repository also ships
`install-modpack-tools.nu`, so installation can be launched directly from Nushell.

## Installation from Nushell

From a clone or extracted release package:

```nu
nu ./install-modpack-tools.nu
```

The Nu installer forwards to the canonical PowerShell installer and supports the
same operational switches needed for normal installation:

```nu
nu ./install-modpack-tools.nu --force --non-interactive
nu ./install-modpack-tools.nu --force --non-interactive --skip-doctor
nu ./install-modpack-tools.nu --install-path 'C:\Users\me\Documents\PowerShell\Modules\ModpackTools'
```

The lower-case kebab-case name follows Nushell's recommended convention for
multi-word command names. The installer prefers `pwsh`. On Windows it can fall back
to Windows PowerShell so the existing bootstrap path can offer to install
PowerShell 7 when interactive. Open a new Nu session after installation so the new
`use ... main` block in `config.nu` is loaded.

## Behaviour

The Nu command is still named `modpack` and accepts the same command tokens as the
PowerShell CLI. The wrapper always enables ModpackTools' machine-readable JSON
channel internally. PowerShell writes the normal R3CLI presentation to stderr and
one JSON envelope to stdout; the wrapper parses the envelope and returns a native
Nushell value.

Typical commands therefore remain pleasant interactively while also composing as
structured pipelines:

```nu
modpack inventory --type mod
| where side == client
| sort-by name

modpack search sodium
| where downloads > 1_000_000

modpack versions sodium
| select number version installed
```

The returned value, not the human R3CLI presentation, is the pipeline result and
therefore the value eligible for `$ans.last` according to the user's Nushell
configuration.

## PowerShell JSON options

The PowerShell entry point also accepts the global options directly:

```powershell
modpack inventory --json
modpack inventory --json --no-human
```

`--json` is additive: normal R3CLI presentation remains visible, but moves to
stderr so stdout is clean JSON. `--no-human` suppresses that presentation and is
valid only together with `--json`.

Every machine response is a schema-versioned envelope. Success responses contain
`ok`, `command`, `arguments`, and `data`. Expected failures contain a structured
`error` record and still preserve the normal terminating PowerShell error contract.

## Returned Nu shapes

The adapter unwraps the most useful payload for common query commands:

- `modpack list` returns a list of project records.
- `modpack inventory` returns a list of inventory records.
- `modpack search` returns a list of search-result records.
- `modpack versions` returns a list of compatible-version records.
- `modpack classify list` returns a list of category records.
- `modpack doctor`, `diff`, and `build` return records.
- Mutating commands return their transaction record when one is available.
- Other commands return the structured `data` record.

The JSON envelope remains the stable PowerShell machine boundary. Nu's unwrapped
shape is deliberately ergonomic for pipelines and can evolve independently while
preserving the envelope schema.

## Active project

PowerShell keeps `modpack use` state inside its process, while the Nu adapter starts
a new PowerShell child for each invocation. The adapter therefore stores the
validated selection in the Nu session environment as `MODPACKTOOLS_PROJECT`.

Each bridge process inherits that environment variable. On import, ModpackTools
uses it as the initial active project, so later commands do not need to be rewritten
or given an injected `--project` option. Explicit `--project <id>` and documented
positional project selectors continue to be interpreted only by the canonical
PowerShell parser and override the inherited active project for that command.

```nu
modpack use vanilla-plus
modpack status
modpack inventory --type mod
modpack doctor
```

`modpack use <id>` first lets PowerShell validate the project and return the active
project in the structured response. Only after that succeeds does the adapter update
`MODPACKTOOLS_PROJECT`. Help requests do not change the selection. Closing the Nu
session clears it, matching PowerShell's session-scoped behaviour rather than
silently creating persistent configuration.

## Bridge boundary

`Nushell/Invoke-ModpackBridge.ps1` receives an argv array as JSON on stdin. It does
not build or evaluate a PowerShell command string. This keeps spaces, URLs and
other selectors as process arguments instead of turning quoting rules into an
accidental second parser.

The bridge reserves stdout for the JSON envelope. Human presentation and expected
diagnostics use stderr. The Nu wrapper redirects only bridge stdout to a temporary
capture file, leaving stderr attached to the terminal so R3CLI output remains live
and terminal-aware.

Both sides of the bridge pin machine traffic to UTF-8. PowerShell explicitly uses
UTF-8 for stdin and stdout, and Nushell explicitly decodes a redirected byte stream
before parsing JSON. This is required for real project data containing non-ASCII
names or filenames; Nushell deliberately preserves an external stream as `binary`
when implicit UTF-8 decoding cannot be guaranteed.

The JSON envelope is authoritative for success and expected failure. The wrapper
does not compare a valid envelope with `$env.LAST_EXIT_CODE`: Nushell can preserve
a caller's native exit status across expression scopes, which would make an old
failure look like a failure of a later successful ModpackTools call. A missing or
malformed envelope still fails the bridge contract, and an envelope with `ok=false`
becomes one native Nu `error make`.
