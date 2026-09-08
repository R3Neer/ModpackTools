# Nushell adapter

ModpackTools keeps PowerShell as its canonical engine and exposes a thin Nushell
adapter from `Nushell/modpack.nu`. The PowerShell installer imports this module into
the user's `config.nu` when Nushell is available on PATH. The repository also ships
`Install-ModpackTools.nu`, so installation can be launched directly from Nushell.

## Installation from Nushell

From a clone or extracted release package:

```nu
nu ./Install-ModpackTools.nu
```

The Nu installer forwards to the canonical PowerShell installer and supports the
same operational switches needed for normal installation:

```nu
nu ./Install-ModpackTools.nu --force --non-interactive
nu ./Install-ModpackTools.nu --force --non-interactive --skip-doctor
nu ./Install-ModpackTools.nu --install-path 'C:\Users\me\Documents\PowerShell\Modules\ModpackTools'
```

It prefers `pwsh`. On Windows it can fall back to Windows PowerShell so the existing
bootstrap path can offer to install PowerShell 7 when interactive. Open a new Nu
session after installation so the new `use ... main` block in `config.nu` is loaded.

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

PowerShell keeps `modpack use` state in its process, while the Nu adapter starts a
new PowerShell child for each invocation. The adapter therefore owns the Nu-session
selection explicitly.

`modpack use <id>` first lets PowerShell validate the project and render the normal
human result. Only after that succeeds does the adapter store the ID in
`MODPACKTOOLS_PROJECT` for the current Nu session. For every later command that
accepts `--project`, the adapter appends `--project <selected-id>` before invoking
the bridge. It does not inject when the command already has an explicit `--project`
or when `status`, `inventory`, `build`, or `diff` use their documented positional
project shorthand.

This means the selection does not depend on a child PowerShell process surviving:

```nu
modpack use vanilla-plus
modpack status
modpack inventory --type mod
modpack doctor
```

Each project-aware line is sent to PowerShell with the selected project explicitly.
Closing the Nu session clears the selection, matching PowerShell's session-scoped
behaviour rather than silently creating persistent configuration.

## Bridge boundary

`Nushell/Invoke-ModpackBridge.ps1` receives an argv array as JSON on stdin. It does
not build or evaluate a PowerShell command string. This keeps spaces, URLs and
other selectors as process arguments instead of turning quoting rules into an
accidental second parser.

The bridge reserves stdout for the JSON envelope. Human presentation and expected
diagnostics use stderr. The Nu wrapper redirects only bridge stdout to a temporary
capture file, leaving stderr attached to the terminal so R3CLI output remains live
and terminal-aware. The wrapper reads the child exit code immediately after that
process finishes, avoiding stale `$env.LAST_EXIT_CODE` values from earlier native
commands.

A failure envelope becomes one native Nu `error make`, so failed commands cannot
masquerade as valid pipeline records. A success envelope is still checked against
the actual bridge exit code as a protocol consistency guard.
