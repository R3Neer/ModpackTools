function New-MpHelpItem {
    param([Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][string]$Description)
    return [pscustomobject]@{ Label = $Label; Description = $Description }
}

function Get-MpCommandCatalog {
    if ($script:MpCommandCatalog) { return $script:MpCommandCatalog }

    $script:MpCommandCatalog = [ordered]@{
        list = [pscustomobject]@{
            Handler = 'Invoke-MpList'; Group = 'PROJECTS'; Summary = 'Show registered projects'; Description = 'Show every registered modpack with its name, ID, and location.'
            Usage = @('modpack list'); Items = @(); Notes = @(); Examples = @()
        }
        use = [pscustomobject]@{
            Handler = 'Invoke-MpUse'; Group = 'PROJECTS'; Summary = 'Select the active project'; Description = 'Select the project used by commands that do not receive an explicit project ID.'
            Usage = @('modpack use [id]'); Items = @(
                New-MpHelpItem '<id>' 'Project ID. Omit it to show the active project.'
            ); Notes = @('The selection lasts only for the current PowerShell session.'); Examples = @('modpack use vp26', 'modpack use')
        }
        status = [pscustomobject]@{
            Handler = 'Invoke-MpStatus'; Group = 'PROJECTS'; Summary = 'Show a project summary'; Description = 'Show project identity, versions, paths, and content totals.'
            Usage = @('modpack status [id] [options]'); Items = @(
                New-MpHelpItem '<id>' 'Project ID using the positional shorthand.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
                New-MpHelpItem '--full' 'Include the complete numbered inventory.'
            ); Notes = @('Do not combine the positional ID with --project.'); Examples = @('modpack status', 'modpack status vp26 --full', 'modpack status --project vp26')
        }
        new = [pscustomobject]@{
            Handler = 'Invoke-MpNew'; Group = 'PROJECTS'; Summary = 'Create a project'; Description = 'Create and register a new Packwiz project under the configured root.'
            Usage = @('modpack new <id> --name <name> --minecraft <version> --loader <fabric|quilt|forge|neoforge> [options]'); Items = @(
                New-MpHelpItem '<id>' 'Stable lowercase project ID.'
                New-MpHelpItem '--name <name>' 'Display name for the modpack.'
                New-MpHelpItem '--minecraft <version>' 'Minecraft version.'
                New-MpHelpItem '--loader <loader>' 'Mod loader: fabric, quilt, forge, or neoforge.'
                New-MpHelpItem '--path <directory>' 'Directory name below the configured root.'
                New-MpHelpItem '--loader-version <version>' 'Specific loader version; otherwise Packwiz selects the latest compatible one.'
                New-MpHelpItem '--pack-version <version>' 'Initial technical Packwiz version.'
                New-MpHelpItem '--display-version <version>' 'Initial user-facing version.'
            ); Notes = @('Packwiz validates the Minecraft and loader version combination.'); Examples = @('modpack new vanilla-plus --name "Vanilla Plus" --minecraft 1.21.1 --loader fabric', 'modpack new forge-pack --name "Forge Pack" --minecraft 1.20.1 --loader forge', 'modpack new quilt-pack --name "Quilt Pack" --minecraft 1.21.1 --loader quilt')
        }
        init = [pscustomobject]@{
            Handler = 'Invoke-MpInit'; Group = 'PROJECTS'; Summary = 'Adopt a Packwiz project'; Description = 'Initialize an existing Packwiz project for ModpackTools without changing its technical Packwiz data.'
            Usage = @('modpack init <id> [--path <directory>] [options]'); Items = @(
                New-MpHelpItem '<id>' 'New stable lowercase project ID.'
                New-MpHelpItem '--path <directory>' 'Existing Packwiz project. Default: current directory.'
                New-MpHelpItem '--display-name <name>' 'Optional display-name override; otherwise use pack.toml.'
                New-MpHelpItem '--display-version <version>' 'Optional display-version override; otherwise use pack.toml.'
                New-MpHelpItem '--output-name <file>' 'Optional .mrpack build filename.'
            ); Notes = @('The project must be a direct child of the configured root.', 'Packwiz files, content, and existing README or .gitignore files are preserved.', 'This command does not use the active project or accept --project.'); Examples = @('modpack init my-pack', 'modpack init legacy --path "D:\Minecraft\Legacy Pack"', 'modpack init my-pack --display-name "My Pack" --output-name MyPack.mrpack')
        }
        inventory = [pscustomobject]@{
            Handler = 'Invoke-MpInventory'; Group = 'CONTENT'; Summary = 'Inspect installed content'; Description = 'Show and filter the mods, resource packs, and shaders in a project.'
            Usage = @('modpack inventory [id] [options]'); Items = @(
                New-MpHelpItem '<id>' 'Project ID using the positional shorthand.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
                New-MpHelpItem '--type <type>' 'Filter by all, mod, resourcepack, or shaderpack.'
                New-MpHelpItem '--category <id|number>' 'Show mods in a category from classify list.'
                New-MpHelpItem '--side <side>' 'Filter by client, host, both, or unknown.'
                New-MpHelpItem '--source <source>' 'Filter by packwiz, local, builtin, or missing.'
                New-MpHelpItem '--state <state>' 'Filter resource packs by all, active, or inactive.'
                New-MpHelpItem '--search <text>' 'Keep entries whose searchable fields contain text.'
                New-MpHelpItem '--unclassified' 'Show only unclassified mods.'
            ); Notes = @('Displayed entries are numbered for resource, classify, and update. Numbers belong to this project and expire after 24 hours.', 'Do not combine --category with --unclassified.'); Examples = @('modpack inventory --type mod', 'modpack inventory --search Taverns', 'modpack inventory --type resourcepack --state active')
        }
        search = [pscustomobject]@{
            Handler = 'Invoke-MpSearch'; Group = 'CONTENT'; Summary = 'Search compatible Modrinth content'; Description = 'Search Modrinth for content compatible with the selected project.'
            Usage = @('modpack search <query> [options]'); Items = @(
                New-MpHelpItem '<query>' 'One or more words to search for.'
                New-MpHelpItem '--type <type>' 'Limit results to mod, resourcepack, or shaderpack.'
                New-MpHelpItem '--limit <1-50>' 'Maximum number of results. Default: 10.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @('Results are numbered for add. Numbers belong to this project and expire after 24 hours.'); Examples = @('modpack search sodium', 'modpack search "fresh animations" --type resourcepack', 'modpack search iris --limit 5 --project vp26')
        }
        add = [pscustomobject]@{
            Handler = 'Invoke-MpAdd'; Group = 'CONTENT'; Summary = 'Install content'; Description = 'Resolve and install compatible Modrinth content and its dependencies as one transaction.'
            Usage = @('modpack add <selector...> [options]'); Items = @(
                New-MpHelpItem '<selector>' 'Modrinth ID, slug, version URL, or number from the latest search.'
                New-MpHelpItem '--category <id>' 'Assign an editorial category when adding a mod.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @('Search numbers are separate from inventory numbers.', 'Categories apply only to mods.'); Examples = @('modpack add sodium', 'modpack add 2', 'modpack add sodium --category performance --project vp26')
        }
        classify = [pscustomobject]@{
            Handler = 'Invoke-MpClassify'; Group = 'CONTENT'; Summary = 'Manage mod categories'; Description = 'Define, inspect, remove, and assign editorial categories for mods.'
            Usage = @(
                'modpack classify list [--project <id>]'
                'modpack classify create <id> [--name <name>] [--order <n>] [--project <id>]'
                'modpack classify remove <category...> [--unclassify] [--project <id>]'
                'modpack classify set <mod...> <category|number|unclassified> [--project <id>]'
            ); Items = @(
                New-MpHelpItem 'list' 'Show defined categories and save their numbered references.'
                New-MpHelpItem 'create <id>' 'Create a category with a stable lowercase ID.'
                New-MpHelpItem 'remove <category>' 'Remove an unused category by ID or latest category number.'
                New-MpHelpItem 'set <mod> <category>' 'Assign, replace, or clear a mod category.'
                New-MpHelpItem '--name <name>' 'Set the display name for a new category.'
                New-MpHelpItem '--order <n>' 'Set its inventory display order; otherwise append it.'
                New-MpHelpItem '--unclassify' 'When removing a used category, clear it from every assigned mod.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @(
                'Classification numbers come from classify list; mod numbers come from inventory.'
                'Unclassified is the final list row and can be assigned, but not removed. Removing a category in use requires --unclassify.'
                'Classification never moves or reinstalls a mod.'
            ); Examples = @('modpack classify list', 'modpack classify create world-generation --name "WORLD GENERATION"', 'modpack classify set 3 2')
        }
        resource = [pscustomobject]@{
            Handler = 'Invoke-MpResource'; Group = 'CONTENT'; Summary = 'Manage resource pack activation'; Description = 'Enable, move, or disable an installed resource pack through Default Options.'
            Usage = @('modpack resource enable <selector...> --position <n> [options]', 'modpack resource move <selector...> --position <n> [options]', 'modpack resource disable <selector...> [options]'); Items = @(
                New-MpHelpItem '<selector>' 'Name, ID, filename, or latest inventory number.'
                New-MpHelpItem '--position <n>' 'Minecraft priority for enable or move. Position 1 is highest.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @('Default Options must be installed in the selected project.', 'Enable also repositions an active pack; move requires it to be active. Disable never uninstalls it.'); Examples = @('modpack resource enable "Fresh Animations" --position 1', 'modpack resource move 4 --position 2 --project vp26', 'modpack resource disable 4')
        }
        side = [pscustomobject]@{
            Handler = 'Invoke-MpSide'; Group = 'CONTENT'; Summary = 'Set a mod distribution side'; Description = 'Correct whether an installed mod is distributed to clients, hosts, or both.'
            Usage = @('modpack side set <mod...> <client|host|both> [options]'); Items = @(
                New-MpHelpItem 'set' 'Replace the selected mod side.'
                New-MpHelpItem '<mod>' 'Name, ID, filename, or latest inventory number.'
                New-MpHelpItem '<side>' 'client, host, or both.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @('For Packwiz content this changes side in the .pw.toml source of truth.', 'For local JARs it stores an explicit project override. This does not change the mod executable itself.'); Examples = @('modpack side set sodium client', 'modpack side set 3 both --project vp26')
        }
        versions = [pscustomobject]@{
            Handler = 'Invoke-MpVersions'; Group = 'CONTENT'; Summary = 'List compatible content versions'; Description = 'Show numbered Modrinth versions compatible with the project Minecraft version and loader.'
            Usage = @('modpack versions <selector> [--project <id>]'); Items = @(
                New-MpHelpItem '<selector>' 'Name, ID, filename, or latest inventory number.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @('The numbered list is saved for update --to and expires after 24 hours.', 'Only Modrinth-managed content has a compatible version list.'); Examples = @('modpack versions sodium', 'modpack versions 3 --project vp26')
        }
        update = [pscustomobject]@{
            Handler = 'Invoke-MpUpdate'; Group = 'CONTENT'; Summary = 'Update Packwiz-managed content'; Description = 'Update Packwiz-managed mods, resource packs, and shaders.'
            Usage = @('modpack update <selector...> [options]', 'modpack update --all [options]'); Items = @(
                New-MpHelpItem '<selector>' 'Name, ID, filename, or latest inventory number.'
                New-MpHelpItem '--all' 'Update all matching Packwiz-managed content.'
                New-MpHelpItem '--type <type>' 'Limit updates to mod, resourcepack, or shaderpack.'
                New-MpHelpItem '--to <version>' 'Use a version ID, version number, or number from versions. Requires one selector.'
                New-MpHelpItem '--strict' 'Block when dependency metadata is incomplete, not only on known conflicts.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @('Known required-version and incompatible-project conflicts block every update.', 'Without --strict, unavailable provider metadata is reported as a warning.', 'Multiple updates are transactional: metadata changes are rolled back if one fails.'); Examples = @('modpack update sodium', 'modpack update sodium --to 2 --strict', 'modpack update --all --type resourcepack')
        }
        build = [pscustomobject]@{
            Handler = 'Invoke-MpBuild'; Group = 'BUILD AND CONFIGURATION'; Summary = 'Generate an MRPack'; Description = 'Refresh project metadata and export a Modrinth .mrpack into dist/.'
            Usage = @('modpack build [id] [options]'); Items = @(
                New-MpHelpItem '<id>' 'Project ID using the positional shorthand.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
                New-MpHelpItem '--no-refresh' 'Skip the Packwiz refresh step.'
                New-MpHelpItem '--keep-old' 'Keep older .mrpack files in dist/.'
                New-MpHelpItem '--open' 'Reveal the generated file in File Explorer.'
                New-MpHelpItem '--raw-log' 'Show the complete Packwiz output.'
            ); Notes = @('The generated dist/ artifact is not a source of truth.', 'The finished artifact is compared with a fresh export of the prepared project before it replaces the previous build.'); Examples = @('modpack build', 'modpack build vp26 --keep-old', 'modpack build --project vp26 --open')
        }
        diff = [pscustomobject]@{
            Handler = 'Invoke-MpDiff'; Group = 'BUILD AND CONFIGURATION'; Summary = 'Compare with the latest build'; Description = 'Compare the current project with its newest .mrpack in dist/.'
            Usage = @('modpack diff [id] [options]'); Items = @(
                New-MpHelpItem '<id>' 'Project ID using the positional shorthand.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @(); Examples = @('modpack diff', 'modpack diff --project vp26')
        }
        doctor = [pscustomobject]@{
            Handler = 'Invoke-MpDoctor'; Group = 'BUILD AND CONFIGURATION'; Summary = 'Check and repair the environment'; Description = 'Check required tools, configuration, projects, and optional local integrations.'
            Usage = @('modpack doctor [options]'); Items = @(
                New-MpHelpItem '--fix' 'Offer safe repairs for missing required components.'
                New-MpHelpItem '--yes' 'Accept recommended repair defaults without prompts. Requires --fix.'
                New-MpHelpItem '--details' 'List every incomplete dependency verification grouped by cause.'
            ); Notes = @('Minecraft Java and Git are optional. Their warnings do not prevent ModpackTools from working.', 'Doctor reports project health, dependency coverage, and latest build freshness separately.', 'Incomplete verification means Fabric Loader may still reject the pack during startup.', 'Doctor never installs Minecraft or rebuilds an outdated artifact.'); Examples = @('modpack doctor', 'modpack doctor --details', 'modpack doctor --fix --yes')
        }
        config = [pscustomobject]@{
            Handler = 'Invoke-MpConfig'; Group = 'BUILD AND CONFIGURATION'; Summary = 'View or change global configuration'; Description = 'Read or change ModpackTools global configuration.'
            Usage = @('modpack config get <root|packwiz>', 'modpack config set root <directory>', 'modpack config set packwiz <executable|auto>'); Items = @(
                New-MpHelpItem 'root' 'Directory whose direct children contain registered modpacks.'
                New-MpHelpItem 'packwiz' 'Executable override. Use auto to restore automatic discovery.'
            ); Notes = @('Automatic discovery checks PATH and then the managed ModpackTools copy.'); Examples = @('modpack config get packwiz', 'modpack config set packwiz "C:\Tools\packwiz.exe"', 'modpack config set packwiz auto')
        }
    }
    foreach ($name in @('pin', 'unpin')) {
        $script:MpCommandCatalog[$name] = [pscustomobject]@{
            Handler = $(if ($name -eq 'pin') { 'Invoke-MpPin' } else { 'Invoke-MpUnpin' }); Group = 'CONTENT'
            Summary = "$name managed content"; Description = 'Control automatic version changes using Packwiz pins.'
            Usage = @("modpack $name <selector...> [--project <id>] [--dry-run]"); Items = @(
                New-MpHelpItem '<selector...>' 'Managed content names, IDs, filenames or inventory numbers; duplicates are consolidated.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
                New-MpHelpItem '--dry-run' 'Preview the complete batch without changing project files.'
            ); Notes = @('Local files cannot be pinned. Updates that change a pin require unpin first.'); Examples = @("modpack $name sodium lithium")
        }
    }
    foreach ($name in @('add','update','classify','side','resource','build','doctor')) {
        $dryRunHelp = if ($name -eq 'classify') { 'Preview set/remove without applying project changes.' } else { 'Prepare and validate without applying project changes.' }
        $script:MpCommandCatalog[$name].Items += New-MpHelpItem '--dry-run' $dryRunHelp
    }
    foreach ($name in @('add','update','doctor')) {
        $script:MpCommandCatalog[$name].Items += New-MpHelpItem '--allow-downgrade' 'Allow dependency resolution to select an older installed version.'
    }
    foreach ($name in @('add','build','doctor')) {
        $script:MpCommandCatalog[$name].Items += New-MpHelpItem '--strict' 'Require complete verification and no known dependency conflicts.'
    }
    $script:MpCommandCatalog.inventory.Items += New-MpHelpItem '--check' 'Download missing artifacts and refresh dependency health. Default: local data and cache only.'
    $script:MpCommandCatalog.add.Items += New-MpHelpItem '--enable --position <n>' 'Activate a resource pack batch as one ordered block; requires Default Options.'
    $script:MpCommandCatalog.doctor.Items += New-MpHelpItem '--project <id>' 'Check or repair this project instead of the active project.'
    $script:MpCommandCatalog.doctor.Description = 'Check the environment and selected project; --fix previews safe repairs.'
    $script:MpCommandCatalog.update.Notes = @('New or aggravated conflicts block updates; unrelated existing conflicts are warnings unless --strict.', 'Pinned files are skipped by --all. Explicit pinned targets require unpin.', 'All selected versions are frozen before applying the transaction.')
    $script:MpCommandCatalog.resource.Notes += 'A batch is removed from the list first, then inserted at the requested position in argument order.'
    $script:MpCommandCatalog.build.Notes += 'Known dependency conflicts always block export; --no-refresh does not bypass validation.'
    return $script:MpCommandCatalog
}

function Show-MpHelp {
    param([string]$Command)
    $catalog = Get-MpCommandCatalog
    if ($Command -and -not $catalog.Contains($Command.ToLowerInvariant())) {
        Throw-MpError -Message "Command '$Command' does not have a help page" -Hint 'modpack --help' -ErrorId 'Command.UnknownHelpTopic' -Category InvalidArgument
    }
    $commands = @(
        foreach ($name in $catalog.Keys) {
            $entry = $catalog[$name]
            [pscustomobject]@{ Name=$name; Group=$entry.Group; Summary=$entry.Summary; Description=$entry.Description; Usage=$entry.Usage; Items=$entry.Items; Notes=$entry.Notes; Examples=$entry.Examples }
        }
    )
    $view = [pscustomobject]@{
        Product='MODPACKTOOLS'; Version=$script:ModuleVersion
        Description='Manage, inspect, update, and build Packwiz modpacks.'; Invocation='modpack'
        Groups=@('PROJECTS','CONTENT','BUILD AND CONFIGURATION'); Commands=$commands
        Usage=@('modpack <command> [arguments] [options]','modpack <command> --help','modpack --version')
        GlobalItems=@(
            New-MpHelpItem '--version' 'Print the installed version.'
            New-MpHelpItem '--colour auto|always|never' 'Control colour; auto follows terminal detection and NO_COLOR.'
            New-MpHelpItem '--ascii' 'Use ASCII symbols for presentation.'
        )
        Notes=@('Project commands accept --project <id>. It overrides the active project for that command.','Run modpack <command> --help for detailed help.')
    }
    [void](Test-R3HelpCatalogue $view -ExecutableCommands @($catalog.Keys))
    Write-R3Help (Get-MpConsole) $view $Command
}
