# ModpackTools

ModpackTools manages Minecraft Java modpacks built with Packwiz on Windows x64.
The executable is compiled with C# Native AOT. PowerShell and the .NET runtime are
not required. Nushell 0.116.2 or newer provides the human interface and structured
pipeline values. The machine executable can run without Nushell.

Dependency checks inspect declared metadata; they do not launch Minecraft or prove
that a pack starts. Incomplete evidence remains visible. Use `--strict` where
incomplete verification must block an operation.

## Installation

Use a trusted native package named `ModpackTools-Native-win-x64-X.Y.Z.zip` and its
independently trusted SHA256. A release containing only a legacy module ZIP cannot
be installed or updated through the native installer. Native assets may not yet
be available on the public release channel.

In Nu, verify the downloaded archive before extracting or executing it:

```nu
open --raw 'C:/Downloads/ModpackTools-Native-win-x64-X.Y.Z.zip' | hash sha256
```

Compare with the trusted digest. Extract into a temporary directory using your
archive tool. Run its native installer, supplying an explicit NEW application
root, the original archive and its digest:

```nu
^'C:/Downloads/ModpackTools/modpacktools-setup.exe' install --root 'D:/Minecraft/Tools/ModpackTools-App' --nu 'D:/Toolchains/Nushell/bin/nu.exe' --package 'C:/Downloads/ModpackTools-Native-win-x64-X.Y.Z.zip' --sha256 'TRUSTED_SHA256'
```

Use your actual paths. Keep the application directory separate from source
checkouts, projects and configuration. The installer verifies the archive and
checks the executable, bundled presentation and Nu integration before selecting
the installed version. It never installs a shell or changes profiles, PATH or plugins.

Add this import to your Nu configuration, using your chosen root:

```nu
use 'D:/Minecraft/Tools/ModpackTools-App/control/Nushell/modpack.nu' *
```

Open a new Nu session. Run `modpack --version` and `modpack --help`.
For other shells, optionally add the application's `bin` directory to your user
PATH, or invoke `bin/modpack.exe` by its full path. Human commands use the Nu
executable selected during installation. `modpack.exe --no-human` emits a JSON
envelope without requiring Nu to render it.

Packwiz is needed for creation, refresh and export. Configure an existing executable
with `modpack config set packwiz <path>`, or review `modpack doctor --fix` to obtain
the pinned hash-verified Windows build. R3CLI's Nu presentation is bundled.
Git and a local Minecraft installation are optional integrations.

## Updates and rollback

```nu
modpack self-update --check
modpack self-update
modpack self-update --yes
modpack self-update --package 'C:/Downloads/native.zip' --sha256 'TRUSTED_SHA256' --yes
```

Human update defaults to No; without a terminal it cancels. Machine mode applies
only with explicit `--yes`. Public checks select newer stable native releases;
legacy-only releases are never substituted. An explicit local package can replace
a build of the same public version; a lower version also needs `--allow-downgrade`.
Open a new Nu session after updating. Older sessions retain their original module.

Use the installed native helper for explicit rollback:

```nu
^'D:/Minecraft/Tools/ModpackTools-App/bin/modpacktools-setup.exe' rollback --root 'D:/Minecraft/Tools/ModpackTools-App'
```

Close old Nu sessions before explicitly collecting retired versions:

```nu
^'D:/Minecraft/Tools/ModpackTools-App/bin/modpacktools-setup.exe' cleanup --root 'D:/Minecraft/Tools/ModpackTools-App'
```

Cleanup removes eligible rollback versions. It retains active, pinned, changed or
uncertain trees and reports exact paths; preserve reported recovery evidence.

## Five-minute workflow

Set the directory containing your projects:

```nu
modpack config set root "D:\Minecraft"
modpack doctor
```

Create a project:

```nu
modpack project create vanilla-plus --name "Vanilla Plus" --minecraft 1.21.1 --loader fabric
modpack project register existing-pack --path "D:\Minecraft\Existing Pack"
```

Select and inspect it:

```nu
modpack project use vanilla-plus
modpack project status
modpack content list
modpack doctor
```

Preview before mutating:

```nu
modpack content search sodium
modpack content add sodium lithium -n
modpack content update --all -n
```

Apply and build:

