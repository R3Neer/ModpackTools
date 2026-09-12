# ModpackTools

[![CI](https://github.com/R3Neer/ModpackTools/actions/workflows/ci.yml/badge.svg)](https://github.com/R3Neer/ModpackTools/actions/workflows/ci.yml)

ModpackTools is a Windows CLI for managing Minecraft Java modpacks built with [Packwiz](https://packwiz.infra.link/).

PowerShell 7 is the canonical engine. ModpackTools also ships a Nushell adapter for the same `modpack` command: normal Nu usage keeps the R3CLI human interface, while `--no-human` exposes native structured results for pipelines.

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

Version 4 deliberately replaces the complete 3.x command vocabulary. There are no
compatibility aliases: review `modpack --help` before updating scripts.

### Self-update

```powershell
modpack --version
modpack self-update --check
modpack self-update
modpack self-update -y
```

`--version` / `-V` prints the loaded version locally without network or cache access.

`modpack self-update` previews and verifies the replacement installation before switching to it. A failed post-replacement verification restores the previous installation. Use `self-update --check` to query without installing.

`modpack content update` updates **modpack content**. `self-update` updates the tool.

## Five-minute workflow

Set the directory containing your projects:

```powershell
modpack config set root "D:\Minecraft"
modpack doctor
```

Create a project:

```powershell
modpack project create vanilla-plus --name "Vanilla Plus" --minecraft 1.21.1 --loader fabric
modpack project register existing-pack --path "D:\Minecraft\Existing Pack"
```

Select and inspect it:

```powershell
modpack project use vanilla-plus
modpack project status
modpack content list
modpack doctor
```

Preview before mutating:

```powershell
modpack content search sodium
modpack content add sodium lithium -n
modpack content update --all -n
```

Apply and build:

```powershell
modpack content add sodium lithium
modpack content update --all
modpack doctor
modpack build
modpack diff
```

The same command grammar is available from Nushell.

## Command map

Use `modpack --help` or `modpack -h` for the generated overview and
`modpack <command> --help` or `modpack <command> -h` for full syntax, notes and examples.

The normal workflow is:

1. **Inspect:** `project status`, `content list`, `content versions` and `doctor` show the current
   project and any known or unverifiable requirements.
2. **Preview:** add `--dry-run` to project mutations and builds to see the complete
   batch without changing project files.
3. **Apply:** ModpackTools resolves selectors against one initial view, freezes the
   plan and commits it as one transaction.
4. **Check:** run `doctor`; use `--details` for every incomplete requirement or
   `--strict` when incomplete coverage must fail.
5. **Build:** `build` validates again, refreshes Packwiz, exports in isolation and
   verifies the result.
6. **Compare:** `diff` shows semantic differences between the project and the
   newest `.mrpack`, ignoring ZIP timestamps and compression details.

Global `--project <id>` or `-p <id>` selects a project for one command and is
accepted before or after the command:

```powershell
modpack -p vanilla-plus content list --category unclassified
modpack doctor --project vanilla-plus
modpack -p vanilla-plus build
```

An explicit selector applies only to that command and does not replace the session selection.

| Area | Command | Purpose |
|---|---|---|
| Projects | `project list/current/use/status` | Discover, select and inspect projects. |
| Projects | `project create/register` | Create a new project or register an existing Packwiz project. |
| Content | `content list/search` | Inspect installed content or search compatible Modrinth content. |
| Content | `content add/remove` | Install or remove checked transactional batches. |
| Content | `content versions/update` | List compatible releases and update managed content. |
| Content | `content pin/unpin` | Prevent or permit automatic version changes. |
| Content | `category` | Create, list, remove, assign and clear editorial categories. |
| Content | `mod set-side` | Correct whether mods are distributed to clients, hosts or both. |
| Content | `resource-pack` | Enable, move or disable resource packs through Default Options. |
| Build | `doctor` | Diagnose and safely repair the environment or selected project. |
| Build | `build` | Validate and export a checked `.mrpack`. |
| Build | `diff` | Compare project content with the latest build. |
| Configuration | `config` | Read or change the project root and Packwiz executable. |
| Configuration | `self-update` | Check for or install a stable ModpackTools release. |

Global options are `-h/--help`, `-V/--version`, `-p/--project`,
`--color auto|always|never` and `--ascii`. Common mutation shorthands are
`-n/--dry-run` and `-y/--yes`.

Machine-output options are `--json` and `--no-human`.

`modpack content search` can run without a selected project. With a project,
results are filtered by its Minecraft version and loader. Without one, search is
global; its numbered results can later be passed to `content add <number>` after
selecting a target project, where normal compatibility validation still applies.

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
modpack content list
modpack content search sodium
modpack doctor
```

Use `--no-human` when Nu itself should receive the structured result:

```nu
modpack content list --type mod --no-human
| where side == client
| sort-by name

modpack content search sodium --no-human
| where downloads > 1_000_000

modpack content versions sodium --no-human
| select number version installed
```

The machine value, not JSON text, is the pipeline result. It is also eligible for `$ans.last` according to the user's Nushell `max_last_result_size` configuration.

The same machine channel is available directly from PowerShell:

```powershell
modpack content list --json
modpack content list --json --no-human
```

In PowerShell, `--json` keeps human presentation while emitting one schema-versioned JSON envelope. `--no-human` suppresses presentation and is valid only together with `--json`. The Nu adapter handles its internal JSON transport automatically, so Nu users normally request only `--no-human`.

The envelope is authoritative for success and failure. The Nu wrapper does not layer a stale `$env.LAST_EXIT_CODE` interpretation on top of it.

See [`docs/nushell.md`](docs/nushell.md) for the adapter contract and returned shapes.

## Active project across Nushell bridge processes

The Nu adapter stores a successfully validated `modpack project use <id>` selection in `MODPACKTOOLS_PROJECT` for the current Nu session. Every short-lived PowerShell bridge process inherits it and imports ModpackTools with that project selected.

```nu
modpack project use vanilla-plus
modpack project status
modpack content list
```

An explicit `--project <id>` remains a normal PowerShell argument and overrides the inherited active project for that invocation. Closing Nushell clears the session-local selection.

`modpack project --help` does not change session state. The adapter only updates `MODPACKTOOLS_PROJECT` from an explicit successful `active_project` machine result.

## Inventory and selectors

Inventory filters compose:

```powershell
modpack content list --type mod --category performance --side client
modpack content list --type resourcepack --state active
modpack content list --source local --match graves
modpack content list --category unclassified
modpack content list --verify
```

Supported filters include:

- `--type all|mod|resourcepack|shaderpack`
- `--category <id|saved-number|unclassified>`
- `--side client|host|both|unknown`
- `--source packwiz|local|builtin|missing`
- `--state all|active|inactive`
- `--match <text>`

`content list --verify` downloads missing verification artifacts and refreshes health.
The default inventory uses local data and valid caches without new downloads.

Commands accept names, stable IDs, filenames and, where documented, saved numbers.
Search results, inventory entries, categories and version lists have separate number
scopes. Project-filtered searches and the other lists are project-bound and expire
after 24 hours; global-search numbers may be reused after selecting a project. An
invalid or ambiguous selector cancels the whole batch; duplicates are consolidated
in first-use order.

## Removing content

```powershell
modpack content remove sodium iris -n
modpack -p vanilla-plus content remove 3 7
modpack content remove fabric-api --cascade --autoremove -n
modpack content remove sodium -y
```

`content remove` accepts installed names, stable IDs, filenames and saved inventory
numbers, including local mods, resource packs and shaders. An invalid or ambiguous
selector cancels the entire batch. Use `--type mod|resourcepack|shaderpack` to
disambiguate explicit selectors; it does not restrict dependency expansion.

The command previews the complete plan before confirmation, which defaults to no.
`--yes` applies without prompting; `--dry-run` only previews. A changed file plan
is rejected before commit. All changes use the shared transaction and recovery
engine, including Packwiz index refresh and editorial metadata cleanup.

By default, removing a required dependency fails and lists the affected dependents.
`--cascade` also removes those dependents, transitively, using the declared
dependency validator. Optional recommendations do not trigger a cascade.

`--autoremove` also removes automatically installed dependencies left unused by
this operation, including orphaned cycles. It preserves explicitly installed
content, local files, pins, shared dependencies and unrelated pre-existing orphans.
Reachability conservatively retains alternatives and conditional references.
Automatic cleanup requires complete dependency verification; if metadata is
incomplete, resolve it or omit `--autoremove`. It is not a standalone global prune.

Requested or cascaded pins require `content unpin` first. Unrelated existing conflicts
remain reported; `--strict` requires a clean resulting graph and complete
verification. These checks do not prove Minecraft runtime compatibility.

Removing an active resource pack also removes its Default Options reference,
preserving the remaining priority order. Configuration files, worlds and category
definitions are retained. Built-in resources cannot be uninstalled separately from
their owning mod. If a Packwiz-managed artifact exists locally, its hash must match
the metadata before removal. The previous build is retained; run `modpack build`
to export the changed pack.

## Dependency resolution and pins

```powershell
modpack content add sodium lithium --category performance -n
modpack content update sodium --to 2
modpack content update --all --type resourcepack
modpack content pin sodium lithium
modpack content unpin sodium
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

Content add, update, pin and unpin operations, category and mod metadata batches, resource ordering,
project repairs and builds use the same transaction layer.

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
modpack -p vanilla-plus doctor --details
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
modpack resource-pack enable "Fresh Animations" --position 1
modpack resource-pack move A B C --position 2
modpack resource-pack disable A B
modpack content add <resource-pack> --enable-at 1
```

Position 1 is the highest visible Minecraft priority. For a batch, selected packs
are removed first and inserted as one ordered block. `move` requires every selected
pack to be active; disabling an inactive pack is a no-op. `content add --enable-at
<n>` installs and activates a resource-pack
batch at that position in the same transaction.

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

The Nu bridge similarly keeps R3CLI presentation on stderr and captures only its JSON transport. It forwards the parent Nu stderr terminal state so `--color auto` still behaves like an interactive command, while `NO_COLOR`, explicit color modes and redirected stderr retain their normal meaning.

Expected failures use stable error IDs and actionable hints. First-party human messages distinguish work in progress, planned changes, completed actions and actual user instructions; see [`docs/message-style.md`](docs/message-style.md).

A complete personal theme can be placed at:

```text
%LOCALAPPDATA%\ModpackTools\theme.toml
```

See [`docs/r3cli-integration.md`](docs/r3cli-integration.md) and [`docs/error-design.md`](docs/error-design.md).

## Documentation

The README is the operational overview. Detailed contracts live in focused documents:

- [`docs/nushell.md`](docs/nushell.md) — Nushell bridge, session state, global search and returned data;
- [`docs/dependency-engine.md`](docs/dependency-engine.md) — resolver and transaction policy;
- [`docs/r3cli-integration.md`](docs/r3cli-integration.md) — presentation-layer boundary and vendoring;
- [`docs/error-design.md`](docs/error-design.md) — stable expected-error design;
- [`docs/message-style.md`](docs/message-style.md) — temporal semantics for human-facing status and informational messages;
- [`docs/releasing.md`](docs/releasing.md) — versioning, packaging and automated stable-release flow;
- [`docs/releases/4.0.0.md`](docs/releases/4.0.0.md) — version 4 vocabulary, breaking changes and validation.

Command-specific syntax remains authoritative in generated CLI help:

```powershell
modpack --help
modpack content --help
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

`modpack project use` is session-local. Select it again or use `--project <id>`/`-p <id>`.

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

Stable releases are published automatically only after CI succeeds on `main`; see [`docs/releasing.md`](docs/releasing.md) for the version, notes and packaging contract.

R3CLI updates are explicit maintainer work. The pinned adapter revision and hashes live in `dependencies.psd1`; see [`docs/r3cli-integration.md`](docs/r3cli-integration.md) for the update procedure.

## Licence

MIT. See [`LICENSE`](LICENSE).
