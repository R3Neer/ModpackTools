use ../../Nushell/modpack.nu

def main [stub_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'unrelated-active-project'
    let arity = (^$env.MODPACKTOOLS_CORE_PATH project register a b --json --no-human | complete)
    let arity_result = ($arity.stdout | from json)
    if $arity.exit_code != 1 or $arity_result.error.target != ['a' 'b'] {error make {msg: 'Registration arity lost its target array'}}
    let catalog = ($env.MODPACKTOOLS_CONFIG_HOME | path dirname | path join 'projects')
    let pack = ($catalog | path join 'new registered pack á')
    mkdir $pack
    'name="Native fixture"
version="1.2"
[index]
file="index.toml"
[versions]
minecraft="1.21"
fabric="0.16"
' | save ($pack | path join 'pack.toml')
    'hash-format="sha256"
files=[]
' | save ($pack | path join 'index.toml')
    let manifest = (open --raw ($pack | path join 'pack.toml'))
    if (modpack project register registered --path $pack --display-name 'Display 雪' --no-human) != {} {error make {msg: 'Registration empty machine contract changed'}}
    if $env.MODPACKTOOLS_PROJECT != 'unrelated-active-project' or (open --raw ($pack | path join 'pack.toml')) != $manifest {error make {msg: 'Registration changed session or technical manifest'}}
    let registered = (modpack project list --no-human | where id == registered)
    if ($registered | length) != 1 or $registered.0.name != 'Display 雪' or $registered.0.root != $pack {error make {msg: 'Registered project does not resolve'}}
    let human_pack = ($catalog | path join 'human existing pack 雪')
    mkdir $human_pack
    $manifest | save ($human_pack | path join 'pack.toml')
    'hash-format="sha256"
files=[]
' | save ($human_pack | path join 'index.toml')
    'Keep my README 雪' | save ($human_pack | path join 'README.md')
    'custom-ignore/' | save ($human_pack | path join '.gitignore')
    if (modpack project register human-registered --path $human_pack --display-name 'Human 雪' --ascii --color never) != null {error make {msg: 'Human registration leaked pipeline output'}}
    if $env.MODPACKTOOLS_PROJECT != 'unrelated-active-project' or (open --raw ($human_pack | path join 'pack.toml')) != $manifest or (open --raw ($human_pack | path join 'README.md')) != 'Keep my README 雪' or (open --raw ($human_pack | path join '.gitignore')) != 'custom-ignore/' {error make {msg: 'Human registration changed preserved inputs/session'}}
    let human = (modpack project list --no-human | where id == human-registered)
    if ($human | length) != 1 or $human.0.name != 'Human 雪' or $human.0.root != $human_pack {error make {msg: 'Human registration readback failed'}}
    let response_path = ($nu.temp-dir | path join $'modpacktools-nu-register-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub_executable
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    let good = {schema_version: 1, ok: true, command: 'project', arguments: ['register' 'peer'], data: {}}
    $good | to json | save --force $response_path
    if (modpack project register peer --no-human) != {} {error make {msg: 'Valid registration peer rejected'}}
    for reply in [($good | update data []) ($good | update data {project: 'unexpected'})] {
        $reply | to json | save --force $response_path
        let failure = try {modpack project register peer --no-human; null} catch {|err| $err.msg}
        if $failure == null or $env.MODPACKTOOLS_PROJECT != 'unrelated-active-project' {error make {msg: 'Malformed registration response accepted or changed session'}}
    }
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu registration/readback and malformed guards passed with empty PATH'
}
