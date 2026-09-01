# ModpackTools

ModpackTools is a small CLI for managing multiple Minecraft Java modpacks based on Packwiz and exporting them to Modrinth. Its only public command is `modpack`.

## Design and sources of truth

- Packwiz owns technical data: technical name, filename, version, side, provider, IDs, URLs, and hashes.
- `.modpack` stores only identity and editorial decisions: short ID, display name/version, artifact name, categories, notes, and name overrides.
- `config/defaultoptions-common.toml` owns the enabled resource-pack order.
- `dist/` contains generated results and is never a source of truth for the modpack.

A mod without metadata remains valid and appears under `MODS · UNCLASSIFIED`. A reference to a missing category produces a warning and the mod is not discarded.

## Requirements and installation

- Windows and PowerShell 7.
- Packwiz available in `PATH`.

From the source directory:

```powershell
./Install-ModpackTools.ps1
```

The installer copies the module version to the first user directory in `PSModulePath`. It does not modify `$PROFILE`; PowerShell autoloads `modpack` when it is used.

## Initial configuration

```powershell
modpack config set root "D:\Minecraft"
modpack config get root
```

Configuration is stored in the user's standard `LocalApplicationData\ModpackTools\config.psd1` directory.

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
modpack help
modpack help build
modpack list
modpack use vp26
modpack use
modpack status
modpack status vp26 --full
modpack inventory
modpack inventory --category performance
modpack inventory --side host --source local
modpack inventory --type resourcepack --state active
modpack inventory --search sodium
modpack resource enable "Fresh Animations" --position 1
modpack resource enable "fresh-animations.zip" --position 3 --project vp26
modpack add sodium --category performance
modpack add sodium --project vp26 --category performance
modpack build
modpack build vp26 --keep-old --raw-log
modpack diff
modpack diff vp26
```

An explicit ID takes precedence over the session selection. `modpack use` affects only the current PowerShell process.

### Inspecting and filtering the inventory

`modpack inventory [id]` displays mods, enabled and disabled resource packs, and shaders. Filters can be combined:

- `--type all|mod|resourcepack|shaderpack`
- `--category <id|unclassified>` or `--unclassified`
- `--side client|host|both|unknown`
- `--source packwiz|local|builtin|missing`
- `--state all|active|inactive`
- `--search <text>` searches the name, ID, and filename.

`host` corresponds to Packwiz's technical `server` value. Category and side filters automatically narrow the view to mods; `--state` narrows it to resource packs.

### Enabling and ordering resource packs

```powershell
modpack resource enable <name|id|filename> --position <n> [--project <id>]
```

The selector accepts the name displayed by `inventory`, the Default Options ID, or the exact filename. Position 1 is the highest priority visible in the Minecraft GUI. If the pack is already enabled, the same command repositions it; if it is disabled, the command enables it. Ambiguous selectors and out-of-range positions are rejected without writing.

The command modifies only `defaultResourcePacks` in `config/defaultoptions-common.toml`; it does not maintain a second activation list.

### Creating a project

```powershell
modpack new demo --name Demo --minecraft 1.21.1 --loader fabric
```

By default, Packwiz selects the latest compatible Fabric Loader; the technical version is `0.1.0`, the display version matches Minecraft, and the directory is `<Name>-<Minecraft>`. These can be adjusted with `--loader-version`, `--pack-version`, `--display-version`, and `--path`. Creation uses a temporary directory and never overwrites the destination.

### Adding a mod

`modpack add` delegates technical selection and writing to `packwiz modrinth add`, then reads the generated `.pw.toml`. When `--category` is provided, only that editorial decision is written to metadata; the category must already exist. The former `modpack add mod` form is invalid.

### Building

`modpack build` validates the project, runs `packwiz refresh` and `packwiz modrinth export`, and publishes the result in `dist/`. It exports to a temporary file first so a valid artifact is not destroyed if Packwiz fails.

A `.mrpack` left in the root of a migrated project is a legacy artifact that ModpackTools preserves to avoid deleting data. New builds and replacements live exclusively in `dist/`; the legacy file can be removed manually when it is no longer needed.

- `--no-refresh`: skips `packwiz refresh`.
- `--keep-old`: generates the new artifact with a timestamp when the usual name exists.
- `--raw-log`: also shows repetitive `added to manifest` lines.
- `--open`: opens Explorer with the result selected.

### Comparing with the latest build

```powershell
modpack diff [id]
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

Colors use the `#RRGGBB` format. Every key is required so an incomplete theme fails clearly instead of silently mixing configured colors with hidden defaults. After editing the source file, run `./Install-ModpackTools.ps1 -Force` and reload the module. The installer copies this same file into the installed version; there is no second palette definition in PowerShell code.

## Default Options

When `config/defaultoptions-common.toml` exists, `defaultResourcePacks` is the source of truth for enabled ordering. Default Options stores packs from lowest to highest priority; ModpackTools reverses the list to display the actual GUI priority. The parser supports brackets, apostrophes, symbols, and escapes inside strings. Built-in IDs without an override are displayed literally. Physical ZIP files not included in the list appear as present but disabled.
