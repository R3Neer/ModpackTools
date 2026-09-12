function New-MpHelpItem {
    param([Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][string]$Description)
    return [pscustomobject]@{ Label = $Label; Description = $Description }
}

function Get-MpCommandCatalog {
    if ($script:MpCommandCatalog) { return $script:MpCommandCatalog }

    $script:MpCommandCatalog = [ordered]@{
        project = [pscustomobject]@{
            Handler='Invoke-MpProject'; Group='PROJECTS'; Summary='Manage projects'; Description='List, select, inspect, create, or register Packwiz projects.'
            Usage=@('modpack project list','modpack project current','modpack project use <id>','modpack project status','modpack project create <id> --name <name> --minecraft <version> --loader <loader> [options]','modpack project register <id> [--path <directory>] [options]')
            Items=@(
                New-MpHelpItem 'list' 'Show every registered project.'
                New-MpHelpItem 'current' 'Show the active project for this PowerShell session.'
                New-MpHelpItem 'use <id>' 'Select the active project for this PowerShell session.'
                New-MpHelpItem 'status' 'Show identity, versions, paths, and content totals.'
                New-MpHelpItem 'create <id>' 'Create and register a new Packwiz project.'
                New-MpHelpItem 'register <id>' 'Register an existing Packwiz project without changing its technical data.'
                New-MpHelpItem '--name <name>' 'Display name for a created project.'
                New-MpHelpItem '--minecraft <version>' 'Minecraft version for a created project.'
                New-MpHelpItem '--loader <loader>' 'fabric, quilt, forge, or neoforge.'
                New-MpHelpItem '--path <directory>' 'Directory name for create, or existing project path for register.'
                New-MpHelpItem '--loader-version <version>' 'Specific loader version for create.'
                New-MpHelpItem '--pack-version <version>' 'Initial technical Packwiz version for create.'
                New-MpHelpItem '--display-name <name>' 'Display-name override for register.'
                New-MpHelpItem '--display-version <version>' 'User-facing version for create or register.'
                New-MpHelpItem '--output-name <file>' 'MRPack build filename for register.'
            )
            Notes=@('Use global --project/-p with project status. Other project operations select their target explicitly.')
            Examples=@('modpack project list','modpack project use vp26','modpack -p vp26 project status','modpack project register legacy --path "D:\Minecraft\Legacy Pack"')
        }
        content = [pscustomobject]@{
            Handler='Invoke-MpContent'; Group='CONTENT'; Summary='Manage installed and available content'; Description='List, search, add, remove, version, update, pin, or unpin mods, resource packs, and shaders.'
            Usage=@('modpack content list [filters]','modpack content search <query> [options]','modpack content add <selector...> [options]','modpack content remove <selector...> [options]','modpack content versions <selector>','modpack content update <selector...> [options]','modpack content update --all [options]','modpack content pin|unpin <selector...> [options]')
            Items=@(
                New-MpHelpItem 'list' 'Show the numbered installed-content inventory.'
                New-MpHelpItem 'search <query>' 'Search compatible Modrinth content.'
                New-MpHelpItem 'add <selector...>' 'Install compatible content and dependencies.'
                New-MpHelpItem 'remove <selector...>' 'Preview and remove installed content.'
                New-MpHelpItem 'versions <selector>' 'List compatible Modrinth versions.'
                New-MpHelpItem 'update <selector...>' 'Update selected Packwiz-managed content.'
                New-MpHelpItem 'pin|unpin <selector...>' 'Control automatic version changes.'
                New-MpHelpItem '--type <type>' 'Filter or scope by all, mod, resourcepack, or shaderpack.'
                New-MpHelpItem '--category <id|number>' 'Filter listed mods or categorize added mods. Use unclassified to find uncategorized mods.'
                New-MpHelpItem '--side <side>' 'Filter listed content by client, host, both, or unknown.'
                New-MpHelpItem '--source <source>' 'Filter listed content by packwiz, local, builtin, or missing.'
                New-MpHelpItem '--state <state>' 'Filter resource packs by all, active, or inactive.'
                New-MpHelpItem '--match <text>' 'Keep listed entries whose searchable fields contain text.'
                New-MpHelpItem '--verify' 'Download missing artifacts and refresh inventory health.'
                New-MpHelpItem '--limit <1-50>' 'Maximum search results. Default: 10.'
                New-MpHelpItem '--all' 'Update all matching managed content.'
                New-MpHelpItem '--to <version>' 'Select an exact update version; requires one selector.'
                New-MpHelpItem '--cascade' 'Also remove required dependents.'
                New-MpHelpItem '--autoremove' 'Remove newly unused automatic dependencies.'
                New-MpHelpItem '--enable-at <n>' 'Enable an added resource-pack batch at priority position n.'
                New-MpHelpItem '--strict' 'Require complete verification and a clean resulting graph.'
                New-MpHelpItem '--allow-downgrade' 'Allow dependency resolution to select an older installed version.'
                New-MpHelpItem '-n, --dry-run' 'Prepare and validate without applying project changes.'
                New-MpHelpItem '-y, --yes' 'Apply a displayed removal plan without confirmation.'
            )
            Notes=@('Search can run globally. Project-filtered search, inventory, version, and category numbers have separate project-bound contexts.','Global search numbers may be used after selecting a project; compatibility is still checked when adding.','Local files cannot be updated or pinned. Pins block explicit changes.','Multiple selectors are resolved and committed as one transaction.')
            Examples=@('modpack content list --match Taverns','modpack content search sodium --type mod','modpack content add 2 --category performance','modpack content remove fabric-api --cascade --autoremove -n','modpack content update sodium --to 2 --strict')
        }
        category = [pscustomobject]@{
            Handler='Invoke-MpCategory'; Group='CONTENT'; Summary='Manage editorial categories'; Description='Define categories and assign or clear mod classifications.'
            Usage=@('modpack category list','modpack category create <id> [--name <name>] [--order <n>]','modpack category remove <category...> [--clear-assignments]','modpack category assign <category> <mod...>','modpack category clear <mod...>')
            Items=@(
                New-MpHelpItem 'list' 'Show category definitions and save numbered references.'
                New-MpHelpItem 'create <id>' 'Create a category with a stable lowercase ID.'
                New-MpHelpItem 'remove <category...>' 'Remove unused category definitions.'
                New-MpHelpItem 'assign <category> <mod...>' 'Assign or replace the selected mods category.'
                New-MpHelpItem 'clear <mod...>' 'Clear category assignments from selected mods.'
                New-MpHelpItem '--name <name>' 'Display name for a new category.'
                New-MpHelpItem '--order <n>' 'Inventory display order for a new category.'
                New-MpHelpItem '--clear-assignments' 'Clear assignments while removing a category.'
                New-MpHelpItem '-n, --dry-run' 'Preview assign, clear, or remove without applying changes.'
            )
            Notes=@('Category numbers come from category list; mod numbers come from content list.','Categories are editorial metadata and never move or reinstall mods.')
            Examples=@('modpack category list','modpack category create world-generation --name "WORLD GENERATION"','modpack category assign performance sodium lithium','modpack category clear 3')
        }
        'resource-pack' = [pscustomobject]@{
            Handler='Invoke-MpResourcePack'; Group='CONTENT'; Summary='Manage resource-pack activation'; Description='Enable, move, or disable installed resource packs through Default Options.'
            Usage=@('modpack resource-pack enable <selector...> --position <n> [options]','modpack resource-pack move <selector...> --position <n> [options]','modpack resource-pack disable <selector...> [options]')
            Items=@(
                New-MpHelpItem '<selector...>' 'Names, IDs, filenames, or latest content-list numbers.'
                New-MpHelpItem '--position <n>' 'Minecraft priority for enable or move. Position 1 is highest.'
                New-MpHelpItem '-n, --dry-run' 'Prepare and validate without applying changes.'
            )
            Notes=@('Default Options must be installed.','Enable also repositions an active pack; disable never uninstalls it.')
            Examples=@('modpack resource-pack enable "Fresh Animations" --position 1','modpack resource-pack move 4 --position 2','modpack resource-pack disable 4')
        }
        mod = [pscustomobject]@{
            Handler='Invoke-MpMod'; Group='CONTENT'; Summary='Manage mod-specific metadata'; Description='Change metadata that applies only to installed mods.'
            Usage=@('modpack mod set-side <client|host|both> <selector...> [options]')
            Items=@(New-MpHelpItem 'set-side <side> <selector...>' 'Set selected mods distribution side.'; New-MpHelpItem '-n, --dry-run' 'Prepare and validate without applying changes.')
            Notes=@('Packwiz content changes its .pw.toml source of truth; local JARs receive a project override.')
            Examples=@('modpack mod set-side client sodium','modpack -p vp26 mod set-side both 3 4')
        }
        build = [pscustomobject]@{
            Handler='Invoke-MpBuild'; Group='BUILD AND CONFIGURATION'; Summary='Generate an MRPack'; Description='Refresh project metadata and export a verified Modrinth .mrpack into dist/.'
            Usage=@('modpack build [options]')
            Items=@(New-MpHelpItem '--no-refresh' 'Skip the Packwiz refresh step; dependency validation still runs.'; New-MpHelpItem '--keep-old' 'Keep older .mrpack files in dist/.'; New-MpHelpItem '--open' 'Reveal the generated file in File Explorer.'; New-MpHelpItem '--raw-log' 'Show complete Packwiz output.'; New-MpHelpItem '--strict' 'Require complete verification and no known dependency conflicts.'; New-MpHelpItem '-n, --dry-run' 'Validate an export without replacing the latest build.')
            Notes=@('The dist/ artifact is not a source of truth.'); Examples=@('modpack build','modpack -p vp26 build --keep-old --open')
        }
        diff = [pscustomobject]@{ Handler='Invoke-MpDiff'; Group='BUILD AND CONFIGURATION'; Summary='Compare with the latest build'; Description='Compare the current project with its newest .mrpack in dist/.'; Usage=@('modpack diff'); Items=@(); Notes=@(); Examples=@('modpack diff','modpack -p vp26 diff') }
        doctor = [pscustomobject]@{
            Handler='Invoke-MpDoctor'; Group='BUILD AND CONFIGURATION'; Summary='Check and repair the environment'; Description='Check the environment and selected project; --fix previews safe repairs.'
            Usage=@('modpack doctor [options]')
            Items=@(New-MpHelpItem '--fix' 'Offer safe repairs for missing required components and project dependencies.'; New-MpHelpItem '-y, --yes' 'Accept recommended repair defaults. Requires --fix.'; New-MpHelpItem '--details' 'List every incomplete dependency verification grouped by cause.'; New-MpHelpItem '--strict' 'Require complete verification and no known dependency conflicts.'; New-MpHelpItem '--allow-downgrade' 'Allow dependency repair to select an older installed version.'; New-MpHelpItem '-n, --dry-run' 'Preview repairs without applying changes.')
            Notes=@('Doctor never installs Minecraft or rebuilds an outdated artifact.'); Examples=@('modpack doctor','modpack -p vp26 doctor --details','modpack doctor --fix -n')
        }
        config = [pscustomobject]@{
            Handler='Invoke-MpConfig'; Group='BUILD AND CONFIGURATION'; Summary='View or change global configuration'; Description='Read or change ModpackTools global configuration.'
            Usage=@('modpack config get <root|packwiz>','modpack config set root <directory>','modpack config set packwiz <executable|auto>')
            Items=@(New-MpHelpItem 'root' 'Directory whose direct children contain registered modpacks.'; New-MpHelpItem 'packwiz' 'Executable override. Use auto to restore discovery.')
            Notes=@(); Examples=@('modpack config get packwiz','modpack config set packwiz "C:\Tools\packwiz.exe"','modpack config set packwiz auto')
        }
        'self-update' = [pscustomobject]@{
            Handler='Invoke-MpSelfUpdate'; Group='BUILD AND CONFIGURATION'; Summary='Update ModpackTools itself'; Description='Check and install the latest stable official release while preserving configuration and theme.'
            Usage=@('modpack self-update [--yes]','modpack self-update --check')
            Items=@(New-MpHelpItem '--check' 'Query the latest stable release without installing it.'; New-MpHelpItem '-y, --yes' 'Install without interactive confirmation.')
            Notes=@('No project is required.','Package identity, official URL, SHA256, effective path, theme, and fresh-process help are verified.')
            Examples=@('modpack self-update --check','modpack self-update','modpack self-update -y')
        }
    }
    return $script:MpCommandCatalog
}

