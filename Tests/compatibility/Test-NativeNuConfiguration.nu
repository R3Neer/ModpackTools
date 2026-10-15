use ../../Nushell/modpack.nu

def main [stub_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'unrelated-active-project'
    let catalog = ($env.MODPACKTOOLS_CONFIG_HOME | path dirname | path join 'projects')
    for arguments in [['set' 'root' $catalog] ['get' 'root'] ['set' 'packwiz' 'auto'] ['get' 'packwiz']] {
        if (modpack config ...$arguments --no-human) != {} {error make {msg: 'Native config changed its empty machine payload'}}
    }
    if $env.MODPACKTOOLS_PROJECT != 'unrelated-active-project' {error make {msg: 'Global config mutated the active project'}}
    let failure = try {modpack config get root --project fixture --no-human; null} catch {|err| $err.msg}
    if $failure == null or $failure !~ 'ModpackTools.Option.ForbiddenCombination' {error make {msg: 'Native config accepted explicit project context'}}
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-config-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let good = {schema_version: 1, ok: true, command: 'config', arguments: ['get' 'root'], data: {}}
    $good | to json | save --force $response_path
    if (modpack config get root --no-human) != {} {error make {msg: 'Valid configuration peer rejected'}}
    for reply in [($good | update data []) ($good | update data {configuration: 'unexpected'})] {
        $reply | to json | save --force $response_path
        let failure = try {modpack config get root --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'unrelated-active-project' {error make {msg: 'Malformed configuration response accepted or changed parent'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu config get/set/auto, global scope and two malformed protections passed without PowerShell'
}
