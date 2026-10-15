use ../../Nushell/modpack.nu

def main [stub_executable: string, packwiz_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'nu-created'
    modpack config set packwiz $packwiz_executable --no-human | ignore
    let current = (modpack diff --no-human)
    let ghost = {kind: null, path: null}
    if $current != {total: 0, added: $ghost, changed: $ghost, removed: $ghost} or $env.MODPACKTOOLS_PROJECT != 'nu-created' {
        error make {msg: 'Native Nu unchanged diff shape mismatch'}
    }
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-diff-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let good = {schema_version: 1, ok: true, command: 'diff', arguments: [], data: {diff: $current}}
    $good | to json | save --force $response_path
    if (modpack diff --no-human) != $current {error make {msg: 'Valid diff peer rejected'}}
    for reply in [($good | update data.diff.total 1) ($good | update data.diff.added {kind: 'CONFIG', path: null}) ($good | insert data.diagnostics 'invalid')] {
        $reply | to json | save --force $response_path
        let failure = try {modpack diff --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'nu-created' {error make {msg: 'Malformed diff accepted or selection changed'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    modpack config set packwiz auto --no-human | ignore
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu diff and malformed guards passed with empty PATH'
}
