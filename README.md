# ModpackTools

ModpackTools is a small CLI for managing multiple Minecraft Java modpacks based on Packwiz and exporting them to Modrinth. Its only public command is `modpack`.

## Design and sources of truth

- Packwiz owns technical data: technical name, filename, version, side, provider, IDs, URLs, and hashes.
- `.modpack` stores only identity and editorial decisions: short ID, display name/version, artifact name, categories, notes, and name overrides.
- `config/defaultoptions-common.toml` owns the enabled resource-pack order.
- `dependencies.psd1` pins the verified Packwiz build, download, and SHA-256 hashes used by managed installation.
- `dist/` contains generated results and is never a source of truth for the modpack.

A mod without metadata remains valid and appears under `MODS · UNCLASSIFIED`. A reference to a missing category produces a warning and the mod is not discarded.

## Installation

ModpackTools targets Windows. From the source directory:

```powershell
.\Install-ModpackTools.ps1
```

The installer is the only setup entry point. It can start under Windows PowerShell 5.1: when PowerShell 7 is missing, it offers to install the latest stable version with WinGet and relaunches itself under `pwsh`. It then installs the module, runs `modpack doctor --fix`, and offers to configure missing requirements.

If Packwiz is already configured or available in `PATH`, it is reused. Otherwise the installer offers the Windows x64 build pinned in `dependencies.psd1`, verifies the archive and executable SHA-256 hashes, and installs it under `LocalApplicationData\ModpackTools\tools`. Adding that directory to the user `PATH` is optional. When declined, ModpackTools saves the executable path and calls it directly.

The module is copied directly to `ModpackTools` under the first user directory in `PSModulePath`. It does not create numeric version subdirectories, modify `$PROFILE`, or keep older installed copies. PowerShell autoloads `modpack` when it is used.

For unattended installation, `-NonInteractive` skips repair prompts and only reports environment health. `-SkipDoctor` skips the final diagnostic entirely.

## Initial configuration

```powershell
modpack config set root "D:\Minecraft"
modpack config get root
modpack config get packwiz
modpack config set packwiz "C:\Tools\packwiz.exe"
modpack config set packwiz auto
```

Configuration is stored in the user's standard `LocalApplicationData\ModpackTools\config.psd1` directory. Packwiz resolution uses an explicit configured path first, then `PATH`, then the managed copy. `auto` removes the explicit override and restores discovery.

## Environment doctor

```powershell
modpack doctor
modpack doctor --fix
modpack doctor --fix --yes
```

`doctor` checks PowerShell, the loaded ModpackTools version, configuration writability, Packwiz resolution and invocation, the project root, project discovery, Git, and a standard Minecraft Java installation. It does not modify anything except a temporary configuration write probe that is immediately removed.

Git and Minecraft Java are optional. A missing standard Minecraft installation produces a warning, not a failure; custom launchers may not be detected. `doctor --fix` offers safe repairs for required components. It never installs Minecraft. `--yes` accepts recommended defaults without adding Packwiz to `PATH` or inventing a project root.

## Project structure

```text
MyPack-1.21/
├── pack.toml
├── index.toml
├── README.md
├── .gitignore
├── .modpack/
│   ├── project.psd1
│   └── metadata.psd1
├── mods/
├── config/
├── resourcepacks/
├── shaderpacks/
└── dist/
```

### `project.psd1`

```powershell
@{
    SchemaVersion  = 1
    Id             = 'example'
    DisplayName    = 'My Pack'
    DisplayVersion = '1.21'
    OutputName     = 'MyPack-1.21.mrpack'
}
```

Minecraft, loader, and technical version are not duplicated: they are read from `pack.toml`. `DisplayName` and `DisplayVersion` are editorial concepts and may differ from the technical values.

### `metadata.psd1`

```powershell
@{
    Categories = @{
        performance = @{ Name = 'PERFORMANCE'; Order = 10 }
    }
    Mods = @{
        'modrinth:AANobbMI' = @{ Category = 'performance' }
    }
    ResourcePacks = @{
        '$polymer-resources' = @{ Name = 'Polymer Resources' }
    }
}
```

Mod keys are stable, namespaced IDs: `modrinth:<id>`, `curseforge:<id>`, `packwiz:<path>`, or `local:<path>`.

## Commands

