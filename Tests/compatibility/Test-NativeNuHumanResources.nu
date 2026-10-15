use ../../Nushell/modpack.nu

def bytes [root: string] {
    ['pack.toml' 'index.toml' '.modpack/metadata.psd1' 'config/defaultoptions-common.toml' 'mods/defaultoptions-fixture.jar' 'resourcepacks/alpha.zip' 'resourcepacks/beta.zip'] | each {|relative|
        let path = ($root | path join $relative)
        {path: $relative, hash: (if ($path | path exists) {open --raw $path | hash sha256} else {null})}
    }
}

def main [] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'nu-created'
    let pwd = $env.PWD
    let root = (modpack project status --no-human).project.root
    for tokens in [['resource-pack' 'enable' 'alpha.zip' 'beta.zip' '--position' '1'] ['resource-pack' 'move' 'beta.zip' '--position' '1'] ['resource-pack' 'disable' 'beta.zip']] {
        let before = (bytes $root)
        if (modpack ...$tokens --dry-run --ascii --color never) != null {error make {msg: 'Human resource dry-run leaked a pipeline value'}}
        if (bytes $root) != $before {error make {msg: 'Human resource dry-run changed source'}}
        if (modpack ...$tokens --ascii --color never) != null {error make {msg: 'Human resource operation leaked a pipeline value'}}
    }
    let resources = (modpack content list --type resourcepack --no-human)
    let active = ($resources | where state == active)
    let inactive = ($resources | where state == inactive)
    if ($active | length) != 1 or $active.0.filename != 'alpha.zip' or $active.0.priority != 1 or not ($inactive | any {|row| $row.filename == 'beta.zip'}) {error make {msg: 'Human resource order/readback mismatch'}}
    if $env.MODPACKTOOLS_PROJECT != 'nu-created' or $env.PATH != [] or $env.PWD != $pwd {error make {msg: 'Human resource operations changed parent session'}}
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native human resource enable/move/disable and dry-runs passed without PowerShell'
}
