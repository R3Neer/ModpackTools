use ../../Nushell/modpack.nu

def main [stub_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'nu-created'
    let preview = (modpack mod set-side host side_fixture.jar --dry-run --no-human)
    if $preview.applied or not $preview.dry_run or $preview.change_count != 1 {error make {msg: 'Side preview transaction mismatch'}}
    let applied = (modpack mod set-side host side_fixture.jar --no-human)
    if not $applied.applied or $applied.dry_run or $applied.change_count != 1 {error make {msg: 'Side applied transaction mismatch'}}
    let row = (modpack content list --type mod --no-human | where filename == side_fixture.jar | first)
    if $row.side != server or $env.MODPACKTOOLS_PROJECT != 'nu-created' {error make {msg: 'Effective side or parent selection mismatch'}}
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-side-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let transaction = {applied: true, dry_run: false, preview: false, change_count: 1, changes: [{action: 'change', path: '.modpack/metadata.psd1', reason: null}]}
    let good = {schema_version: 1, ok: true, command: 'mod', arguments: ['set-side' 'host' 'peer'], data: {transaction: $transaction}}
    $good | to json | save --force $response_path
    if (modpack mod set-side host peer --no-human) != $transaction {error make {msg: 'Valid mod-side transaction peer rejected'}}
    for reply in [($good | update data.transaction.change_count 2) ($good | update data.transaction.applied false)] {
        $reply | to json | save --force $response_path
        let failure = try {modpack mod set-side host peer --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'nu-created' {error make {msg: 'Malformed side response accepted or changed session'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu side preview/apply/readback and malformed guards passed with empty PATH'
}
