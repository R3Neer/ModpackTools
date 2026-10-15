use ../../Nushell/modpack.nu

def main [stub_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'fixture'
    let expected = {applied: false, dry_run: false, preview: false, change_count: 0, changes: []}
    if (modpack content update --all --no-human) != $expected {
        error make {msg: 'Native Nu empty update transaction mismatch'}
    }
    for arguments in [['content' 'add'] ['content' 'update' 'one' '--all']] {
        let failure = try { modpack ...$arguments --no-human; null } catch {|err| $err.msg }
        if $failure == null or $failure !~ 'ModpackTools.(Command.InvalidArguments|Option.ForbiddenCombination)' {
            error make {msg: 'Native Nu mutation failure was not preserved'}
        }
    }
    hide-env MODPACKTOOLS_PROJECT
    let missing = try { modpack content update --all --no-human; null } catch {|err| $err.msg }
    if $missing == null or $missing !~ 'ModpackTools.Project.NotSelected' {
        error make {msg: 'Native Nu mutation selected a stale project'}
    }
    $env.MODPACKTOOLS_PROJECT = 'fixture'
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-mutation-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let transaction = {applied: true, dry_run: false, preview: false, change_count: 1, changes: [{action: 'add', path: 'mods/example.pw.toml', reason: null}]}
    let good = {schema_version: 1, ok: true, command: 'content', arguments: ['add' 'example'], data: {transaction: $transaction}}
    $good | to json | save --force $response_path
    if (modpack content add example --no-human) != $transaction { error make {msg: 'Native Nu transaction unwrap changed its fields'} }
    for index in 0..5 {
        let reply = match $index {
            0 => ($good | update data.transaction.change_count '1')
            1 => ($good | update data.transaction.change_count 2)
            2 => ($good | update data.transaction.dry_run true)
            3 => ($good | update data.transaction.preview true)
            4 => ($good | update data.transaction.changes [{action: 'add', path: 'mods/example.pw.toml'}])
            _ => ($good | insert data.transaction.diagnostics null)
        }
        $reply | to json | save --force $response_path
        let failure = try { modpack content add example --no-human; null } catch {|err| $err.msg }
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'fixture' {
            error make {msg: 'Malformed transaction accepted or changed parent state'}
        }
    }
    let resource_transaction = {applied: false, dry_run: false, preview: false, change_count: 0, changes: []}
    let row = {number: null, id: 'vanilla', name: 'Vanilla', type: 'resourcepack', side: null, source: 'builtin', filename: null, category: null, pinned: false, state: 'active', priority: 1, version_id: null, project_id: null, slug: null}
    let resource = {schema_version: 1, ok: true, command: 'resource-pack', arguments: ['disable' 'example'], data: {transaction: $resource_transaction, resource_packs: [$row]}}
    $resource | to json | save --force $response_path
    if (modpack resource-pack disable example --no-human) != $resource_transaction { error make {msg: 'Native Nu resource transaction unwrap failed'} }
    for reply in [($resource | reject data.resource_packs) ($resource | update data.resource_packs.0.priority '1') ($resource | update data.resource_packs.0.pinned null)] {
        $reply | to json | save --force $response_path
        let failure = try { modpack resource-pack disable example --no-human; null } catch {|err| $err.msg }
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'fixture' { error make {msg: 'Malformed native resource inventory accepted or changed parent'} }
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    $env.PATH = $previous_path
    if $previous_project == null { hide-env --ignore-errors MODPACKTOOLS_PROJECT } else { $env.MODPACKTOOLS_PROJECT = $previous_project }
    print 'Native Nu update, mutation failures and six malformed transaction protections passed without PowerShell'
}