function Show-MpHelp {
    param([string]$Command)
    $catalog = Get-MpCommandCatalog
    if ($Command -and -not $catalog.Contains($Command.ToLowerInvariant())) { Throw-MpError -Message "Command '$Command' does not have a help page" -Hint 'modpack --help' -ErrorId 'Command.UnknownHelpTopic' -Category InvalidArgument }
    $commands = @(foreach ($name in $catalog.Keys) { $entry=$catalog[$name]; [pscustomobject]@{ Name=$name; Group=$entry.Group; Summary=$entry.Summary; Description=$entry.Description; Usage=$entry.Usage; Items=$entry.Items; Notes=$entry.Notes; Examples=$entry.Examples } })
    $view = [pscustomobject]@{
        Product='MODPACKTOOLS'; Version=$script:ModuleVersion; Description='Manage, inspect, update, and build Packwiz modpacks.'; Invocation='modpack'
        Groups=@('PROJECTS','CONTENT','BUILD AND CONFIGURATION'); Commands=$commands
        Usage=@('modpack [global options] <command> [operation] [arguments] [options]','modpack <command> --help','modpack --version')
        GlobalItems=@(New-MpHelpItem '-h, --help' 'Show top-level or command help.'; New-MpHelpItem '-V, --version' 'Print the loaded local version without network access.'; New-MpHelpItem '-p, --project <id>' 'Use this project instead of the active project; accepted in any position.'; New-MpHelpItem '--color auto|always|never' 'Control color; auto follows terminal detection and NO_COLOR.'; New-MpHelpItem '--ascii' 'Use ASCII symbols for presentation.'; New-MpHelpItem '--json' 'Emit a schema-versioned JSON envelope on stdout in addition to human presentation.'; New-MpHelpItem '--no-human' 'Suppress human presentation when --json is enabled. Requires --json.')
        Notes=@('Run modpack <command> --help for operations and examples.','Options use exact names; command prefixes are not accepted.','The Nushell adapter enables --json automatically and returns parsed structured values.')
    }
    [void](Test-R3HelpCatalogue $view -ExecutableCommands @($catalog.Keys))
    Write-R3Help (Get-MpConsole) $view $Command
}
