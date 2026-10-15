const MODULE_DIR = path self .
const PACKAGE = ($MODULE_DIR | path join '../presentation/r3cli' | path expand)
const RENDERER = ($MODULE_DIR | path join 'Render-Native.nu')

def validate-package [] {
    let manifest_path = ($PACKAGE | path join 'manifest.json')
    if not ($manifest_path | path exists) {error make {msg: 'Native R3CLI presentation package is missing.'}}
    if (ls $manifest_path | first | get size | into int) > 262144 {error make {msg: 'R3CLI manifest exceeds its quota.'}}
    let manifest = (open $manifest_path)
    if $manifest.schema_version != 1 or $manifest.revision != '18df65b7eff51bc0c893ec687161654ea378ee7f' or $manifest.version != '0.5.2' or ($manifest.files | columns | sort) != ['LICENSE' 'hashes.json' 'mod.nu' 'resources.json'] {error make {msg: 'R3CLI presentation manifest is invalid.'}}
    for entry in ($manifest.files | transpose name expected) {
        let path = ($PACKAGE | path join $entry.name)
        if not ($path | path exists) or (ls $path | first | get size | into int) > 8388608 {error make {msg: $'R3CLI package file is missing or too large: ($entry.name)'}}
        if (open --raw $path | hash sha256 | str uppercase) != ($entry.expected | str uppercase) {error make {msg: $'R3CLI package hash mismatch: ($entry.name)'}}
    }
}

export def render [envelope: record, args: list<string>] {
    if $envelope.command == '--version' {
        print --stderr $'ModpackTools ($envelope.data.version.version)'
        return
    }
    validate-package
    mut colour = 'auto'
    for index in 0..<($args | length) {
        let token = ($args | get $index)
        if ($token | str starts-with '--color=') {$colour = ($token | str substring 8..)}
        if $token == '--color' {$colour = ($args | get ($index + 1))}
    }
    let configuration = ($env.MODPACKTOOLS_CONFIG_HOME? | default ($env.LOCALAPPDATA | path join 'ModpackTools'))
    let width = (try {term size | get columns} catch {80})
    let request_path = ($nu.temp-dir | path join $'modpacktools-human-((random uuid)).json')
    let request = {envelope: $envelope, colour: ($colour | str lowercase), ascii: ('--ascii' in $args), terminal: (is-terminal --stderr), width: (if $width > 0 {$width} else {80}), theme_path: ($configuration | path join 'theme.toml')}
    $request | to json | save --force $request_path
    let capture = try {^$nu.current-exe --no-config-file $RENDERER $request_path | complete} catch {|err|
        rm --force $request_path
        error make {msg: $'Native presentation could not start: ($err.msg)'}
    }
    rm --force $request_path
    if $capture.exit_code != 0 or $capture.stdout != '' {error make {msg: $'Native presentation failed: ($capture.stderr)'}}
    if $capture.stderr != '' {print --stderr --no-newline $capture.stderr}
}
