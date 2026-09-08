# ModpackTools

[![CI](https://github.com/R3Neer/ModpackTools/actions/workflows/ci.yml/badge.svg)](https://github.com/R3Neer/ModpackTools/actions/workflows/ci.yml)

ModpackTools is a Windows CLI for managing multiple Minecraft Java modpacks built
with [Packwiz](https://packwiz.infra.link/). PowerShell 7 remains the canonical
engine, while ModpackTools 3.3 adds a Nushell adapter that exposes the same `modpack`
command with structured pipeline results.

It inventories pack content, resolves compatible Modrinth releases and declared
dependencies, applies multi-item changes as transactions, and exports checked
`.mrpack` files.

```text
current project -> resolve -> validate -> plan -> apply -> checked MRPack
```

## Highlights

- Creates new Packwiz projects or adopts existing ones without replacing technical
  metadata.
- Manages mods, resource packs and shader packs across named projects.
- Resolves add and update batches together, including transitive Modrinth
  dependencies, pins and installed constraints.
- Reads declared dependencies from Fabric, Quilt, Forge and NeoForge JAR metadata.
- Applies project changes atomically with staging, rollback, recovery journals and
  concurrent-change detection.
- Keeps editorial categories and names separate from Packwiz's technical data.
- Manages enabled resource-pack priority through Default Options.
- Diagnoses environment, project structure, dependency health, verification
  coverage and `.mrpack` freshness.
- Uses R3CLI for consistent help, status output, colour and error presentation.
- Exposes schema-versioned JSON output for automation and a native Nushell wrapper
  for structured pipelines.

Dependency validation checks requirements declared by Modrinth and supported mod
manifests. It does not launch Minecraft or prove runtime compatibility. When
metadata is missing or ambiguous, ModpackTools reports **verification incomplete**.
Use `--strict` when incomplete verification must block an operation.

## Requirements

- Windows.
- PowerShell 7 or newer. It remains the execution engine even when ModpackTools is
  called from Nushell.
- Packwiz. The installer can download the pinned, hash-verified Windows build.
- A directory whose direct children are Packwiz projects.

Nushell is optional. The adapter is tested in CI with Nushell 0.115.1. Git and a
standard Minecraft installation are optional integrations reported by `doctor`.

## Install or update

Download and extract the
[latest release](https://github.com/R3Neer/ModpackTools/releases/latest), or clone
this repository.

### PowerShell

```powershell
.\Install-ModpackTools.ps1
```

The installer can start under Windows PowerShell 5.1. If PowerShell 7 is missing,
it can offer to install it with WinGet and relaunch itself. It installs the module,
verifies the bundled R3CLI adapter, locates or installs Packwiz, and normally runs
`modpack doctor --fix`.

For unattended installation:

```powershell
.\Install-ModpackTools.ps1 -Force -NonInteractive
.\Install-ModpackTools.ps1 -Force -NonInteractive -SkipDoctor
```

The installer also accepts `-InstallPath <directory>` for an explicit user module
target. Re-running it updates the installed copy while preserving a customized
installed theme.

If Nushell is available on `PATH`, the PowerShell installer also installs the Nu
adapter and adds one marked, idempotent `use ... main` block to `config.nu` without
replacing the rest of the user's configuration.

### Nushell

The release also includes a Nushell-native installer entry point. Its lower-case
kebab-case filename follows Nushell's recommended naming convention for multi-word
commands:

```nu
nu ./install-modpack-tools.nu
nu ./install-modpack-tools.nu --force --non-interactive
nu ./install-modpack-tools.nu --force --non-interactive --skip-doctor
```

The Nu installer delegates installation to the canonical PowerShell installer, so
there is still one installation implementation and one verification path. Open a
new Nushell session afterward so the generated `config.nu` import is loaded.

See [Nushell adapter](docs/nushell.md) for the complete shell boundary and returned
data shapes.

## Self-update

From ModpackTools 3.2 onward, the tool can update itself:

```powershell
modpack --version
modpack --version --offline
modpack --update --check
modpack --update
modpack --update --yes
```

`--version` prints the loaded version and can announce a newer stable release.
Successful checks are cached for 24 hours. `--offline` skips network and cache.

`--update --check` checks without installing. `--update` previews the selected user
installation and version change before confirmation; `--yes` skips the prompt. The
update path validates the official asset URL, SHA256 digest, package identity and
replacement installation. A failed post-replacement verification restores the
previous installation.

`modpack update` continues to mean **update modpack content**. Self-update is the
global `modpack --update` option.

## Nushell compatibility and structured output

PowerShell remains the source of truth for command parsing, project resolution,
transactions and help. The Nu adapter does not reimplement those rules.

Every Nu invocation requests ModpackTools' JSON channel internally:

```text
Nushell modpack
   -> PowerShell bridge
      -> R3CLI human output on stderr
      -> one JSON envelope on stdout
   -> parsed native Nushell value
```

That means normal interactive output remains visible while the pipeline receives
records or lists:

```nu
modpack inventory --type mod
| where side == client
| sort-by name

modpack search sodium
| where downloads > 1_000_000

modpack versions sodium
| select number version installed
```

The structured value, not the rendered R3CLI text, is also the result eligible for
`$ans.last` according to the user's Nushell configuration.

The PowerShell command can request the same machine channel directly:

```powershell
modpack inventory --json
modpack inventory --json --no-human
```

`--json` is additive: human presentation stays visible while stdout receives one
schema-versioned JSON envelope. `--no-human` suppresses presentation and requires
`--json`.

The JSON envelope is authoritative for expected success or failure. The Nu wrapper
does not use a possibly stale `$env.LAST_EXIT_CODE` as a second success signal.

### Active project in Nushell

`modpack use <id>` is session-local in both shells. In Nushell, the wrapper stores
the successfully validated project ID in `MODPACKTOOLS_PROJECT`:

```nu
modpack use vanilla-plus
modpack status
modpack inventory
modpack doctor
```

Each PowerShell bridge process inherits that environment variable and ModpackTools
uses it as the initial active project when the module is imported. The adapter does
**not** rewrite later commands and does not inject `--project` arguments.

An explicit `--project <id>` or documented positional project selector therefore
continues to be parsed only by PowerShell and overrides the inherited active project
for that one command. `modpack use --help` does not change the Nu session selection.
Closing the Nu session clears the active project.

## Quick start

Configure the directory that contains your projects:

```powershell
modpack config set root "D:\Minecraft"
modpack doctor
```

Create a project, or adopt an existing Packwiz project:

```powershell
modpack new vanilla-plus --name "Vanilla Plus" --minecraft 1.21.1 --loader fabric
modpack init existing-pack --path "D:\Minecraft\Existing Pack"
```

Select it and inspect it:

```powershell
modpack use vanilla-plus
modpack status --full
modpack inventory
modpack doctor
```

Search, preview and apply content changes:

```powershell
modpack search sodium
modpack add sodium lithium --dry-run
modpack add sodium lithium
modpack update --all --dry-run
modpack update --all
```

Build after reviewing project health:

```powershell
modpack doctor
modpack build
modpack diff
```

The same `modpack` syntax is available through the Nushell adapter; query commands
then return native structured values.

## Project selection

Every command that operates on an existing project accepts `--project <id>`.
This makes one-off commands explicit without changing the session selection:

```powershell
modpack inventory --project vanilla-plus --unclassified
modpack doctor --project vanilla-plus
modpack build --project vanilla-plus
```

`status`, `inventory`, `build` and `diff` also retain their documented positional
project shorthand. PowerShell remains the only parser for both forms.

## Command map

Run `modpack --help` for the generated overview and
`modpack <command> --help` for complete syntax and examples.

| Area | Command | Purpose |
|---|---|---|
| Projects | `list` | List registered projects. |
| Projects | `use` | Select the active project for the current shell session. |
| Projects | `status` | Show a project summary or full configuration. |
| Projects | `new` | Create a new Fabric, Quilt, Forge or NeoForge Packwiz project. |
| Projects | `init` | Adopt an existing Packwiz project. |
| Content | `inventory` | Inspect and filter mods, resource packs and shaders. |
| Content | `search` | Search compatible Modrinth content. |
| Content | `add` | Resolve and install a Modrinth batch with dependencies. |
| Content | `versions` | List compatible releases for installed Modrinth content. |
| Content | `update` | Update selected content or eligible managed content. |
| Content | `remove` | Remove installed content with optional cascade/autoremove. |
| Content | `pin` / `unpin` | Prevent or permit automatic version changes. |
| Content | `classify` | Create, list, remove and assign editorial mod categories. |
| Content | `side` | Set client, host or both distribution metadata. |
| Content | `resource` | Enable, move or disable resource packs through Default Options. |
| Build | `doctor` | Diagnose and safely repair the environment or selected project. |
| Build | `build` | Validate and export a checked `.mrpack`. |
| Build | `diff` | Compare project content with the latest build. |
| Configuration | `config` | Read or change the project root and Packwiz executable. |

Global presentation options are `--colour auto|always|never` and `--ascii`.
Machine-output options are `--json` and `--no-human`.

## Inventory and selectors

Inventory filters can be combined:

```powershell
modpack inventory --type mod --category performance --side client
modpack inventory --type resourcepack --state active
modpack inventory --source local --search graves
modpack inventory --unclassified
modpack inventory --check
```

Supported filters include `--type`, `--category`, `--unclassified`, `--side`,
`--source`, `--state` and `--search`.

Commands accept names, stable IDs, filenames and, where documented, saved numbers.
Search results, inventory entries, categories and version lists have separate,
project-bound number scopes. Invalid or ambiguous selectors cancel the whole batch.

## Removal, dependency resolution and pins

```powershell
modpack remove sodium iris --dry-run
modpack remove fabric-api --cascade --autoremove --dry-run
modpack add sodium lithium --category performance --dry-run
modpack update sodium --to 2
modpack pin sodium lithium
modpack unpin sodium
```

`remove` blocks operations that would break declared required dependencies unless
`--cascade` includes affected dependents. `--autoremove` can clean dependencies left
unused by that removal while retaining explicit content, pins, local files and
shared dependencies. Incomplete metadata blocks speculative automatic cleanup.

The resolver handles batches together, backtracks across compatible releases,
detects cycles and minimizes unnecessary changes. Automatic downgrades require
`--allow-downgrade`; pins must be explicitly removed before an operation may change
them.

See [Dependency engine and project transactions](docs/dependency-engine.md) for the
full resolution and policy contract.

## Atomic project changes

Mutating operations use one transaction layer:

- preparation happens outside the pack;
- project state is fingerprinted before preparation and commit;
- concurrent changes abort rather than being silently merged;
- failed commits restore modified bytes and remove only transaction-created files;
- pending journals are recovered before another write;
- `--dry-run` validates the same plan without committing it.

Cloud-backed OneDrive directories are supported. Real symbolic links, junctions and
linked paths remain blocked because their targets cannot be rolled back safely.

## Health and build freshness

```powershell
modpack doctor
modpack doctor --project vanilla-plus --details
modpack doctor --fix --dry-run
modpack doctor --fix --yes --allow-downgrade
modpack build --strict
```

Doctor distinguishes known required issues, incomplete verification and stale build
artifacts. A successful health summary means no known required issue was found; it
is not a promise that Minecraft will launch.

`doctor --fix` can repair regenerable indexes and dependency changes with a
determinate solution. It does not guess editorial metadata, remove content, install
Minecraft or rebuild a stale artifact.

Builds validate, optionally refresh Packwiz, export in isolation and compare the
result against the prepared project before replacing the previous artifact.
Generated `.mrpack` files live under `dist/` and are never a project source of truth.

## Resource packs and Default Options

When `config/defaultoptions-common.toml` is present, ModpackTools can edit enabled
resource-pack order transactionally:

```powershell
modpack resource enable "Fresh Animations" --position 1
modpack resource move A B C --position 2
modpack resource disable A B
modpack add <resource-pack> --enable --position 1
```

Position 1 is the highest visible Minecraft priority. Batch moves preserve selector
order and avoid duplicate entries.

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
|---|---|
| Minecraft, loader, technical versions, files, hashes, provider IDs and managed sides | Packwiz files |
| Stable project ID, display/build identity and optional Java version | `.modpack/project.psd1` |
| Categories, display overrides, notes and explicit/transitive intent | `.modpack/metadata.psd1` |
| Enabled resource-pack order | `config/defaultoptions-common.toml` |
| Generated installable artifacts | `dist/` (output only) |
| Installer dependency versions and hashes | `dependencies.psd1` |

## Output, errors and themes

R3CLI owns CLI layout, status symbols, help rendering, colour handling and expected
error presentation. ModpackTools supplies domain data and a small product theme.
Expected failures keep stable error IDs and actionable hints.

The machine-readable boundary is separate from presentation: JSON goes to stdout,
while human rendering goes to stderr when `--json` is active. This keeps automation
parseable without sacrificing interactive output.

Place a complete personal theme at
`$env:LOCALAPPDATA\ModpackTools\theme.toml`. `NO_COLOR` and redirected output disable
ANSI under automatic colour detection.

See [R3CLI integration](docs/r3cli-integration.md),
[Nushell adapter](docs/nushell.md) and [Error design](docs/error-design.md).

## Troubleshooting

- **A new terminal loads an old version:** inspect `Get-Module -ListAvailable
  ModpackTools | Select Version,Path`, update unintended duplicates, then verify
  `modpack --version` in a new terminal.
- **The active project disappeared:** `modpack use` is session-local. Select it
  again or pass `--project <id>`.
- **Nushell shows an old wrapper after updating:** open a new Nu session so the
  installed module import in `config.nu` is reloaded.
- **Doctor says verification is incomplete:** use `doctor --details`; incomplete
  coverage is not equivalent to a successful loader launch.
- **Doctor says the `.mrpack` is stale:** run `modpack build` and install the new
  file from `dist/`.
- **A synced folder is rejected as linked:** normal OneDrive Files On-Demand folders
  are supported; real symlinks and junctions are intentionally blocked.

## Development and validation

Run the deterministic PowerShell suite with Pester 4.10.1:

```powershell
Import-Module Pester -RequiredVersion 4.10.1
Invoke-Pester -Script .\Tests
```

CI also installs Nushell 0.115.1, parses both Nu entry points, exercises the bridge
contract, verifies `modpack use` across separate PowerShell children, runs the Nu
installer, checks the generated `config.nu`, and invokes the installed wrapper in a
fresh Nushell process.

Further documentation:

- [Nushell adapter](docs/nushell.md)
- [Dependency engine and project transactions](docs/dependency-engine.md)
- [R3CLI integration](docs/r3cli-integration.md)
- [Error design](docs/error-design.md)

## License

[MIT](LICENSE)
