use ../../Nushell/modpack.nu

def main [stub_executable: string] {
    let previous_path = $env.PATH
    let previous_config = $env.MODPACKTOOLS_CONFIG_HOME
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'help-must-not-resolve-this-project'
    $env.MODPACKTOOLS_CONFIG_HOME = ($nu.temp-dir | path join $'missing-help-config-((random uuid))')
    if (modpack --help --no-human) != {help: {command: null}} or (modpack --no-human) != {help: {command: null}} {error make {msg: 'Native global/default help changed structured output'}}
    for command in ['project' 'content' 'category' 'resource-pack' 'mod' 'build' 'diff' 'doctor' 'config' 'self-update'] {
        if (modpack $command --help --no-human) != {} {error make {msg: $'Native help failed for ($command)'}}
    }
    if (modpack --project 'missing' doctor -h --no-human) != {} {error make {msg: 'Help resolved global project context'}}
    if ($env.MODPACKTOOLS_CONFIG_HOME | path exists) {error make {msg: 'Help created a configuration directory'}}
    let good = (^$previous_core --help --json --no-human | from json)
    let response_path = ($nu.temp-dir | path join $'modpacktools-help-peer-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    for reply in [($good | update presentation.topic 'doctor') ($good | update presentation.help.commands.0.items.0.description null) ($good | update data {})] {
        $reply | to json | save --force $response_path
        let failure = try {modpack --help --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'help-must-not-resolve-this-project' {error make {msg: 'Malformed help accepted or session changed'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    $env.MODPACKTOOLS_CONFIG_HOME = $previous_config
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu default/global/ten-topic help and malformed catalogue guards passed without project/configuration lookup'
}
