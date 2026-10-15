use ../../Nushell/modpack.nu

def main [stub_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'unrelated-active-project'
    modpack config set packwiz $stub_executable --no-human | ignore
    if (modpack project create nu-created --name 'Nu Native Pack' --minecraft 1.21.1 --loader fabric --loader-version 0.16.10 --no-human) != {} {error make {msg: 'Creation empty machine contract changed'}}
    if $env.MODPACKTOOLS_PROJECT != 'unrelated-active-project' {error make {msg: 'Creation changed parent selection'}}
    let project = (modpack project list --no-human | where id == nu-created)
    if ($project | length) != 1 or $project.0.name != 'Nu Native Pack' or $project.0.loader_version != '0.16.10' {error make {msg: 'Created project does not resolve'}}
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-create-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let good = {schema_version: 1, ok: true, command: 'project', arguments: ['create' 'peer'], data: {}}
    $good | to json | save --force $response_path
    if (modpack project create peer --no-human) != {} {error make {msg: 'Valid creation peer rejected'}}
    for reply in [($good | update data []) ($good | update data {project: 'unexpected'})] {
        $reply | to json | save --force $response_path
        let failure = try {modpack project create peer --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'unrelated-active-project' {error make {msg: 'Malformed creation response accepted or changed session'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    modpack config set packwiz auto --no-human | ignore
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu creation/readback and malformed guards passed with empty PATH'
}
