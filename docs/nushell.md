# Nushell adapter

ModpackTools keeps PowerShell as its canonical engine and exposes a thin Nushell
adapter from `Nushell/modpack.nu`. The installer imports this module into the
user's `config.nu` when Nushell is available on PATH.

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

PowerShell keeps `modpack use` state in its process. The Nu wrapper starts a child
PowerShell process for each call, so session state is bridged through the
`MODPACKTOOLS_PROJECT` environment variable.

`modpack use <id>` first lets PowerShell validate and select the project, then
updates that environment variable in the current Nu session. Later bridge
processes inherit it. Closing the Nu session clears the selection, matching the
original session-scoped behaviour rather than silently creating persistent
configuration.

## Bridge boundary

`Nushell/Invoke-ModpackBridge.ps1` receives an argv array as JSON on stdin. It does
not build or evaluate a PowerShell command string. This keeps spaces, URLs and
other selectors as process arguments instead of turning quoting rules into an
accidental second parser.

The bridge reserves stdout for the JSON envelope. Human presentation and expected
diagnostics use stderr. The Nu wrapper converts a failure envelope into `error
make`, so failed commands cannot masquerade as valid pipeline records.
