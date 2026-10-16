const SCRIPT_DIR = path self .

# Convenience entrypoint for a source checkout or extracted native setup.
# Runtime installation is performed by the native helper; configuration is manual.
def main [
    --root: string
    --package: path
    --sha256: string
    --setup: path
    --state: path
    --json
] {
    if $root == null or $package == null or $sha256 == null {
        error make {msg: 'Supply --root NEW_DIRECTORY --package NATIVE_ZIP --sha256 TRUSTED_SHA256. Verify the archive before executing its extracted native setup.'}
    }
    let helper = if $setup == null { $SCRIPT_DIR | path join 'modpacktools-setup.exe' } else {$setup}
    if not ($helper | path exists) or ($helper | path type) != 'file' {
        error make {msg: $'Native setup executable not found: ($helper). Extract a trusted native package and pass --setup; no shell will be installed.'}
    }
    mut forwarded = ['install' '--root' $root '--nu' $nu.current-exe '--package' $package '--sha256' $sha256]
    if $state != null {$forwarded = ($forwarded | append ['--state' $state])}
    if $json {$forwarded = ($forwarded | append '--json')}
    ^$helper ...$forwarded
    if $env.LAST_EXIT_CODE != 0 {error make {msg: $'Native installation failed with exit code ($env.LAST_EXIT_CODE); preserve any reported recovery paths.'}}
}
