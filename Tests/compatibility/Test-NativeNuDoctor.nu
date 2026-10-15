use ../../Nushell/modpack.nu

def main [stub_executable: string, packwiz_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'nu-created'
    modpack config set packwiz $packwiz_executable --no-human | ignore
    let root = (modpack project status --no-human).project.root
    let paths = ['pack.toml' 'index.toml' '.modpack/project.psd1' '.modpack/metadata.psd1']
    let before = ($paths | each {|path| open --raw ($root | path join $path) | hash sha256})
    let report = (modpack doctor --details --no-human)
    if ($report.checks | where label == 'PowerShell' | length) != 0 {error make {msg: 'Native doctor still requires PowerShell'}}
    if ($report.checks | where label == 'Invocation' | get value) != ['Working'] {error make {msg: 'Native doctor did not execute configured Packwiz'}}
    if ($report.checks | where label == 'Dependencies' | get value) != ['Healthy'] {error make {msg: 'Native doctor did not inspect selected project'}}
    if ($paths | each {|path| open --raw ($root | path join $path) | hash sha256}) != $before {error make {msg: 'Native doctor changed project sources'}}
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-doctor-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let good = {schema_version: 1, ok: true, command: 'doctor', arguments: ['--details'], data: {doctor: $report}}
    $good | to json | save --force $response_path
    if (modpack doctor --details --no-human) != $report {error make {msg: 'Valid diagnostic peer rejected'}}
    for reply in [($good | update data.doctor.failures ($report.failures + 1)) ($good | update data.doctor.checks.0.detail null) ($good | update data.doctor.checks.0.status 'unknown')] {
        $reply | to json | save --force $response_path
        let failure = try {modpack doctor --details --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'nu-created' {error make {msg: 'Malformed doctor accepted or selection changed'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    modpack config set packwiz auto --no-human | ignore
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu doctor diagnostics and malformed guards passed with empty PATH'
}
