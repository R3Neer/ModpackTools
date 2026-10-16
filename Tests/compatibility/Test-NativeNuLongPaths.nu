def main [package: path] {
    let root = ($nu.temp-dir | path join $'modpacktools-long-nu-((random uuid))')
    mut deep = $root
    while ($deep | str length) < 330 {
        $deep = ($deep | path join 'Unicode 雪 spaces and long directory segment')
    }
    mkdir $deep
    let target = ($deep | path join 'native application')
    cp --recursive $package $target
    let module = ($target | path join 'Nushell' 'modpack.nu')
    $env.MODPACKTOOLS_TEST_EXPECTED_VERSION = (open ($target | path join 'package.json') | get version)
    let code = (r###'use '__MODULE__'; let result = (modpack --version --no-human); if $result.version != $env.MODPACKTOOLS_TEST_EXPECTED_VERSION {error make {msg: 'Long-path packaged Nu version mismatch'}}; modpack --help | ignore'### | str replace '__MODULE__' ($module | str replace --all "'" "''"))
    hide-env --ignore-errors MODPACKTOOLS_CORE_PATH
    let capture = (^$nu.current-exe --no-config-file -c $code | complete)
    rm --recursive --force $root
    if $capture.exit_code != 0 {error make {msg: $'Long-path Nu bridge failed: ($capture.stderr)'}}
    print 'Packaged Nu bridge version and help pass from an executable path longer than 330 characters'
}
