const ADAPTER = path self ../Nushell/modpack.nu
use $ADAPTER main

def ensure [condition: bool, message: string] {
    if not $condition { error make { msg: $message } }
}

def main [] {
    # Seed a stale-looking value without letting a failed child terminate this
    # test process. The structured envelope, not LAST_EXIT_CODE, is authoritative.
    $env.LAST_EXIT_CODE = 7

    # Default Nu usage keeps human R3CLI output on stderr while stdout remains
    # parseable structured data. This call deliberately does not use --no-human.
    let visible_version = (modpack --version --offline)
    ensure ((($visible_version | describe) =~ '^record')) 'Visible version output contaminated the Nu pipeline.'
    ensure (($visible_version.version? | default '') != '') 'Visible version record is missing its version value.'

    let quiet_version = (modpack --version --offline --no-human)
    ensure ((($quiet_version | describe) =~ '^record')) 'No-human version did not return a Nushell record.'
    ensure (($quiet_version.version? | default '') != '') 'No-human version record is missing its version value.'

    # --wrapped must pass --help through to ModpackTools rather than allowing Nu
    # to replace the canonical command catalogue with generated wrapper help.
    let help_result = (modpack --help --no-human)
    ensure ((($help_result | describe) =~ '^record')) 'Global help did not pass through the wrapped adapter.'

    let failed = try {
        modpack definitely-not-a-command --no-human | ignore
        false
    } catch {
        true
    }
    ensure $failed 'A structured ModpackTools error did not become a Nushell error.'

    # Build a minimal project so modpack use can be verified across separate
    # PowerShell bridge processes. Nu keeps the selection in an environment
    # variable, and each child PowerShell imports that inherited session state.
    let fixture_root = ($nu.temp-dir | path join $'modpacktools-nu-((random uuid))')
    let project_root = ($fixture_root | path join 'Nu Fixture')
    mkdir ($project_root | path join '.modpack')
    mkdir ($project_root | path join 'mods')
    mkdir ($project_root | path join 'config')
    mkdir ($project_root | path join 'resourcepacks')
    mkdir ($project_root | path join 'shaderpacks')

    [
        'name = "Technical nu-fixture"'
        'author = "Test"'
        'version = "0.1.0"'
        'pack-format = "packwiz:1.1.0"'
        ''
        '[index]'
        'file = "index.toml"'
        'hash-format = "sha256"'
        'hash = "test"'
        ''
        '[versions]'
        'fabric = "0.16.0"'
        'minecraft = "1.21.1"'
    ] | str join (char nl) | save --force ($project_root | path join 'pack.toml')

    'hash-format = "sha256"' | save --force ($project_root | path join 'index.toml')

    [
        '@{'
        '    SchemaVersion = 1'
        "    Id = 'nu-fixture'"
        "    DisplayName = 'Nu Fixture'"
        "    DisplayVersion = '1.0'"
        "    OutputName = 'nu-fixture.mrpack'"
        '}'
    ] | str join (char nl) | save --force ($project_root | path join '.modpack' 'project.psd1')

    [
        '@{'
        '    Categories = @{}'
        '    Mods = @{}'
        '    ResourcePacks = @{}'
        '}'
    ] | str join (char nl) | save --force ($project_root | path join '.modpack' 'metadata.psd1')

    let unicode_resource = '§-café.zip'
    '' | save --force ($project_root | path join 'resourcepacks' $unicode_resource)

    modpack config set root $fixture_root --no-human | ignore
    let selected = (modpack use nu-fixture --no-human)
    ensure (($selected.active_project? | default '') == 'nu-fixture') 'modpack use did not return the selected Nu project.'
    ensure (($env.MODPACKTOOLS_PROJECT? | default '') == 'nu-fixture') 'modpack use did not persist the project in the Nu session.'

    let queried = (modpack use --no-human)
    ensure (($queried.active_project? | default '') == 'nu-fixture') 'modpack use did not report the inherited Nu session project.'

    modpack use --help --no-human | ignore
    ensure (($env.MODPACKTOOLS_PROJECT? | default '') == 'nu-fixture') 'modpack use --help changed the active Nu project.'

    let status = (modpack status --no-human)
    ensure (($status.project.id? | default '') == 'nu-fixture') 'A later project command did not inherit the Nu session project.'

    # Exercise non-ASCII data in both directions. The search term travels through
    # JSON stdin and the matching filename returns through redirected JSON stdout.
    let unicode_inventory = (modpack inventory --type resourcepack --search 'café' --no-human)
    ensure (
        $unicode_inventory | any {|item|
            ($item.filename? | default '') == $unicode_resource
        }
    ) 'Non-ASCII bridge data was not preserved as UTF-8.'

    rm --recursive --force $fixture_root
    $env.LAST_EXIT_CODE = 0
    print 'Nushell adapter contract passed.'
}
