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

The Nu command is still named `modpack` and uses the same commands and domain
semantics as the PowerShell CLI, but the Nu adapter owns option spelling at the shell
boundary. Nushell long options must use lowercase double-dash spelling such as
`--project` and `--allow-downgrade`. Lowercase one-letter options are syntactically
valid at the adapter boundary; ModpackTools currently defines `-h` for `--help`,
`-u` for the global `--update`, and `-v` for `--version`. Other short forms work only
if a command explicitly defines them in the future. PowerShell-style single-dash
words such as `-Project` or `-project`, uppercase short options such as `-P`, and
uppercase long options such as `--Project` are rejected by the Nu adapter before the
request reaches PowerShell.

This restriction applies only to the Nushell adapter. The PowerShell CLI keeps its
own PowerShell invocation semantics. After validating Nu option spelling, the
adapter always enables ModpackTools' machine-readable JSON channel internally so it
can validate success, turn structured failures into Nu errors and maintain Nu
session state.

That internal JSON is transport, not presentation. In normal Nu usage the adapter
consumes it silently and only the normal R3CLI output remains visible:

```nu
modpack inventory
modpack search sodium
modpack doctor
```

Use `--no-human` when a script or Nushell pipeline actually needs the structured
machine value. Human presentation is then suppressed and the parsed JSON envelope
is unwrapped into native Nu records or lists:

```nu
modpack inventory --type mod --no-human
| where side == client
| sort-by name

modpack search sodium --no-human
| where downloads > 1_000_000

modpack versions sodium --no-human
| select number version installed
```

The machine value, not the JSON text itself, is the pipeline result. Raw JSON from
the internal bridge is never printed by the Nu adapter.

## PowerShell JSON options

The PowerShell entry point also accepts the global options directly:

```powershell
modpack inventory --json
modpack inventory --json --no-human
```

`--json` is additive in PowerShell: normal R3CLI presentation remains visible, but
moves to stderr so stdout is clean JSON. `--no-human` suppresses that presentation
and is valid only together with `--json`.

Every machine response is a schema-versioned envelope. Success responses contain
`ok`, `command`, `arguments`, and `data`. Expected failures contain a structured
`error` record and still preserve the normal terminating PowerShell error contract.

## Returned Nu shapes

With `--no-human`, the adapter unwraps the most useful payload for common commands:

- `modpack list --no-human` returns a list of project records.
- `modpack inventory --no-human` returns a list of inventory records.
- `modpack search ... --no-human` returns a list of search-result records.
- `modpack versions ... --no-human` returns a list of compatible-version records.
- `modpack classify list --no-human` returns a list of category records.
- `modpack doctor --no-human`, `diff --no-human`, and `build --no-human` return records.
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
modpack inventory
modpack doctor
```

`modpack use <id>` first lets PowerShell validate the project and return the active
project in the structured response. The adapter consumes that response internally,
updates `MODPACKTOOLS_PROJECT`, and does not print the machine record during normal
interactive use. `modpack use --no-human` exposes the record for automation. Help
requests do not change the selection. Closing the Nu session clears it, matching
PowerShell's session-scoped behaviour rather than silently creating persistent
configuration.

## Global search without an active project

`modpack search` is intentionally different from project-bound commands: it can run
before `modpack use` has selected anything. With no active project and no explicit
`--project`, ModpackTools omits Minecraft-version and loader compatibility facets and
returns a global Modrinth result set.

```nu
modpack search 'Note Block Tuner'
```

The numbered results from that project-free search are portable. After selecting a
target project, a result number can be passed to `modpack add <number>`; the normal
resolver then validates that result against the target project's Minecraft version
and loader before anything is installed.

```nu
modpack search 'Note Block Tuner'
modpack use vanilla-plus
modpack add 1
```

A search made while a project is active, or with explicit `--project <id>`, keeps the
project compatibility filters and produces project-bound references. Those numbers
cannot silently be reused against another project. The distinction is part of the
PowerShell domain contract; Nushell only transports it.

## Bridge boundary

`Nushell/Invoke-ModpackBridge.ps1` receives an argv array as UTF-8 JSON on stdin. It
does not build or evaluate a PowerShell command string. This keeps spaces, Unicode,
URLs and other selectors as process arguments instead of turning quoting rules into
an accidental second parser.

The bridge also forces UTF-8 for stdout before emitting the machine envelope. The Nu
wrapper redirects only bridge stdout to a temporary capture file and keeps R3CLI
presentation on stderr. Because Nushell forwards a child process stderr through its
own plumbing, the child PowerShell process cannot reliably infer whether the
original Nu stderr is still attached to a terminal. The adapter therefore sends the
parent `is-terminal --stderr` result with each bridge request. R3CLI `--colour auto`
uses that parent-terminal hint, still respects `NO_COLOR` and explicit
`--colour always|never`, and avoids ANSI when Nu stderr is redirected.

The wrapper decodes captured stdout explicitly as UTF-8 when Nushell exposes it as
`binary`, then parses the JSON. The JSON envelope is authoritative for success and
expected failure. The wrapper does not compare a valid envelope with
`$env.LAST_EXIT_CODE`: Nushell can preserve a caller's native exit status across
expression scopes, which would make an old failure look like a failure of a later
successful ModpackTools call. A missing or malformed envelope still fails the
bridge contract, and an envelope with `ok=false` becomes one native Nu `error make`.
