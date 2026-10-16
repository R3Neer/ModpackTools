use ../../Nushell/modpack.nu

def main [stub_executable: string, packwiz_executable: string] {
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
    modpack config set packwiz $packwiz_executable --no-human | ignore
    let old_http = $env.HTTP_PROXY?
    let old_https = $env.HTTPS_PROXY?
    let old_no_proxy = $env.NO_PROXY?
    $env.HTTP_PROXY = 'http://127.0.0.1:1'
    $env.HTTPS_PROXY = 'http://127.0.0.1:1'
    $env.NO_PROXY = ''
    let creation = ['project' 'create' 'nu-human-created' '--name' 'Nu Human Native Pack' '--path' 'human project with spaces' '--minecraft' '1.21.1' '--loader' 'fabric' '--loader-version' '0.16.10']
    let blocked = (try {modpack ...$creation --ascii --color never; null} catch {|err| $err.msg})
    let catalog = ($project.0.root | path dirname)
    let retained = (ls --all $catalog | any {|entry| $entry.name | path basename | str starts-with '.modpacktools-create-'})
    if $blocked == null or $blocked !~ 'ModpackTools.Build.ProcessFailed' or ($catalog | path join 'human project with spaces' | path exists) or $retained {error make {msg: 'Blocked Packwiz metadata request did not fail before publication with clean workspace'}}
    if $old_http == null {hide-env --ignore-errors HTTP_PROXY} else {$env.HTTP_PROXY = $old_http}
    if $old_https == null {hide-env --ignore-errors HTTPS_PROXY} else {$env.HTTPS_PROXY = $old_https}
    if $old_no_proxy == null {hide-env --ignore-errors NO_PROXY} else {$env.NO_PROXY = $old_no_proxy}
    # Real Go init currently reads Mojang metadata even with explicit versions.
    # This successful call permits that read; it is not an offline acceptance.
    if (modpack ...$creation --ascii --color never) != null {error make {msg: 'Human project creation leaked a pipeline value'}}
    let human_project = (modpack project list --no-human | where id == nu-human-created)
    if ($human_project | length) != 1 or $human_project.0.name != 'Nu Human Native Pack' or $human_project.0.loader_version != '0.16.10' or ($human_project.0.root | path basename) != 'human project with spaces' or $env.MODPACKTOOLS_PROJECT != 'unrelated-active-project' {error make {msg: 'Human project creation/readback or session mismatch'}}
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
