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
            Handler = 'Invoke-MpAdd'; Group = 'CONTENT'; Summary = 'Install content'; Description = 'Add a compatible Modrinth project to the selected modpack with Packwiz.'
            Usage = @('modpack add <id|slug|search-number> [options]'); Items = @(
                New-MpHelpItem '<selector>' 'Modrinth ID, slug, or number from the latest search.'
                New-MpHelpItem '--category <id>' 'Assign an editorial category when adding a mod.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @('Search numbers are separate from inventory numbers.', 'Categories apply only to mods.'); Examples = @('modpack add sodium', 'modpack add 2', 'modpack add sodium --category performance --project vp26')
        }
        classify = [pscustomobject]@{
            Handler = 'Invoke-MpClassify'; Group = 'CONTENT'; Summary = 'Manage mod categories'; Description = 'Define, inspect, remove, and assign editorial categories for mods.'
            Usage = @(
                'modpack classify list [--project <id>]'
                'modpack classify create <id> [--name <name>] [--order <n>] [--project <id>]'
                'modpack classify remove <category|number> [--unclassify] [--project <id>]'
                'modpack classify set <mod|inventory-number> <category|number|unclassified> [--project <id>]'
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
            Usage = @('modpack resource enable <selector> --position <n> [options]', 'modpack resource move <selector> --position <n> [options]', 'modpack resource disable <selector> [options]'); Items = @(
                New-MpHelpItem '<selector>' 'Name, ID, filename, or latest inventory number.'
                New-MpHelpItem '--position <n>' 'Minecraft priority for enable or move. Position 1 is highest.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @('Default Options must be installed in the selected project.', 'Enable also repositions an active pack; move requires it to be active. Disable never uninstalls it.'); Examples = @('modpack resource enable "Fresh Animations" --position 1', 'modpack resource move 4 --position 2 --project vp26', 'modpack resource disable 4')
        }
        update = [pscustomobject]@{
            Handler = 'Invoke-MpUpdate'; Group = 'CONTENT'; Summary = 'Update Packwiz-managed content'; Description = 'Update Packwiz-managed mods, resource packs, and shaders.'
            Usage = @('modpack update <selector...> [options]', 'modpack update --all [options]'); Items = @(
                New-MpHelpItem '<selector>' 'Name, ID, filename, or latest inventory number.'
                New-MpHelpItem '--all' 'Update all matching Packwiz-managed content.'
                New-MpHelpItem '--type <type>' 'Limit updates to mod, resourcepack, or shaderpack.'
                New-MpHelpItem '--project <id>' 'Use this project instead of the active one.'
            ); Notes = @('Local files cannot be updated.', 'Multiple updates are transactional: metadata changes are rolled back if one fails.'); Examples = @('modpack update sodium', 'modpack update 2 5', 'modpack update --all --type resourcepack')
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
            ); Notes = @('The generated dist/ artifact is not a source of truth.'); Examples = @('modpack build', 'modpack build vp26 --keep-old', 'modpack build --project vp26 --open')
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
            ); Notes = @('Minecraft Java and Git are optional. Their warnings do not prevent ModpackTools from working.', 'Doctor never installs Minecraft.'); Examples = @('modpack doctor', 'modpack doctor --fix', 'modpack doctor --fix --yes')
        }
        config = [pscustomobject]@{
            Handler = 'Invoke-MpConfig'; Group = 'BUILD AND CONFIGURATION'; Summary = 'View or change global configuration'; Description = 'Read or change ModpackTools global configuration.'
            Usage = @('modpack config get <root|packwiz>', 'modpack config set root <directory>', 'modpack config set packwiz <executable|auto>'); Items = @(
                New-MpHelpItem 'root' 'Directory whose direct children contain registered modpacks.'
                New-MpHelpItem 'packwiz' 'Executable override. Use auto to restore automatic discovery.'
            ); Notes = @('Automatic discovery checks PATH and then the managed ModpackTools copy.'); Examples = @('modpack config get packwiz', 'modpack config set packwiz "C:\Tools\packwiz.exe"', 'modpack config set packwiz auto')
        }
    }
    return $script:MpCommandCatalog
}

function Show-MpHelp {
    param([string]$Command)
    $catalog = Get-MpCommandCatalog
    if (-not $Command) {
        Write-MpBanner "MODPACKTOOLS $script:ModuleVersion"
        Write-MpHelpText 'Manage, inspect, update, and build Packwiz modpacks.'
        Write-MpHelpHeading 'USAGE'
        Write-MpCommandLine 'modpack <command> [arguments] [options]'
        Write-MpCommandLine 'modpack <command> --help'
        Write-MpCommandLine 'modpack --version'
        foreach ($group in @('PROJECTS', 'CONTENT', 'BUILD AND CONFIGURATION')) {
            Write-MpHelpHeading $group
            foreach ($name in $catalog.Keys) {
                if ($catalog[$name].Group -eq $group) { Write-MpHelpRow -Label $name -Description $catalog[$name].Summary -Width 14 }
            }
        }
        Write-Host ''
        Write-MpInfo 'Project commands accept --project <id>. It overrides the active project for that command.'
        Write-MpInfo 'Run modpack <command> --help for detailed help.'
        return
    }

    $key = $Command.ToLowerInvariant()
    if (-not $catalog.Contains($key)) {
        Throw-MpError -Message "Command '$Command' does not have a help page" -Hint 'modpack --help' -ErrorId 'Command.UnknownHelpTopic' -Category InvalidArgument -TargetObject $Command
    }
    $entry = $catalog[$key]
    Write-MpBanner $key.ToUpperInvariant()
    Write-MpHelpText $entry.Description
    Write-MpHelpHeading 'USAGE'
    foreach ($usage in $entry.Usage) { Write-MpCommandLine $usage }
    if ($entry.Items.Count) {
        Write-MpHelpHeading 'ARGUMENTS AND OPTIONS'
        foreach ($item in $entry.Items) { Write-MpHelpRow -Label $item.Label -Description $item.Description -Width 28 }
    }
    if ($entry.Notes.Count) {
        Write-MpHelpHeading 'NOTES'
        foreach ($note in $entry.Notes) { Write-MpInfo $note }
    }
    if ($entry.Examples.Count) {
        Write-MpHelpHeading 'EXAMPLES'
        foreach ($example in $entry.Examples) { Write-MpCommandLine $example }
    }
}
