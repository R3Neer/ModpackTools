use ../../Nushell/modpack.nu

def main [stub_executable: string] {
    let root = ($nu.temp-dir | path join $'modpacktools-doctor-failure-((random uuid))')
    let configuration = ($root | path join configuration)
    mkdir $configuration
    let response = ($root | path join response.json)
    let target = ($root | path join 'retained evidence with spaces')
    {schema_version: 1, ok: false, command: doctor, arguments: ['--fix'], error: {id: 'ModpackTools.Dependency.BootstrapRetained', message: 'Controlled dependency preparation failure; evidence was retained.', category: InvalidOperation, target: $target}} | to json | save $response
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_CONFIG_HOME = $configuration
    $env.MODPACKTOOLS_STATE_HOME = ($root | path join state)
    $env.MODPACKTOOLS_PROJECT = 'preserve-parent'
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '1'
    $env.PATH = []
    let failure = try {modpack doctor --fix --yes --ascii --color never; null} catch {|err| $err.msg}
    if $failure == null or not ($failure | str contains $'Target: ($target)') or not ($failure | str contains 'ModpackTools.Dependency.BootstrapRetained') or $env.MODPACKTOOLS_PROJECT != 'preserve-parent' or $env.PATH != [] or ($root | path join state | path exists) {
        error make {msg: 'Doctor failure lost its exact retained-evidence target or continued applying'}
    }
    if ($root | path expand | path dirname) != ($nu.temp-dir | path expand) or not (($root | path basename) | str starts-with 'modpacktools-doctor-failure-') {error make {msg: 'Unsafe doctor failure fixture cleanup'}}
    rm --recursive --force $root
    print 'Native human doctor exposes exact retained-evidence paths and stops after a controlled environment failure'
}
