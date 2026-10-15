use ../../Nushell/modpack.nu

def source-bytes [root: string] {
    ['pack.toml' 'index.toml' '.modpack/metadata.psd1' 'mods/side_fixture.jar'] | each {|relative|
        {path: $relative, hash: (open --raw ($root | path join $relative) | hash sha256)}
    }
}

def main [packwiz_executable: string] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    $env.PATH = []
    $env.MODPACKTOOLS_PROJECT = 'nu-created'
    modpack config set packwiz $packwiz_executable --no-human | ignore
    let root = (modpack project status --no-human).project.root
    let before = (source-bytes $root)
    let side = (modpack mod set-side client side_fixture.jar -n --no-human)
    if not $side.dry_run or $side.applied or $side.preview {error make {msg: 'Short dry-run side alias failed'}}
    let removal = (modpack content remove side_fixture.jar -y -n --no-human)
    if not $removal.dry_run or $removal.applied or $removal.preview {error make {msg: 'Short yes/dry-run removal aliases failed'}}
    let build = (modpack build -n --no-human)
    if ($build | columns) != ['health'] {error make {msg: 'Short dry-run build alias failed'}}
    if (modpack -V --no-human).version != '4.0.2' {error make {msg: 'Short version alias failed'}}
    if (source-bytes $root) != $before or $env.MODPACKTOOLS_PROJECT != 'nu-created' {error make {msg: 'Short aliases changed sources or session'}}
    modpack config set packwiz auto --no-human | ignore
    $env.PATH = $previous_path
    if $previous_project == null {hide-env --ignore-errors MODPACKTOOLS_PROJECT} else {$env.MODPACKTOOLS_PROJECT = $previous_project}
    print 'Native Nu short aliases passed with empty PATH and unchanged sources'
}
