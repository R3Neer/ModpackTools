use ../../Nushell/modpack.nu

def main [stub_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    for arguments in [['project' 'use' 'fixture'] ['PROJECT' 'USE' 'fixture'] ['project' 'USE' 'fixture'] ['PROJECT' 'use' 'fixture'] ['project' 'use' '--ascii' 'fixture']] {
        $env.MODPACKTOOLS_PROJECT = 'previous-stale'
        let result = (modpack ...$arguments --no-human)
        if $result != {active_project: 'fixture'} { error make {msg: 'Native Nu selected result mismatch'} }
        let expected = if $arguments.0 == 'project' and $arguments.1 == 'use' { 'fixture' } else { 'previous-stale' }
        if $env.MODPACKTOOLS_PROJECT != $expected { error make {msg: 'Nu parent update casing changed'} }
        if (modpack project current --no-human) != {active_project: $expected} { error make {msg: 'Use/current session roundtrip failed'} }
    }
    $env.MODPACKTOOLS_PROJECT = 'fixture'
    if (modpack project use ' ' --no-human) != {active_project: 'fixture'} { error make {msg: 'Whitespace session fallback changed'} }
    for selector in ['missing' ''] {
        let failure = try { modpack project use $selector --no-human; null } catch {|err| $err.msg }
        if $failure == null or $failure !~ 'ModpackTools.Project.' or $env.MODPACKTOOLS_PROJECT != 'fixture' { error make {msg: 'Use failure changed parent environment'} }
    }
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-reply-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    let good = {schema_version: 1, ok: true, command: 'project', arguments: ['use' 'fixture'], data: {active_project: 'fixture'}}
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    $good | to json | save --force $response_path
    if (modpack project use fixture --no-human) != {active_project: 'fixture'} {
        error make {msg: 'Native Nu response peer did not return its valid control'}
    }
    for index in 0..11 {
        $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = if $index == 7 { '1' } else { '0' }
        let reply = match $index {
            0 => ($good | update data.active_project null)
            1 => ($good | update data.active_project '')
            2 => ($good | update data.active_project 42)
            3 => ($good | update command 'content')
            4 => ($good | update arguments ['current' 'fixture'])
            5 => ($good | update arguments ['use'])
            6 => ($good | update data {})
            8 => ($good | update data.active_project ' ')
            9 => ($good | update command 'PROJECT')
            10 => ($good | update arguments ['USE' 'fixture'])
            11 => ($good | update arguments ['use' 'other'])
            _ => $good
        }
        $reply | to json | save --force $response_path
        let failure = try { modpack project use fixture --no-human; null } catch {|err| $err.msg }
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'fixture' { error make {msg: 'Malformed response accepted or changed session'} }
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    $env.PATH = $previous_path
    if $previous_project == null { hide-env --ignore-errors MODPACKTOOLS_PROJECT } else { $env.MODPACKTOOLS_PROJECT = $previous_project }
    print 'Native Nu use/current and 12 malformed response protections passed'
}
