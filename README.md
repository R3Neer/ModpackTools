# ModpackTools

[![CI](https://github.com/R3Neer/ModpackTools/actions/workflows/ci.yml/badge.svg)](https://github.com/R3Neer/ModpackTools/actions/workflows/ci.yml)

ModpackTools is a Windows CLI for managing Minecraft Java modpacks built with [Packwiz](https://packwiz.infra.link/).

PowerShell 7 is the canonical engine. ModpackTools 3.3 also ships a Nushell adapter for the same `modpack` command: normal Nu usage keeps the R3CLI human interface, while `--no-human` exposes native structured results for pipelines.

```text
project
  │
  ├── inspect/search
  │
  ├── resolve dependencies
  │
  ├── validate one complete plan
  │
  ├── apply atomically
  │
  └── verify + export .mrpack
```

The tool is deliberately conservative: when metadata is incomplete or a mutation cannot be proven safe, it reports the uncertainty instead of pretending Minecraft compatibility has been verified.

## What it manages

ModpackTools can:

- create Packwiz projects or adopt existing ones;
- manage mods, resource packs and shader packs across named projects;
- search Modrinth and resolve compatible releases;
- resolve batches together, including transitive dependencies and installed constraints;
- read declared dependencies from Fabric, Quilt, Forge and NeoForge JAR metadata;
- pin content and control automatic version changes;
- classify content with editorial categories without rewriting Packwiz's technical metadata;
- manage enabled resource-pack priority through Default Options;
- diagnose environment, project, dependency and build health;
- apply multi-item changes transactionally with rollback and recovery journals;
- export validated `.mrpack` files;
- expose schema-versioned JSON for PowerShell automation and native Nushell values through explicit `--no-human` machine mode.

Dependency validation is metadata validation. It does **not** launch Minecraft or prove that a complete runtime will start. Missing or ambiguous evidence is reported as **verification incomplete**. Use `--strict` when incomplete verification must block the operation.

## Requirements

- Windows;
- PowerShell 7 or newer as the execution engine;
- Packwiz;
- a directory whose direct children are Packwiz projects.

Nushell is optional. The adapter is tested in CI with Nushell 0.115.1. Git and a standard Minecraft installation are optional integrations surfaced by `doctor`.

The installer can obtain the pinned, hash-verified Windows Packwiz build when needed.

## Installation and updates

Download a release or clone the repository.

### PowerShell

```powershell
.\Install-ModpackTools.ps1
```

For unattended installation:

```powershell
.\Install-ModpackTools.ps1 -Force -NonInteractive
.\Install-ModpackTools.ps1 -Force -NonInteractive -SkipDoctor
```

An explicit module destination can be supplied with `-InstallPath <directory>`.

The installer can start under Windows PowerShell 5.1. When PowerShell 7 is missing it can offer to install it with WinGet and relaunch. Installation verifies the bundled R3CLI dependency, locates or installs Packwiz, installs the module and normally runs `modpack doctor --fix`.

If Nushell is on `PATH`, the PowerShell installer also installs the Nu adapter and maintains one marked import block in `config.nu`.

### Nushell

```nu
nu ./install-modpack-tools.nu
nu ./install-modpack-tools.nu --force --non-interactive
nu ./install-modpack-tools.nu --force --non-interactive --skip-doctor
```

The Nu entry point delegates to the canonical installer rather than implementing a second installation system. Open a new Nushell session afterward so its import is loaded.

### Self-update

```powershell
modpack --version
modpack -v --offline
modpack --update --check
modpack -u
modpack --update --yes
```

`--version` / `-v` may announce a newer stable release. Successful checks are cached for 24 hours; `--offline` bypasses both network access and the cache.

`modpack --update` / `modpack -u` previews and verifies the replacement installation before switching to it. A failed post-replacement verification restores the previous installation.

`modpack update` is intentionally different: it updates **modpack content**. Self-update is the global `--update` / `-u` option.

## Five-minute workflow

Set the directory containing your projects:

```powershell
modpack config set root "D:\Minecraft"
modpack doctor
```

Create a project:

```powershell
modpack new vanilla-plus --name "Vanilla Plus" --minecraft 1.21.1 --loader fabric
```

or adopt an existing Packwiz pack:

```powershell
modpack init existing-pack --path "D:\Minecraft\Existing Pack"
```

Select and inspect it:

```powershell
modpack use vanilla-plus
modpack status --full
modpack inventory
modpack doctor
```

Preview before mutating:

```powershell
modpack search sodium
modpack add sodium lithium --dry-run
modpack update --all --dry-run
```

Apply and build:

```powershell
modpack add sodium lithium
modpack update --all
modpack doctor
modpack build
modpack diff
```

The same command grammar is available from Nushell.

## Command map

Use `modpack --help` or `modpack -h` for the generated overview and `modpack <command> --help` or `modpack <command> -h` for full syntax, notes and examples.

| Area | Command | Purpose |
| --- | --- | --- |
| Projects | `list` | List registered projects. |
| Projects | `use` | Select the active project for the current shell session. |
| Projects | `status` | Show project configuration and state. |
| Projects | `new` | Create a Fabric, Quilt, Forge or NeoForge Packwiz project. |
| Projects | `init` | Adopt an existing Packwiz project. |
| Content | `inventory` | Inspect and filter installed content. |
| Content | `search` | Search Modrinth globally or with project compatibility filters. |
| Content | `add` | Resolve and install one dependency-aware batch. |
| Content | `versions` | List compatible releases for installed Modrinth content. |
| Content | `update` | Update selected content or eligible managed content. |
| Content | `remove` | Remove content with dependency protection and optional cleanup. |
| Content | `pin` / `unpin` | Block or permit automatic version changes. |
| Content | `classify` | Manage editorial categories and assignments. |
| Content | `side` | Set client, host or both distribution metadata. |
| Content | `resource` | Enable, move or disable resource packs through Default Options. |
| Health / build | `doctor` | Diagnose and safely repair known issues. |
| Health / build | `build` | Validate and export a checked `.mrpack`. |
| Health / build | `diff` | Compare current project content with the latest build. |
| Configuration | `config` | Read or change root and Packwiz configuration. |

Global command aliases are `-h` for `--help`, `-v` for `--version` and `-u` for the self-update `--update` option.
Global presentation options are `--colour auto|always|never` and `--ascii`.
Machine-output options are `--json` and `--no-human`.

## Project selection

`modpack use <id>` selects a project for the current shell session:

```powershell
modpack use vanilla-plus
modpack inventory
modpack doctor
```

Every command that operates on an existing project also accepts an explicit `--project <id>`:

```powershell
modpack inventory --project vanilla-plus --unclassified
modpack doctor --project vanilla-plus
modpack build --project vanilla-plus
```

`status`, `inventory`, `build` and `diff` retain their documented positional shorthand as well.

An explicit selector applies only to that command and does not replace the session selection.

`modpack search` is the exception that can also run without any selected project. With an active or explicit project, results are filtered by that project's Minecraft version and loader. Without one, search is global; its numbered results can later be passed to `modpack add <number>` after a target project is selected, where normal compatibility validation still applies.

## Nushell: one command, two presentation modes

The Nu adapter does not reimplement ModpackTools. It preserves PowerShell as the only parser and domain engine, then uses the JSON machine channel internally as the shell boundary:

```text
Nushell `modpack`
       │
       ▼
PowerShell bridge
       │
       ├── R3CLI human output ──> stderr
       │
       └── JSON envelope ───────> captured transport
                                  │
                   ┌──────────────┴──────────────┐
                   ▼                             ▼
             normal Nu use                --no-human
             human display              native Nu value
```

Normal interactive usage consumes the transport envelope silently and leaves only the R3CLI presentation visible:

```nu
modpack inventory
modpack search sodium
modpack doctor
```

Use `--no-human` when Nu itself should receive the structured result:

```nu
modpack inventory --type mod --no-human
| where side == client
| sort-by name

modpack search sodium --no-human
| where downloads > 1_000_000

modpack versions sodium --no-human
| select number version installed
```

The machine value, not JSON text, is the pipeline result. It is also eligible for `$ans.last` according to the user's Nushell `max_last_result_size` configuration.

The same machine channel is available directly from PowerShell:

```powershell
modpack inventory --json
modpack inventory --json --no-human
```

In PowerShell, `--json` keeps human presentation while emitting one schema-versioned JSON envelope. `--no-human` suppresses presentation and is valid only together with `--json`. The Nu adapter handles its internal JSON transport automatically, so Nu users normally request only `--no-human`.

The envelope is authoritative for success and failure. The Nu wrapper does not layer a stale `$env.LAST_EXIT_CODE` interpretation on top of it.

See [`docs/nushell.md`](docs/nushell.md) for the adapter contract and returned shapes.

## Active project across Nushell bridge processes

The Nu adapter stores a successfully validated `modpack use <id>` selection in `MODPACKTOOLS_PROJECT` for the current Nu session. Every short-lived PowerShell bridge process inherits it and imports ModpackTools with that project selected.

```nu
modpack use vanilla-plus
modpack status
modpack inventory
```

An explicit `--project <id>` remains a normal PowerShell argument and overrides the inherited active project for that invocation. Closing Nushell clears the session-local selection.

`modpack use --help` does not change session state. The adapter only updates `MODPACKTOOLS_PROJECT` from an explicit successful `active_project` machine result.

## Inventory and selectors

Inventory filters compose:

```powershell
modpack inventory --type mod --category performance --side client
modpack inventory --type resourcepack --state active
modpack inventory --source local --search graves
modpack inventory --unclassified
modpack inventory --check
```

Supported filters include `--type`, `--category`, `--unclassified`, `--side`, `--source`, `--state` and `--search`.

Commands accept names, stable IDs, filenames and, where documented, saved result numbers. Inventory entries, categories, versions and project-filtered search results use separate project-bound number scopes. A search made with no active or explicit project stores a global number scope whose results may be reused later by `add` in a selected project. Ambiguous or invalid selectors cancel the complete batch instead of applying a partial interpretation.

## Dependency resolution, removal and pins

```powershell
modpack add sodium lithium --category performance --dry-run
modpack update sodium --to 2
modpack remove sodium iris --dry-run
modpack remove fabric-api --cascade --autoremove --dry-run
modpack pin sodium lithium
modpack unpin sodium
```

The resolver works on the batch as a whole. It can backtrack across compatible releases, enforce installed constraints, resolve transitive requirements, detect cycles and minimize unnecessary changes.

`remove` refuses to break known required dependencies unless `--cascade` includes affected dependents. `--autoremove` can remove dependencies left unused by that operation while preserving explicit content, pins, local files and shared requirements. Incomplete metadata blocks speculative automatic cleanup.

Automatic downgrades require `--allow-downgrade`. Pinned content must be explicitly unpinned before an operation may change it.

See [`docs/dependency-engine.md`](docs/dependency-engine.md).

## Transaction model

Mutating commands share one transaction layer:

```text
resolve
  ↓
validate complete plan
  ↓
stage outside project
  ↓
re-check project fingerprint
  ↓
commit
  ↓
verify / rollback if required
```

Important guarantees:

- preparation occurs outside the pack;
- project state is fingerprinted before preparation and again before commit;
- concurrent changes abort instead of being silently merged;
- failed commits restore modified bytes and remove only files created by that transaction;
- pending recovery journals are handled before another write;
- `--dry-run` exercises the same planning and validation path without committing.

Normal OneDrive Files On-Demand directories are supported. Real symlinks, junctions and linked paths remain blocked because their external targets cannot be rolled back safely.

## Doctor, build and verification

```powershell
modpack doctor
modpack doctor --project vanilla-plus --details
modpack doctor --fix --dry-run
modpack doctor --fix --yes --allow-downgrade
modpack build --strict
```

`doctor` separates three concepts that should not be collapsed into one green checkmark:

- known required problems;
- incomplete verification coverage;
- stale build artifacts.

`doctor --fix` repairs only issues for which ModpackTools has a determinate safe action. It does not guess editorial metadata, arbitrarily remove content, install Minecraft or silently rebuild a stale artifact.

`build` validates the project, optionally refreshes Packwiz state, exports in isolation and checks the result before replacing the previous artifact. Generated `.mrpack` files live under `dist/` and are output, never a project source of truth.

## Resource-pack priority

When `config/defaultoptions-common.toml` exists, enabled resource-pack order can be edited transactionally:

```powershell
modpack resource enable "Fresh Animations" --position 1
modpack resource move A B C --position 2
modpack resource disable A B
modpack add <resource-pack> --enable --position 1
```

Position 1 is the highest visible Minecraft priority. Batch moves preserve selector order and avoid duplicate entries.

## Project model and sources of truth

```text
MyPack/
├── pack.toml
├── index.toml
├── .modpack/
│   ├── project.psd1
│   └── metadata.psd1
├── mods/
├── config/
├── resourcepacks/
├── shaderpacks/
└── dist/
```

| Data | Source of truth |
| --- | --- |
| Minecraft, loader, technical versions, files, hashes, provider IDs and managed sides | Packwiz files |
| Stable project ID, display/build identity and optional Java version | `.modpack/project.psd1` |
| Categories, display overrides, notes and explicit/transitive intent | `.modpack/metadata.psd1` |
| Enabled resource-pack order | `config/defaultoptions-common.toml` |
| Generated installable artifacts | `dist/` |
| Installer dependency versions and hashes | `dependencies.psd1` |

## Presentation, errors and themes

R3CLI owns CLI layout, help, symbols, colour and expected-error presentation. ModpackTools owns domain data, command semantics and a small product theme extension.

When `--json` is active in PowerShell, human rendering goes to stderr and machine JSON goes to stdout. Structured output therefore remains parseable without sacrificing interactive feedback.

The Nu bridge similarly keeps R3CLI presentation on stderr and captures only its JSON transport. It forwards the parent Nu stderr terminal state so `--colour auto` still behaves like an interactive command, while `NO_COLOR`, explicit colour modes and redirected stderr retain their normal meaning.

Expected failures use stable error IDs and actionable hints. First-party human messages distinguish work in progress, planned changes, completed actions and actual user instructions; see [`docs/message-style.md`](docs/message-style.md).

A complete personal theme can be placed at:

```text
%LOCALAPPDATA%\ModpackTools\theme.toml
```

See [`docs/r3cli-integration.md`](docs/r3cli-integration.md) and [`docs/error-design.md`](docs/error-design.md).

## Documentation

The README is the operational overview. Detailed contracts live in focused documents:

- [`docs/nushell.md`](docs/nushell.md) — Nushell bridge, session state and returned data;
- [`docs/dependency-engine.md`](docs/dependency-engine.md) — resolver and transaction policy;
- [`docs/r3cli-integration.md`](docs/r3cli-integration.md) — presentation-layer boundary and vendoring;
- [`docs/error-design.md`](docs/error-design.md) — stable expected-error design;
- [`docs/message-style.md`](docs/message-style.md) — temporal semantics for human-facing status and informational messages;
- [`docs/releases/3.3.0.md`](docs/releases/3.3.0.md) — release-specific 3.3 changes and validation.

Command-specific syntax remains authoritative in generated CLI help:

```powershell
modpack --help
modpack add --help
modpack doctor --help
```

## Troubleshooting

**A new terminal still loads an old module**

```powershell
Get-Module -ListAvailable ModpackTools | Select-Object Version, Path
```

Remove or update unintended duplicate installations, then verify `modpack --version` in a new terminal.

**Nushell still has an old wrapper after an update**

Open a new Nu session so the `config.nu` import is parsed again.

**The active project disappeared**

`modpack use` is session-local. Select it again or use `--project <id>`.

**A Nu pipeline sees no structured rows**

Normal Nu usage is human mode. Add `--no-human` to the `modpack` invocation before piping it to `where`, `select`, `sort-by` or another Nu data command.

**Doctor reports verification incomplete**

Use `modpack doctor --details`. Incomplete evidence is not equivalent to either known failure or proven runtime success.

**Doctor reports a stale `.mrpack`**

Run `modpack build` and install the new artifact from `dist/`.

## Development

Run the deterministic PowerShell suite with Pester 4.10.1:

```powershell
Import-Module Pester -RequiredVersion 4.10.1
Invoke-Pester -Script .\Tests
```

CI also validates the Nushell adapter with Nushell 0.115.1, shell bridge behaviour, installation, UTF-8 machine transport, terminal-colour forwarding, vendored R3CLI integrity and cross-shell contracts.

R3CLI updates are explicit maintainer work. The pinned adapter revision and hashes live in `dependencies.psd1`; see [`docs/r3cli-integration.md`](docs/r3cli-integration.md) for the update procedure.

## Licence

MIT. See [`LICENSE`](LICENSE).