```nu
modpack content add sodium lithium
modpack content update --all
modpack doctor
modpack build
modpack diff
```

Use the imported Nushell command for session-local project selection.

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

```nu
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

## Nushell pipelines and session selection

Normal commands show R3CLI output. Add `--no-human` to receive Nu records or lists:

```nu
modpack content list --type mod --no-human | where side == client | sort-by name
modpack project list --no-human | select id name
```

The imported `modpack project use <id>` changes `MODPACKTOOLS_PROJECT` in the current
Nu session after successful validation. An explicit `--project` affects only one
invocation. An external executable cannot change its parent shell's environment;
use the imported Nu command for persistent selection within that session.

Direct `modpacktools.exe` is the machine host: use `--json --no-human` for a single
schema-versioned JSON envelope. `bin/modpack.exe` selects the installed entrypoint;
`--no-human` selects its machine path. Error envelopes are authoritative and Nu
converts failures into actionable shell errors. See `docs/nushell.md` in the source repository for the complete adapter contract.

## Inventory and selectors

Inventory filters compose:

```nu
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

```nu
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

```nu
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
  â†“
validate complete plan
  â†“
stage outside project
  â†“
re-check project fingerprint
  â†“
commit
  â†“
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

```nu
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

```nu
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
â”œâ”€â”€ pack.toml
â”œâ”€â”€ index.toml
â”œâ”€â”€ .modpack/
â”‚   â”œâ”€â”€ project.psd1
â”‚   â””â”€â”€ metadata.psd1
â”œâ”€â”€ mods/
â”œâ”€â”€ config/
â”œâ”€â”€ resourcepacks/
â”œâ”€â”€ shaderpacks/
â””â”€â”€ dist/
```

| Data | Source of truth |
| --- | --- |
| Minecraft, loader, technical versions, files, hashes, provider IDs and managed sides | Packwiz files |
| Stable project ID, display/build identity and optional Java version | `.modpack/project.psd1` |
| Categories, display overrides, notes and explicit/transitive intent | `.modpack/metadata.psd1` |
| Enabled resource-pack order | `config/defaultoptions-common.toml` |
| Generated installable artifacts | `dist/` |

## Configuration and recovery

Configuration and caches default to `%LOCALAPPDATA%/ModpackTools`, independent of
application sources and installed versions. `MODPACKTOOLS_CONFIG_HOME` overrides
that configuration root. Existing supported PSD1 data is parsed by the native tool;
PowerShell is not executed. `MODPACKTOOLS_PROJECT` is session-local.

`MODPACKTOOLS_STATE_HOME` selects recovery state (default
`%LOCALAPPDATA%/ModpackTools/native-state`). Keep it separate from the application
and project trees. Preserve pending journals and recovery evidence before changing
this path. Setup also accepts `--state <directory>`; use the same selection for
later update, rollback and cleanup operations.

`MODPACKTOOLS_CORE_PATH` optionally overrides the executable used by a directly
imported package module for development. Installed control imports bind the selected
version automatically. A bad override fails explicitly.

A personal R3CLI theme can be placed at the configuration root's `theme.toml`.
`--color auto|always|never`, `NO_COLOR` and `--ascii` control presentation. Human
output goes to stderr; machine values remain available for pipelines.

## Troubleshooting

- After an update, reopen Nu to load the selected version and release old modules.
- If an old command still loads, inspect your config import and `which modpack`;
  point them to the chosen application's control import and bin directory.
- Use `doctor --details` for incomplete verification; it is not proof of runtime success.
- Use `--no-human` before piping results to `where`, `select` or `sort-by`.
- Preserve any exact retained-evidence path reported by a failed operation before retrying.

## Building

Source builds require the pinned SDK in `global.json` and the Windows Native AOT
C++ linker prerequisites. Publish the CLI, Launcher and Setup projects in Release
for win-x64, then use `modpacktools-setup.exe --help`
for package arguments. The native setup `pack` command includes the executables,
Nu modules, bundled R3CLI, licenses and this README. It excludes development code,
legacy PowerShell installers and migration documents. Runtime use needs no SDK.

Maintainer release instructions are in `docs/releasing.md` in the source repository.
MIT; see [LICENSE](LICENSE).
