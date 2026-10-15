use ../../Nushell/modpack.nu

def main [packwiz_executable: string, stub_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    $env.PATH = []
    modpack config set packwiz $packwiz_executable --no-human | ignore
    modpack project create doctor-repair --name 'Doctor fixture' --minecraft 1.21.1 --loader fabric --loader-version 0.16.10 --no-human | ignore
    $env.MODPACKTOOLS_PROJECT = 'doctor-repair'
    let root = (modpack project status --no-human).project.root
    'private repair fixture' | save ($root | path join 'fixture.txt')
    let paths = ['pack.toml' 'index.toml' 'fixture.txt']
    let before = ($paths | each {|path| open --raw ($root | path join $path) | hash sha256})
    let preview = (modpack doctor --fix --no-human)
    if $preview.repair.applied or not $preview.repair.preview or $preview.repair.change_count == 0 {error make {msg: 'Doctor did not propose the stale index repair'}}
    let dry = (modpack doctor --fix --yes --dry-run --no-human)
    if $dry.repair.applied or not $dry.repair.dry_run or $dry.repair.preview {error make {msg: 'Doctor ignored dry-run precedence'}}
    if ($paths | each {|path| open --raw ($root | path join $path) | hash sha256}) != $before {error make {msg: 'Doctor preview or dry-run changed the original project'}}
    let applied = (modpack doctor --fix --yes --no-human)
    if not $applied.repair.applied or not $applied.repair.project_applied or $applied.repair.changes != $preview.repair.changes {error make {msg: 'Doctor applied a different repair'}}
    let repeated = (modpack doctor --fix --yes --no-human)
    if $repeated.repair.applied or $repeated.repair.change_count != 0 {error make {msg: 'Doctor repeat was not idempotent'}}
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    let response_path = ($nu.temp-dir | path join $'modpacktools-doctor-repair-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let good = {schema_version: 1, ok: true, command: 'doctor', arguments: ['--fix' '--yes'], data: {doctor: $applied}}
    for reply in [($good | update data.doctor.repair.applied false) ($good | update data.doctor.repair.dry_run true) ($good | update data.doctor.repair.change_count 0)] {
        $reply | to json | save --force $response_path
        let failure = try {modpack doctor --fix --yes --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'doctor-repair' {error make {msg: 'Malformed repair accepted or session changed'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    modpack config set packwiz auto --no-human | ignore
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu doctor repair preview/dry-run/application/repeat passed with empty PATH'
}
