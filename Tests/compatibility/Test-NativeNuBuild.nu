use ../../Nushell/modpack.nu

def main [stub_executable: string, packwiz_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'nu-created'
    modpack config set packwiz $packwiz_executable --no-human | ignore
    let dry = (modpack build --dry-run --no-human)
    if ($dry | columns) != ['health'] {error make {msg: 'Build dry-run must return only health'}}
    let applied = (modpack build --no-human)
    if $applied.dry_run or $applied.size_bytes < 1 or not ($applied.path | path exists) or $env.MODPACKTOOLS_PROJECT != 'nu-created' {
        error make {msg: 'Native Nu build failed or selection changed'}
    }
    let project_root = (modpack project list --no-human | where id == nu-created | first | get root)
    let before = (open --raw ($project_root | path join 'pack.toml'))
    if (modpack build --dry-run --ascii --color never) != null {error make {msg: 'Human build dry-run leaked output'}}
    if (open --raw ($project_root | path join 'pack.toml')) != $before {error make {msg: 'Human build dry-run changed manifest'}}
    if (modpack build --ascii --color never) != null or $env.MODPACKTOOLS_PROJECT != 'nu-created' {error make {msg: 'Human build leaked output or changed selection'}}
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-build-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let good = {schema_version: 1, ok: true, command: 'build', arguments: ['--dry-run'], data: {health: {errors: [], warnings: [], unknown: []}}}
    $good | to json | save --force $response_path
    if (modpack build --dry-run --no-human) != $good.data {error make {msg: 'Valid dry-run peer rejected'}}
    for reply in [($good | insert data.build $applied) ($good | update data.health.errors 'invalid') ($good | update command 'diff')] {
        $reply | to json | save --force $response_path
        let failure = try {modpack build --dry-run --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'nu-created' {error make {msg: 'Malformed build accepted or selection changed'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    modpack config set packwiz auto --no-human | ignore
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu build dry-run/apply and malformed guards passed with empty PATH'
}