```powershell
modpack --help
modpack --version
modpack doctor
modpack build --help
modpack list
modpack use vp26
modpack use
modpack status
modpack status vp26 --full
modpack status --project vp26 --full
modpack inventory
modpack inventory --project vp26
modpack inventory --category performance
modpack inventory --side host --source local
modpack inventory --type resourcepack --state active
modpack inventory --search sodium
modpack update 3
modpack resource enable "Fresh Animations" --position 1
modpack resource enable "fresh-animations.zip" --position 3 --project vp26
modpack resource disable "Fresh Animations"
modpack search sodium
modpack search "fresh animations" --type resourcepack
modpack add 2
modpack add sodium --category performance
modpack classify sodium performance
modpack classify sodium unclassified
modpack add sodium --project vp26 --category performance
modpack update sodium
modpack update sodium lithium ferrite-core
modpack update --all
modpack update --all --type resourcepack
modpack update --all --project vp26
modpack build
modpack build vp26 --keep-old --raw-log
modpack build --project vp26 --keep-old --raw-log
modpack diff
modpack diff vp26
modpack diff --project vp26
```

Run `modpack --help` for the command overview or `modpack <command> --help` for a command's syntax, options, important behavior, and focused examples. Help is generated from one catalog so the overview and detailed pages stay consistent; `help` is not a command.

Every command that operates on an existing project accepts `--project <id>`. After `modpack use <id>`, omit it to use the active project for the current PowerShell process. `status`, `inventory`, `build`, and `diff` also retain their positional ID shorthand; specifying both forms at once is rejected.

### Inspecting and filtering the inventory

`modpack inventory [id] [--project <id>]` displays mods, enabled and disabled resource packs, and shaders. Filters can be combined:

- `--type all|mod|resourcepack|shaderpack`
- `--category <id|unclassified>` or `--unclassified`
- `--side client|host|both|unknown`
- `--source packwiz|local|builtin|missing`
- `--state all|active|inactive`
- `--search <text>` searches the name, ID, and filename.

`host` corresponds to Packwiz's technical `server` value. Category and side filters automatically narrow the view to mods; `--state` narrows it to resource packs.

Every item displayed by `inventory` receives one global `[number]` for that exact filtered view. The numbering continues across mod categories, enabled and disabled resource packs, and shaders. It is saved for 24 hours and bound to the selected project. These references can be used by:

```powershell
modpack resource enable <inventory-number> --position <n>
modpack resource disable <inventory-number>
modpack classify <inventory-number> <category|unclassified>
modpack update <inventory-number>...
```

Number contexts are deliberately separate. `modpack add <number>` always resolves against the latest `search` results; `resource`, `classify`, and `update` always resolve against the latest `inventory` view. Therefore running `search`, then `inventory`, does not make a later number ambiguous or overwrite the other list. The caches are convenience references only; project files remain the sources of truth.

### Enabling, disabling, and ordering resource packs

```powershell
modpack resource enable <name|id|filename> --position <n> [--project <id>]
modpack resource disable <name|id|filename> [--project <id>]
```

The selector accepts the name displayed by `inventory`, the Default Options ID, or the exact filename. Position 1 is the highest priority visible in the Minecraft GUI. If the pack is already enabled, `enable` repositions it; if it is disabled, the command enables it. `disable` removes it from the enabled order without deleting its ZIP or `.pw.toml`. Repeated disable operations are safe. Ambiguous selectors and out-of-range positions are rejected without writing.

The command modifies only `defaultResourcePacks` in `config/defaultoptions-common.toml`; it does not maintain a second activation list.

### Classifying an existing mod

```powershell
modpack classify <name|id|filename> <category> [--project <id>]
modpack classify <name|id|filename> unclassified [--project <id>]
```

The selector accepts the displayed name, stable ID, current filename, or `.pw.toml` stem. The category must already exist in `.modpack/metadata.psd1`. Reclassification replaces only the mod's editorial `Category` value; it does not move, reinstall, or modify the Packwiz metadata. `unclassified` removes the assignment while preserving other editorial fields such as a name override.

### Creating a project

```powershell
modpack new demo --name Demo --minecraft 1.21.1 --loader fabric
```

By default, Packwiz selects the latest compatible Fabric Loader; the technical version is `0.1.0`, the display version matches Minecraft, and the directory is `<Name>-<Minecraft>`. These can be adjusted with `--loader-version`, `--pack-version`, `--display-version`, and `--path`. Creation uses a temporary directory and never overwrites the destination.

### Adding content

`modpack add` delegates technical selection and writing to `packwiz modrinth add`, then reads the generated `.pw.toml`. It accepts a Modrinth ID, slug, or a number from the last search and can install mods, resource packs, and shaders. When `--category` is provided, the result must be a mod and only that editorial decision is written to metadata; the category must already exist. The former `modpack add mod` form is invalid.

### Searching Modrinth

```powershell
modpack search <query> [--type <mod|resourcepack|shaderpack>] [--limit <1-50>] [--project <id>]
modpack add <number>
```

