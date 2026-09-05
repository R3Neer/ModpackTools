# ModpackTools

[![CI](https://github.com/R3Neer/ModpackTools/actions/workflows/ci.yml/badge.svg)](https://github.com/R3Neer/ModpackTools/actions/workflows/ci.yml)

ModpackTools is a Windows PowerShell CLI for managing several Minecraft Java
modpacks built with [Packwiz](https://packwiz.infra.link/). It inventories pack
content, resolves compatible Modrinth releases and declared dependencies, applies
multi-item changes as transactions, and exports checked `.mrpack` files. Its only
public PowerShell command is `modpack`.

```text
current project -> resolve -> validate -> plan -> apply -> checked MRPack
```

## What it does

- Creates new Packwiz projects or adopts existing ones without replacing their
  technical metadata.
- Manages mods, resource packs and shader packs across named projects.
- Resolves an entire add or update batch together, including transitive Modrinth
  dependencies, pins and installed constraints.
- Reads declared dependencies from Fabric, Quilt, Forge and NeoForge JAR metadata.
- Applies project changes atomically with staging, rollback, recovery journals and
  concurrent-change detection.
- Keeps editorial categories and names separate from Packwiz's technical data.
- Manages enabled resource-pack priority through Default Options.
- Diagnoses the environment, project structure, dependency graph, verification
  coverage and freshness of the latest `.mrpack`.
- Uses R3CLI for consistent help, status output, colour and error presentation.

Dependency validation has a deliberate boundary. It checks requirements that
Modrinth and mod manifests declare; it does not launch Minecraft, execute mixins or
prove that every mod will work at runtime. When metadata is missing or ambiguous,
ModpackTools reports **verification incomplete** and states that the loader may
still reject the pack during startup. Use `--strict` when incomplete verification
must block an operation.

## Requirements

- Windows and PowerShell 7 or newer.
- Packwiz. The installer can download the pinned, hash-verified Windows build.
- A directory whose direct children are Packwiz projects.

Git and a standard Minecraft installation are optional integrations reported by
`doctor`; they are not required to manage a project.

## Install or update

From ModpackTools 3.2.0 onward, update the tool itself from PowerShell:

```powershell
modpack --version
modpack --version --offline
modpack --update --check
modpack --update
modpack --update --yes
```

`--version` prints the loaded version first, then announces a newer stable release
when one is available. Successful GitHub checks are cached for 24 hours; an expired
cache triggers a request with a three-second timeout. Connection failures leave
the local version readable without claiming that it is current. `--offline` skips
both cache and network access and prints only the version line.

`--update --check` always checks GitHub without installing. `--update` previews the
selected user installation, versions and release link, then asks for confirmation
(default no); `--yes` skips the prompt. It downloads the official release ZIP,
checks GitHub's SHA256 digest and package identity, and runs the installer in a
separate PowerShell process. The installer preserves the custom theme and verifies
the installed version, path and command help in a fresh process before discarding
its backup. A failed verification restores the previous installation.

User installation selection follows `PSModulePath`. Other visible copies are
reported; a machine-wide or versioned installation that takes precedence blocks
the update with an instruction to select the intended user installation. Open a
new PowerShell session afterward to load the new code. Self-update does not
require a project or run project repairs. `modpack update` continues to update
content inside a pack.

Older versions need the release package installer once to acquire self-update:

Download and extract the [latest release](https://github.com/R3Neer/ModpackTools/releases/latest),
or clone this repository, then run:

```powershell
.\Install-ModpackTools.ps1
```

The installer can start under Windows PowerShell 5.1. If PowerShell 7 is missing,
it offers to install it with WinGet and relaunches itself. It then installs the
module, verifies the bundled R3CLI adapter, locates or installs Packwiz, and runs
`modpack doctor --fix`.

The module is copied directly to the first existing user `ModpackTools` installation
in `PSModulePath`, or the first user directory if none exists. Re-running the installer updates that copy while preserving a
customized installed theme. For unattended installation:

```powershell
.\Install-ModpackTools.ps1 -Force -NonInteractive
.\Install-ModpackTools.ps1 -Force -NonInteractive -SkipDoctor
```

The installer also accepts `-InstallPath <directory>` for an explicit existing
user module root target. The directory must be named `ModpackTools` directly
inside a user `PSModulePath` entry. Concurrent installations share an exclusive
installation lock.

After updating an open terminal, reload the module. A new terminal will autoload it:

```powershell
Import-Module ModpackTools -Force
modpack --version
(Get-Module ModpackTools).Path
```

Users coming from 2.x should review `modpack --help`: content commands now accept
batches, `add` no longer uses the old `add mod` form, and category assignment uses
`modpack classify set <mod...> <category>`.

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

Select it for the current PowerShell session and inspect it:

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

Build only after reviewing project health:

```powershell
modpack doctor
modpack build
modpack diff
```

`build` writes the result to `dist/`. The generated artifact is checked against a
fresh export of the prepared project before it replaces the previous build.

## Normal workflow

1. **Inspect:** `status`, `inventory`, `versions` and `doctor` show the current
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

Every command that operates on an existing project accepts `--project <id>`.
This makes one-off commands explicit without changing the session selection:

```powershell
modpack inventory --project vanilla-plus --unclassified
modpack doctor --project vanilla-plus
modpack build --project vanilla-plus
```

## Command map

Run `modpack --help` for the generated overview and
`modpack <command> --help` for complete syntax and focused examples.

| Area | Command | Purpose |
|---|---|---|
| Projects | `list` | List registered projects. |
| Projects | `use` | Select the active project for this PowerShell session. |
| Projects | `status` | Show a project summary or full configuration. |
| Projects | `new` | Create a new Fabric, Quilt, Forge or NeoForge Packwiz project. |
| Projects | `init` | Adopt an existing Packwiz project. |
| Content | `inventory` | Inspect and filter mods, resource packs and shaders. |
| Content | `search` | Search compatible Modrinth content. |
| Content | `add` | Resolve and install a Modrinth batch with its dependencies. |
| Content | `versions` | List compatible releases for installed Modrinth content. |
| Content | `update` | Update selected content or all eligible managed content. |
| Content | `remove` | Remove installed content, optionally cascading and cleaning unused dependencies. |
| Content | `pin` / `unpin` | Prevent or permit automatic version changes. |
| Content | `classify` | Create, list, remove and assign editorial mod categories. |
| Content | `side` | Correct whether mods are distributed to clients, hosts or both. |
| Content | `resource` | Enable, move or disable resource packs through Default Options. |
| Build | `doctor` | Diagnose and safely repair the environment or selected project. |
| Build | `build` | Validate and export a checked `.mrpack`. |
| Build | `diff` | Compare project content with the latest build. |
| Configuration | `config` | Read or change the project root and Packwiz executable. |

Global presentation options are `--colour auto|always|never` and `--ascii`.

### Inventory and selectors

Inventory filters can be combined:

```powershell
modpack inventory --type mod --category performance --side client
modpack inventory --type resourcepack --state active
modpack inventory --source local --search graves
modpack inventory --unclassified
modpack inventory --check
```

Supported filter values are:

- `--type all|mod|resourcepack|shaderpack`
- `--category <id|saved-number>` or `--unclassified`
- `--side client|host|both|unknown`
- `--source packwiz|local|builtin|missing`
- `--state all|active|inactive`
- `--search <text>`

`inventory --check` downloads missing verification artifacts and refreshes health.
The default inventory uses local data and valid caches without new downloads.

Commands accept names, stable IDs, filenames and, where documented, saved numbers.
Search results, inventory entries, categories and version lists have separate number
scopes. They are project-bound and expire after 24 hours. An invalid or ambiguous
selector cancels the whole batch; duplicates are consolidated in first-use order.

## Removing content

```powershell
modpack remove sodium iris --dry-run
modpack remove 3 7 --project vanilla-plus
modpack remove fabric-api --cascade --autoremove --dry-run
modpack remove sodium --yes
```

`remove` accepts installed names, stable IDs, filenames and saved inventory
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

Requested or cascaded pins require `unpin` first. Unrelated existing conflicts
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
modpack add sodium lithium --category performance --dry-run
modpack update sodium --to 2
modpack update --all --type resourcepack
modpack pin sodium lithium
modpack unpin sodium
```

The resolver fixes explicitly requested versions, searches dependencies with
backtracking, detects cycles and minimizes changes to installed content. It then
prefers fewer changes to explicit items, fewer additions, newer releases and stable
ID ordering. It never resolves a conflict by removing content or changing the
project's Minecraft, loader or Java configuration.

Automatic downgrades require `--allow-downgrade`. An explicit `update --to` may
select an older release. `update --all` skips pins, and an operation that needs to
change pinned content fails with an `unpin` instruction. Packwiz's `pin` field is
the only pin source of truth.

By default, add and update block conflicts introduced or aggravated by the planned
change while retaining unrelated existing conflicts as warnings. Builds block all
known required conflicts. `--strict` additionally requires complete verification
and a clean graph.

Automatic acquisition of new dependencies currently uses Modrinth. JAR validation
works for supported loader metadata regardless of where the file came from, but
ModpackTools does not invent replacement projects when another provider has no
verifiable candidate. Local files are validated when possible and are never
replaced automatically.

See [Dependency engine and project transactions](docs/dependency-engine.md) for the
full resolution, loader and policy contracts.

## Atomic project changes

`add`, `update`, `pin`, `unpin`, category and side batches, resource ordering,
project repairs and builds use the same transaction layer.

- Preparation happens outside the pack.
- The project is locked and fingerprinted before preparation and commit.
- A changed project aborts rather than merging against a stale plan.
- A failed commit restores modified bytes and removes only files created by that
  transaction.
- A pending journal is recovered before another write begins.
- `--dry-run` prepares and validates the same operation without committing project
  files.

Cloud-backed OneDrive directories are supported. Real symbolic links, junctions
and linked paths remain blocked because their targets cannot be rolled back safely.

## Health, repair and build freshness

```powershell
modpack doctor
modpack doctor --project vanilla-plus --details
modpack doctor --fix --dry-run
modpack doctor --fix --yes --allow-downgrade
modpack build --strict
```

Doctor reports three separate facts:

| Result | Meaning |
|---|---|
| Known required issue | A declared conflict or broken required project component was found. |
| Verification incomplete | Available provider or JAR metadata cannot prove every requirement. Loader startup may still fail. |
| Latest `.mrpack` stale | The source project and newest installable artifact differ. Do not install that artifact; run `build`. |

Optional dependency recommendations are grouped by owning mod. Incomplete checks
are grouped by cause; `--details` lists every result. A success summary says no
**known** required issues were found rather than claiming that Minecraft will run.

`doctor --fix` can repair regenerable indexes and dependency changes with a
determinate resolver solution. It previews the project transaction and respects the
existing confirmation or `--yes`. It does not guess categories, reconstruct corrupt
editorial metadata, remove content, install Minecraft or rebuild a stale artifact.

Build options include:

- `--no-refresh`: use an already valid index; dependency validation still runs.
- `--strict`: block known conflicts and incomplete verification.
- `--dry-run`: prepare, export and verify without replacing project files or builds.
- `--keep-old`: write a timestamped artifact instead of replacing the usual name.
- `--raw-log`: show Packwiz's repetitive manifest output.
- `--open`: reveal the finished artifact in Explorer.

`dist/` contains generated results and is never a project source of truth. A legacy
`.mrpack` in the project root is preserved but ignored by new builds.

## Resource packs and Default Options

When Default Options and `config/defaultoptions-common.toml` are present,
ModpackTools can edit the enabled resource-pack order as part of a transaction:

```powershell
modpack resource enable "Fresh Animations" --position 1
modpack resource move A B C --position 2
modpack resource disable A B
modpack add <resource-pack> --enable --position 1
```

Position 1 is the highest visible Minecraft priority. For a batch, selected packs
are removed first and inserted as one ordered block. `move` requires every selected
pack to be active; disabling an inactive pack is a no-op. `add --enable` requires
both `--enable` and `--position`, and installs and activates the batch in one
transaction.

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
| Stable project ID, display/build identity and optional target Java version | `.modpack/project.psd1` |
| Categories, display overrides, notes and explicit/transitive intent | `.modpack/metadata.psd1` |
| Enabled resource-pack order | `config/defaultoptions-common.toml` |
| Generated installable artifacts | `dist/` (output only) |
| Installer dependency versions and hashes | `dependencies.psd1` |

Minimal project identity:

```powershell
@{
    SchemaVersion  = 1
    Id             = 'example'
    DisplayName    = 'Example Pack'
    DisplayVersion = '1.21.1'
    OutputName     = 'Example-Pack-1.21.1.mrpack'
    JavaVersion    = '21' # optional, enables Java requirement checks
}
```

Editorial metadata keys use stable namespaced identities such as
`modrinth:<project-id>`, `curseforge:<project-id>`, `packwiz:<path>` and
`local:<path>`. Older content without recorded intent is treated conservatively as
explicit. Adding an installed transitive dependency promotes it to explicit.

## Output, errors and themes

R3CLI owns the CLI layout, status symbols, help rendering, colour handling and
expected-error format. ModpackTools supplies domain data and a small product theme.
Expected failures show a concise cause, optional details and an actionable `Try:`
line; unexpected PowerShell errors remain visible for diagnosis.

The bundled `theme.toml` adds semantic colours for client, host and local content:

```toml
[colours]
client = "#748FFC"
host = "#BE70FF"
local = "#FF91CD"
```

Place a complete personal theme at
`$env:LOCALAPPDATA\ModpackTools\theme.toml`. User values override the bundled theme;
missing roles fall back through R3CLI. `NO_COLOR` and redirected output disable ANSI
under automatic colour detection.

See [R3CLI integration](docs/r3cli-integration.md) and
[Error design](docs/error-design.md) for the presentation contracts.

## Troubleshooting

- **A new terminal loads an old version:** run `Get-Module -ListAvailable
  ModpackTools | Select Version,Path`, update every unintended duplicate, then check
  `modpack --version` and `(Get-Module ModpackTools).Path` in a normal new terminal.
- **The active project disappeared:** `modpack use` is session-local. Select it
  again or pass `--project <id>`.
- **Doctor says verification is incomplete:** use `doctor --details`; add
  `JavaVersion` when appropriate, but do not interpret incomplete coverage as a
  successful loader launch.
- **Doctor says the `.mrpack` is stale:** the project changed after the last build.
  Run `modpack build` and install the new file from `dist/`.
- **A synced folder is rejected as linked:** normal OneDrive Files On-Demand folders
  are supported. Move real symlinks or junctions out of the transactional project.
- **A local JAR is outdated:** local files participate in validation but must be
  replaced manually.

## Development and documentation

Run the deterministic suite with PowerShell 7 and Pester 4.10.1:

```powershell
Import-Module Pester -RequiredVersion 4.10.1
Invoke-Pester -Script .\Tests
```

The optional live integration creates a disposable project, downloads real
Modrinth artifacts and inspects the generated MRPack:

```powershell
.\Tests\Invoke-LiveIntegration.ps1 -WorkRoot <scratch-directory> -PackwizPath <packwiz.exe>
```

Further documentation:

- [Dependency engine and project transactions](docs/dependency-engine.md)
- [R3CLI integration](docs/r3cli-integration.md)
- [Error design](docs/error-design.md)

## License

[MIT](LICENSE)
