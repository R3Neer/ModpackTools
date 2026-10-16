use ../../Nushell/modpack.nu

def main [stub_executable: string, packwiz_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'nu-created'
    modpack config set packwiz $packwiz_executable --no-human | ignore
    let preview = (modpack content remove side_fixture.jar --no-human)
    if $preview.applied or $preview.dry_run or not $preview.preview or $preview.change_count < 1 {error make {msg: 'Removal preview transaction mismatch'}}
    let dry = (modpack content remove side_fixture.jar --yes --dry-run --no-human)
    if $dry.applied or not $dry.dry_run or $dry.preview or $dry.change_count != $preview.change_count {error make {msg: 'Removal dry-run transaction mismatch'}}
    if (modpack content list --type mod --no-human | where filename == side_fixture.jar | length) != 1 {error make {msg: 'Preview or dry-run deleted source'}}
    let applied = (modpack content remove side_fixture.jar --yes --no-human)
    if not $applied.applied or $applied.dry_run or $applied.preview or $applied.change_count != $preview.change_count {error make {msg: 'Removal application mismatch'}}
    if (modpack content list --type mod --no-human | where filename == side_fixture.jar | length) != 0 or $env.MODPACKTOOLS_PROJECT != 'nu-created' {error make {msg: 'Removed file retained or parent selection changed'}}
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-removal-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let transaction = {applied: false, dry_run: false, preview: true, change_count: 1, changes: [{action: 'remove', path: 'mods/peer.jar', reason: null}]}
    let good = {schema_version: 1, ok: true, command: 'content', arguments: ['remove' 'peer'], data: {transaction: $transaction}}
    $good | to json | save --force $response_path
    if (modpack content remove peer --no-human) != $transaction {error make {msg: 'Valid removal preview peer rejected'}}
    for reply in [($good | update data.transaction.preview false) ($good | update data.transaction.applied true) ($good | update data.transaction.dry_run true)] {
        $reply | to json | save --force $response_path
        let failure = try {modpack content remove peer --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'nu-created' {error make {msg: 'Contradictory removal accepted or selection changed'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    modpack config set packwiz auto --no-human | ignore
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu removal preview/dry-run/apply and malformed guards passed with empty PATH'
}