Search results are filtered by the selected project's Minecraft version. Mod-only searches also filter by its loader. Each result displays a temporary number, type, title, stable Modrinth project ID, slug, author, and download count.

The numbered list is stored in `LocalApplicationData\ModpackTools\last-search.json`, expires after 24 hours, and is tied to the project used for the search. It exists only for command-line convenience: Packwiz remains the source of truth and installation uses the stable project ID. Running another search replaces the list. A number that is missing, expired, or belongs to another project is rejected without installing anything.

### Updating mods

```powershell
modpack update <name|id|filename...> [--type <type>] [--project <id>]
modpack update --all [--type <type>] [--project <id>]
```

A selector can be the displayed name, stable ID, current filename, or `.pw.toml` stem. It can identify a mod, resource pack, or shader. Several selectors are updated as one transaction: all are validated before Packwiz runs, and if any update fails, `pack.toml`, `index.toml`, and all `.pw.toml` files are restored to their original bytes.

`--all` means all Packwiz-managed external content: mods, resource packs, and shaders. `--type mod|resourcepack|shaderpack` narrows either a selector operation or `--all`. Local JAR and ZIP files are never updated and are reported as non-updatable when explicitly selected. Updating does not generate a build; use `modpack diff` to review the changes and `modpack build` when ready.

### Building

`modpack build` validates the project, runs `packwiz refresh` and `packwiz modrinth export`, and publishes the result in `dist/`. It exports to a temporary file first so a valid artifact is not destroyed if Packwiz fails.

A `.mrpack` left in the root of a migrated project is a legacy artifact that ModpackTools preserves to avoid deleting data. New builds and replacements live exclusively in `dist/`; the legacy file can be removed manually when it is no longer needed.

- `--no-refresh`: skips `packwiz refresh`.
- `--keep-old`: generates the new artifact with a timestamp when the usual name exists.
- `--raw-log`: also shows repetitive `added to manifest` lines.
- `--open`: opens Explorer with the result selected.

### Comparing with the latest build

```powershell
modpack diff [id] [--project <id>]
```

`diff` selects the newest `.mrpack` in `dist/`, creates a temporary Packwiz export of the current project without running `packwiz refresh`, and compares both artifacts semantically. It reports added, changed, and removed dependencies, mods, resource packs, shaders, configurations, local files, metadata, and other overrides. ZIP compression and entry timestamps do not create false differences.

The hidden temporary artifact is created in `dist/` so Packwiz can export on the same volume, and it is always removed. It is excluded from baseline selection. The project, Packwiz index, and previous builds are not modified. When `dist/` contains no previous build, the command fails with a clear error.

## Inventory and sides

`.pw.toml` files provide `client`, `server`, or `both`. For local JAR files, ModpackTools reads `fabric.mod.json`; when the side cannot be determined, it uses `unknown`.

```text
[C]    client only
[H]    host/server only
[C][H] both
[?]    unknown
```

The UI says “Host”, although Packwiz's technical value is `server`.

## CLI presentation

Menus, help pages, status views, confirmations, inventories, and build summaries share the same palette and visual hierarchy. The reserved `[C]` and `[H]` colors are used exclusively for client and host sides.

### Theme configuration

The complete CLI palette has a single source of truth in `theme.toml`, located at the root of the ModpackTools source code:

```toml
[colors]
client = "#748FFC"
host = "#BE70FF"
success = "#50C878"
error = "#F55A5A"
process = "#F5C850"
secondary = "#9196A0"
heading = "#50CDDC"
local = "#FF91CD"
accent = "#FFAA46"
value = "#EBEEF5"
```

Colors use the `#RRGGBB` format. Every key is required so an incomplete theme fails clearly instead of silently mixing configured colors with hidden defaults. After editing the source file, run `./Install-ModpackTools.ps1 -Force` and reload the module. The installer copies this same file into the installed module; there is no second palette definition in PowerShell code.

### Error messages

Expected CLI errors use a shared structure: a precise cause, optional upstream details, and one actionable `Try:` line. They also expose stable `ModpackTools.<area>.<condition>` error IDs and no longer report private module source lines as the apparent failure location. Unexpected programming errors remain ordinary PowerShell exceptions so diagnostic context is preserved. The complete contract and audited error namespaces are documented in [`docs/error-design.md`](docs/error-design.md).

## Default Options

When `config/defaultoptions-common.toml` exists, `defaultResourcePacks` is the source of truth for enabled ordering. Default Options stores packs from lowest to highest priority; ModpackTools reverses the list to display the actual GUI priority. The parser supports brackets, apostrophes, symbols, and escapes inside strings. Built-in IDs without an override are displayed literally. Physical ZIP files not included in the list appear as present but disabled.

## License

ModpackTools is released under the [MIT License](LICENSE).
