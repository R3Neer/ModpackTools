def main [package: path] {
    $env.PATH = []
    hide-env --ignore-errors MODPACKTOOLS_CORE_PATH
    $env.MODPACKTOOLS_TEST_EXPECTED_VERSION = (open ($package | path join 'package.json') | get version)
    let module = ($package | path join 'Nushell' 'modpack.nu')
    let script = (r###'use '__MODULE__'; let value = (modpack --version --no-human); if $value.version != $env.MODPACKTOOLS_TEST_EXPECTED_VERSION {error make {msg: 'Packaged default native executable failed'}}; let failure = (try {modpack deliberately-unknown --no-human; null} catch {|err| $err.msg}); if $failure == null or $failure !~ 'Command.Unknown' {error make {msg: 'Unknown command did not stay native'}}'### | str replace '__MODULE__' ($module | str replace --all "'" "''"))
    ^$nu.current-exe --no-config-file -c $script
    if $env.LAST_EXIT_CODE != 0 {error make {msg: 'Default packaged native routing failed'}}
    $env.MODPACKTOOLS_CORE_PATH = ($package | path join 'missing-native.exe')
    let missing = (r###'use '__MODULE__'; let failure = (try {modpack --version --no-human; null} catch {|err| $err.msg}); if $failure == null or $failure !~ 'missing-native.exe' or $failure !~ 'Reinstall a complete native package' {error make {msg: 'Missing executable lacks actionable native diagnostic'}}'### | str replace '__MODULE__' ($module | str replace --all "'" "''"))
    ^$nu.current-exe --no-config-file -c $missing
    if $env.LAST_EXIT_CODE != 0 {error make {msg: 'Missing native executable diagnostic failed'}}
    print 'Packaged default, unknown command and missing executable pass with empty PATH and no PowerShell bridge'
}
