# {0} {1}

Minecraft Java modpack managed with [Packwiz](https://packwiz.infra.link/) and ModpackTools.

## Project

- **ID:** `{2}`
- **Minecraft:** {3}
- **Loader:** {4}
- **Technical pack version:** {5}
- **Build artifact:** `dist/{6}`

## Quick start

```text
modpack --help
modpack --version
modpack doctor
modpack project use {2}
modpack project status
modpack content list
modpack diff
modpack build
```

`modpack project use` selects this project only for the current shell session. Global `--project <id>` or `-p <id>` selects one project for a single command and is accepted in any position, for example `modpack -p {2} diff`.

Run `modpack <command> --help` for concise command documentation. For example, `modpack content --help` explains inventory filters, searches, and numbered references.

Run `modpack doctor` to check the native tool, Packwiz, configuration, project discovery, Git, and the standard Minecraft Java installation. Git and Minecraft are optional; Packwiz and a valid project root are required for the complete workflow.

## Creating or registering projects

`project create` creates Fabric, Quilt, Forge, or NeoForge Packwiz projects. `project register` registers an existing Packwiz project without changing its technical metadata or installed content:

```text
modpack project create <id> --name <name> --minecraft <version> --loader <fabric|quilt|forge|neoforge>
modpack project register <id> --path <existing-packwiz-directory>
```

A registered project must be a direct child of the configured root. Existing README and `.gitignore` files are preserved.

## Managing content

Search or add Modrinth content. Editorial categories apply only to mods:

```text
modpack content search <query>
modpack content search <query> --type mod
modpack content add <search-number>
modpack content add <slug> --category <category>
modpack category list
modpack category create <id> --name <name>
modpack category assign <category|number> <name|id|filename...>
modpack category clear <name|id|filename...>
modpack category remove <category|number> [--clear-assignments]
modpack mod set-side <client|host|both> <mod|inventory-number...>
modpack content versions <name|id|filename>
modpack content update <name|id|filename> --to <version|number>
modpack content update --all --type mod
```

Inspect or filter the current contents:

```text
modpack content list --type mod
modpack content list --category <category|number|unclassified>
modpack content list --type resourcepack --state inactive
modpack content list --match <text>
modpack content update <inventory-number>
```

Every displayed inventory item has one global reference number for that filtered view. Use it with `resource-pack`, `mod set-side`, `content versions`, `category assign`, or other content operations. References are bound to this project and expire after 24 hours.

Enable or reposition a resource pack. Position `1` is the highest priority in the Minecraft GUI:

```text
modpack resource-pack enable <name|id|filename> --position <n>
modpack resource-pack move <name|id|filename> --position <n>
modpack resource-pack disable <name|id|filename>
```

## Build workflow

```text
modpack diff
modpack build
```

`modpack diff` compares the current project with the newest `.mrpack` in `dist/`. `modpack build` refreshes Packwiz metadata and writes the generated artifact to `dist/`.

`modpack content search` queries compatible Modrinth projects and saves numbered results for 24 hours. `content add <number>` installs from that saved list; IDs and slugs remain valid directly. The cache is only a convenience reference and Packwiz remains the technical source of truth.

Search, inventory, version, and category numbers have separate contexts. `content add <number>` uses the latest search; installed-content operations use the latest inventory; `content update --to <number>` uses the latest compatible-version list; category operations use the latest `category list`.

`modpack category` defines, lists, removes, assigns, and clears editorial categories. Removing a defined category that is in use requires `--clear-assignments`. Category definitions and assignments live only in `.modpack/metadata.psd1`; numbered lists are temporary references. Resource pack activation and ordering require Default Options; `resource-pack move` reorders an enabled pack and `disable` removes it from the enabled order without uninstalling it.

`content versions` lists compatible Modrinth releases and saves numbered choices. `content update` updates Packwiz-managed mods, resource packs, and shaders; `--to` selects an exact release, including an older one. Add and update resolve each batch together against a dependency graph combining provider and native JAR requirements for Fabric, Quilt, Forge and NeoForge. New or affected conflicts block the operation; `--strict` requires complete verification and a clean graph. Multiple selectors form one transaction covering technical files, editorial metadata and Default Options. Use `--dry-run` to preview it and `--allow-downgrade` to permit automatic dependency downgrades. `content pin <selector...>` fixes versions and `unpin` releases them; `update --all` skips pinned items. Use `--type mod|resourcepack|shaderpack` to narrow the operation. Local files are never updated. Review the result with `modpack diff` before building.

## Sources of truth

- `pack.toml`, its configured index, and `.pw.toml` files own technical Packwiz data.
- `.modpack/project.psd1` owns the short ID and display/build identity.
- `.modpack/metadata.psd1` owns editorial categories, display-name overrides, and explicit side overrides for local JARs.
- `config/defaultoptions-common.toml` owns enabled resource packs and their order.
- `dist/` contains generated artifacts and is not a source of truth.