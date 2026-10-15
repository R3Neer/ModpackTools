use ../../Nushell/modpack.nu

def main [stub_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'fixture'
    let expected = [
        {number: 1, id: 'native-category', name: 'Native $ category', order: -7, mod_count: 0, unclassified: false}
        {number: 2, id: 'unclassified', name: 'UNCLASSIFIED', order: null, mod_count: 0, unclassified: true}
    ]
    if (modpack category create native-category --name 'Native $ category' --order=-7 --no-human) != $expected {
        error make {msg: 'Native category creation returned incorrect rows'}
    }
    if (modpack category list --no-human) != $expected { error make {msg: 'Native creation and later category list disagree'} }
    hide-env MODPACKTOOLS_PROJECT
    let failure = try {modpack category create another --order=bad --no-human; null} catch {|err| $err.msg}
    if $failure == null or $failure !~ 'ModpackTools.Project.NotSelected' {error make {msg: 'Category creation order validation won before selection'}}
    $env.MODPACKTOOLS_PROJECT = 'fixture'
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-category-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let project = {id: 'fixture', name: null, version: null, minecraft: null, loader: null, loader_version: null, root: 'fixture-root', output_name: 'fixture'}
    let good = {schema_version: 1, ok: true, command: 'category', arguments: ['create' 'native-category'], data: {categories: {project: $project, items: $expected}}}
    $good | to json | save --force $response_path
    if (modpack category create native-category --no-human) != $expected {error make {msg: 'Valid native category peer rejected'}}
    for reply in [($good | update data.categories.items.0.mod_count '0') ($good | update data.categories.items.0.number 0) ($good | insert data.diagnostics null)] {
        $reply | to json | save --force $response_path
        let failure = try {modpack category create native-category --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'fixture' {error make {msg: 'Malformed category response accepted or changed parent state'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu creation/list parity, priority and three malformed category protections passed without PowerShell'
}
