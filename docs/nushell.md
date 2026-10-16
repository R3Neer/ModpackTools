# Nushell usage

Requires Nushell 0.116.2 or newer and a complete native Windows x64 package.
Use the native setup executable with an explicit application root, Nu executable,
archive and trusted SHA256; see the README. It does not edit profiles or plugins.

Add the generated control import to your Nu configuration:

```nu
use 'D:/Minecraft/Tools/ModpackTools-App/control/Nushell/modpack.nu' *
```

Open a fresh session after installation, update or rollback. Each import binds a
coherent module/executable pair. Existing sessions keep their imported version;
cleanup conservatively retains versions while old sessions might still use them.

## Human and machine use

```nu
modpack content list
modpack doctor
modpack content list --type mod --no-human | where side == client
modpack project list --no-human | select id name
```

Normal use renders R3CLI to stderr. `--no-human` returns native Nu values with no
human output. The adapter transports typed requests to the native executable and
validates the result, including echoed command, exit status and presentation shape.
It never starts PowerShell. Errors become Nu errors with a stable ID and hint.

| Command | Nu machine value |
|---|---|
| `--version` | `{version: string}` |
| `project list` | project list |
| `project current/use` | record containing `active_project` |
| `content list` | inventory item list |
| `content search/versions` | returned search/version data |
| `category list` | returned category data |
| Other commands | command data record; some successful operations return `{}` |

`--json` is accepted as a transport option. Direct native executable users receive
a schema1 JSON envelope; the Nu command unwraps it into pipeline values.

## Project selection

The imported command validates `modpack project use <id>` before setting
`MODPACKTOOLS_PROJECT` for the current Nu session. It preserves custom environment
conversions. `--project <id>` or `-p <id>` chooses only that invocation and does not
replace session selection. Help and failed commands never change selection.

## Options and confirmation

Use lowercase `--long-options`, `-h`, `-V`, `-p`, `-n` and `-y`. Single-dash words
such as `-Project` and uppercase `--Project` are rejected. Global project/color
options may appear before or after the command. Quoted paths remain argument values.

Content removal and self-update preview before confirmation, defaulting to No.
A closed/non-terminal input cancels. Explicit `--yes` still validates the preview
and binds approval to it; a changed plan or installation requires a fresh review.
Doctor repair supports environment and project steps with separate confirmations.
Use `--dry-run` where supported to preview without applying project changes.

## Executable resolution

A package module locates `modpacktools.exe` beside its `Nushell` directory.
`MODPACKTOOLS_CORE_PATH` is an optional development override, not an installation
requirement. Missing files and invalid overrides fail explicitly. The generated
installed control module chooses its version automatically; keep that import.
